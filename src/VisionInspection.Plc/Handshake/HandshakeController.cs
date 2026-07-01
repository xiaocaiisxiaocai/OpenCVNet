using System;
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
        private readonly Func<Recipe, InspectionResult> _inspect;
        private readonly HandshakeAddressMap _map;
        private readonly int _inspectTimeoutMs;
        private bool _lastTrigger;
        private bool _heartbeat;

        public event Action<InspectionResult> Inspected;
        public event Action<string> Log;

        public HandshakeController(
            IPlcClient plc,
            IRecipeStore store,
            Func<Recipe, InspectionResult> inspect,
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
        {
            bool trigger = _plc.ReadBool(_map.TriggerBit);
            bool rising = trigger && !_lastTrigger;
            bool falling = !trigger && _lastTrigger;
            _lastTrigger = trigger;

            if (falling)
            {
                _plc.WriteBool(_map.DoneBit, false); // PLC 已读走结果 → 复位完成位
                return false;
            }
            if (!rising) return false;

            _plc.WriteBool(_map.BusyBit, true);
            _plc.WriteBool(_map.DoneBit, false);
            try
            {
                return ExecuteCycle();
            }
            finally
            {
                _plc.WriteBool(_map.DoneBit, true);
                _plc.WriteBool(_map.BusyBit, false);
            }
        }

        private bool ExecuteCycle()
        {
            string modelCode = ((ushort)_plc.ReadInt16(_map.ModelCodeWord)).ToString();

            if (!_store.TryLoad(modelCode, out var recipe))
            {
                WriteErrorResult(modelCode, PlcErrorCode.NoRecipe, $"无匹配配方：型号码 {modelCode}");
                return true;
            }

            InspectionResult result;
            try
            {
                result = InvokeInspect(recipe);
            }
            catch (Exception ex)
            {
                WriteErrorResult(modelCode, PlcErrorCode.Internal, "检测异常：" + ex.Message);
                return true;
            }

            if (result.Outcome == InspectionOutcome.Error)
            {
                WriteErrorResult(modelCode, PlcErrorCode.FromInspection(result.ErrorCode), result.Message);
                return true;
            }

            var words = DefectBitmap.Encode(result.Stations, _map.DefectBitmapWordCount, unknownAsDefect: true);
            _plc.WriteUInt16(_map.DefectBitmapWord, words);
            _plc.WriteInt16(_map.ErrorCodeWord, PlcErrorCode.None);

            bool ok = result.Outcome == InspectionOutcome.Ok;
            _plc.WriteBool(_map.OkBit, ok);
            _plc.WriteBool(_map.NgBit, !ok);

            Inspected?.Invoke(result);
            Log?.Invoke($"检测完成 型号={modelCode} 结论={(ok ? "OK" : "NG")} 缺件={result.MissingCount}");
            return true;
        }

        /// <summary>执行检测,带单周期超时。超时抛 <see cref="TimeoutException"/>,由上层写错误码+清忙,避免整线死等。</summary>
        private InspectionResult InvokeInspect(Recipe recipe)
        {
            if (_inspectTimeoutMs <= 0) return _inspect(recipe); // 同步路径(测试)

            var task = Task.Run(() => _inspect(recipe));
            bool done;
            try { done = task.Wait(_inspectTimeoutMs); }
            catch (AggregateException ae) { throw ae.InnerException ?? ae; } // 展开检测内部异常
            if (!done)
                throw new TimeoutException($"检测超过 {_inspectTimeoutMs}ms 未返回(相机/检测可能卡死)");
            return task.Result;
        }

        private void WriteErrorResult(string modelCode, short errorCode, string message)
        {
            _plc.WriteUInt16(_map.DefectBitmapWord, new ushort[_map.DefectBitmapWordCount]);
            _plc.WriteInt16(_map.ErrorCodeWord, errorCode);
            _plc.WriteBool(_map.OkBit, false);
            _plc.WriteBool(_map.NgBit, true);
            Log?.Invoke($"检测异常 型号={modelCode} 错误码={errorCode} {message}");
        }

        /// <summary>翻转心跳位（由运行层定时调用，供 PLC 判活）。</summary>
        public void ToggleHeartbeat()
        {
            _heartbeat = !_heartbeat;
            _plc.WriteBool(_map.HeartbeatBit, _heartbeat);
        }
    }
}
