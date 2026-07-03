using System;
using System.Threading;
using System.Threading.Tasks;
using VisionInspection.Core.Abstractions;
using VisionInspection.Core.Inspection;
using VisionInspection.Core.Models;

namespace VisionInspection.Plc.Handshake
{
    /// <summary>
    /// 检测握手状态机（含型号码切换）。轮询触发位边沿驱动一次检测周期：
    /// <para>触发上升沿 → 读型号码加载配方（无则错误码 + NG）→ 置忙 → 取像检测 → 写缺件位图 + OK/NG → 置完成清忙；</para>
    /// <para>触发下降沿（PLC 读走结果并清触发）→ 清完成位。</para>
    /// 检测执行由注入的委托完成，从而与相机 / 检测引擎解耦，便于单元测试。
    /// </summary>
    public sealed class HandshakeController
    {
        private readonly IPlcClient _plc;
        private readonly IRecipeStore _store;
        private readonly Func<Recipe, CancellationToken, InspectionResult> _inspect;
        private readonly HandshakeAddressMap _map;
        private readonly int _inspectTimeoutMs;
        private bool _lastTrigger;
        private bool _heartbeat;
        private int _cycleInFlight;
        private short? _lastCompletedSequence;

        public event Action<InspectionResult> Inspected;
        public event Action<string> Log;

        public HandshakeController(
            IPlcClient plc,
            IRecipeStore store,
            Func<Recipe, InspectionResult> inspect,
            HandshakeAddressMap map = null,
            int inspectTimeoutMs = 0)
            : this(plc, store, (recipe, token) => inspect(recipe), map, inspectTimeoutMs)
        {
        }

        public HandshakeController(
            IPlcClient plc,
            IRecipeStore store,
            Func<Recipe, CancellationToken, InspectionResult> inspect,
            HandshakeAddressMap map = null,
            int inspectTimeoutMs = 0)
        {
            _plc = plc ?? throw new ArgumentNullException(nameof(plc));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _inspect = inspect ?? throw new ArgumentNullException(nameof(inspect));
            _map = map ?? new HandshakeAddressMap();
            _inspectTimeoutMs = inspectTimeoutMs; // 0 = 同步、不超时(便于单元测试确定性)
        }

        public HandshakeAddressMap Map => _map;

        /// <summary>处理一次轮询；返回本次是否执行了检测。</summary>
        public bool ProcessOnce()
            => ProcessOnce(CancellationToken.None);

        /// <summary>处理一次轮询；返回本次是否执行了检测。</summary>
        public bool ProcessOnce(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            bool trigger = _plc.ReadBool(_map.TriggerBit);
            bool rising = trigger && !_lastTrigger;
            bool falling = !trigger && _lastTrigger;

            if (falling)
            {
                _plc.WriteBool(_map.DoneBit, false); // PLC 已读走结果 → 复位完成位
                _lastTrigger = false;
                return false;
            }
            if (!rising) return false;

            try
            {
                bool resultWritten;
                bool ran = ExecuteCycle(cancellationToken, out resultWritten, out var sequence);
                if (resultWritten)
                {
                    if (_map.UseSequence && sequence.HasValue)
                        _plc.WriteInt16(_map.AckSequenceWord, sequence.Value);
                    _plc.WriteBool(_map.DoneBit, true);
                    if (_map.UseSequence && sequence.HasValue)
                        _lastCompletedSequence = sequence.Value;
                }
                if (ran) _lastTrigger = true;
                return ran;
            }
            finally
            {
                try { _plc.WriteBool(_map.BusyBit, false); } catch { }
            }
        }

        private bool ExecuteCycle(CancellationToken cancellationToken, out bool resultWritten, out short? sequence)
        {
            resultWritten = false;
            sequence = null;
            if (Interlocked.CompareExchange(ref _cycleInFlight, 1, 0) != 0)
            {
                resultWritten = WriteErrorResult("?", PlcErrorCode.Internal, "上一检测周期尚未结束，拒绝本次触发。");
                return true;
            }

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                string modelCode = ((ushort)_plc.ReadInt16(_map.ModelCodeWord)).ToString();
                if (_map.UseSequence)
                {
                    sequence = _plc.ReadInt16(_map.RequestSequenceWord);
                    if (_lastCompletedSequence.HasValue && _lastCompletedSequence.Value == sequence.Value)
                        return false;
                }

                _plc.WriteBool(_map.DoneBit, false);
                _plc.WriteBool(_map.BusyBit, true);
                ClearResultArea();

                if (!_store.TryLoad(modelCode, out var recipe))
                {
                    resultWritten = WriteErrorResult(modelCode, PlcErrorCode.NoRecipe, $"无匹配配方：型号码 {modelCode}");
                    return true;
                }
                try
                {
                    RecipeValidator.Validate(recipe, _map.DefectBitmapWordCount);
                }
                catch (Exception ex)
                {
                    resultWritten = WriteErrorResult(modelCode, PlcErrorCode.Internal, "配方校验失败：" + ex.Message);
                    return true;
                }

                InspectionResult result;
                try
                {
                    result = InvokeInspect(recipe, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    resultWritten = false;
                    return true;
                }
                catch (Exception ex)
                {
                    resultWritten = WriteErrorResult(modelCode, PlcErrorCode.Internal, "检测异常：" + ex.Message);
                    return true;
                }

                if (result.Outcome == InspectionOutcome.Error)
                {
                    resultWritten = WriteErrorResult(modelCode, PlcErrorCode.FromInspection(result.ErrorCode), result.Message);
                    return true;
                }

                ushort[] words;
                try
                {
                    words = DefectBitmap.Encode(result.Stations, _map.DefectBitmapWordCount, unknownAsDefect: true);
                }
                catch (Exception ex)
                {
                    resultWritten = WriteErrorResult(modelCode, PlcErrorCode.Internal, "结果编码异常：" + ex.Message);
                    return true;
                }

                _plc.WriteUInt16(_map.DefectBitmapWord, words);
                _plc.WriteInt16(_map.ErrorCodeWord, PlcErrorCode.None);

                bool ok = result.Outcome == InspectionOutcome.Ok;
                _plc.WriteBool(_map.OkBit, ok);
                _plc.WriteBool(_map.NgBit, !ok);
                resultWritten = true;

                SafeRaiseInspected(result);
                SafeLog($"检测完成 型号={modelCode} 结论={(ok ? "OK" : "NG")} 缺件={result.MissingCount}");
                return true;
            }
            catch (Exception ex)
            {
                SafeLog("握手周期异常：" + ex.Message);
                resultWritten = false;
                throw;
            }
            finally
            {
                // 状态 2 表示检测已超时但后台任务还没退出，必须继续拒绝新周期。
                Interlocked.CompareExchange(ref _cycleInFlight, 0, 1);
            }
        }

        /// <summary>执行检测,带单周期超时。超时抛 <see cref="TimeoutException"/>,由上层写错误码+清忙,避免整线死等。</summary>
        private InspectionResult InvokeInspect(Recipe recipe, CancellationToken cancellationToken)
        {
            if (_inspectTimeoutMs <= 0) return _inspect(recipe, cancellationToken); // 同步路径(测试)

            using (var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                var token = cts.Token;
                var task = Task.Run(() => _inspect(recipe, token), token);
                bool done;
                try { done = task.Wait(_inspectTimeoutMs); }
                catch (AggregateException ae) { throw ae.InnerException ?? ae; } // 展开检测内部异常
                if (!done)
                {
                    cts.Cancel();
                    Interlocked.Exchange(ref _cycleInFlight, 2);
                    task.ContinueWith(t =>
                    {
                        if (t.IsFaulted && t.Exception != null)
                        {
                            var ignored = t.Exception;
                            SafeLog("超时后台任务已退出：异常已观察 " + (t.Exception.InnerException?.GetType().Name ?? t.Exception.GetType().Name));
                        }
                        else if (t.IsCanceled)
                        {
                            SafeLog("超时后台任务已退出：已取消");
                        }
                        else
                        {
                            SafeLog("超时后台任务已退出");
                        }
                        Interlocked.Exchange(ref _cycleInFlight, 0);
                    });
                    throw new TimeoutException($"检测超过 {_inspectTimeoutMs}ms 未返回(相机/检测可能卡死)");
                }
                return task.Result;
            }
        }

        private void ClearResultArea()
        {
            _plc.WriteUInt16(_map.DefectBitmapWord, new ushort[_map.DefectBitmapWordCount]);
            _plc.WriteInt16(_map.ErrorCodeWord, PlcErrorCode.None);
            _plc.WriteBool(_map.OkBit, false);
            _plc.WriteBool(_map.NgBit, false);
        }

        private bool WriteErrorResult(string modelCode, short errorCode, string message)
        {
            _plc.WriteUInt16(_map.DefectBitmapWord, new ushort[_map.DefectBitmapWordCount]);
            _plc.WriteInt16(_map.ErrorCodeWord, errorCode);
            _plc.WriteBool(_map.OkBit, false);
            _plc.WriteBool(_map.NgBit, true);
            SafeLog($"检测异常 型号={modelCode} 错误码={errorCode} {message}");
            return true;
        }

        public void ResetOutputs()
        {
            _plc.WriteBool(_map.BusyBit, false);
            _plc.WriteBool(_map.DoneBit, false);
            ClearResultArea();
            _lastTrigger = false;
            _lastCompletedSequence = null;
        }

        private void SafeRaiseInspected(InspectionResult result)
        {
            var handlers = Inspected;
            if (handlers == null) return;
            foreach (Action<InspectionResult> h in handlers.GetInvocationList())
            {
                try { h(result); }
                catch (Exception ex) { SafeLog("检测事件订阅者异常：" + ex.Message); }
            }
        }

        private void SafeLog(string message)
        {
            var handlers = Log;
            if (handlers == null) return;
            foreach (Action<string> h in handlers.GetInvocationList())
            {
                try { h(message); }
                catch { }
            }
        }

        /// <summary>翻转心跳位（由运行层定时调用，供 PLC 判活）。</summary>
        public void ToggleHeartbeat()
        {
            _heartbeat = !_heartbeat;
            _plc.WriteBool(_map.HeartbeatBit, _heartbeat);
        }
    }
}
