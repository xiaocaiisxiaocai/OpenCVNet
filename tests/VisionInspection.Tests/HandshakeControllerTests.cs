using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using VisionInspection.Core.Models;
using VisionInspection.Infrastructure.Storage;
using VisionInspection.Plc.Handshake;
using VisionInspection.Plc.Simulation;
using Xunit;

namespace VisionInspection.Tests
{
    public class HandshakeControllerTests : IDisposable
    {
        private readonly string _dir;
        private readonly JsonRecipeStore _store;

        public HandshakeControllerTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "vi_hs_" + Path.GetRandomFileName());
            Directory.CreateDirectory(_dir);
            _store = new JsonRecipeStore(_dir);
            // 型号码 "1"：2 工位
            _store.Save(new Recipe
            {
                ModelCode = "1",
                Stations =
                {
                    new Station { Index = 0, Roi = new RoiRect(0, 0, 10, 10) },
                    new Station { Index = 1, Roi = new RoiRect(10, 0, 10, 10) }
                }
            });
        }

        public void Dispose() => Directory.Delete(_dir, true);

        private static InspectionResult Ok(Recipe r) => new InspectionResult(
            r.ModelCode, InspectionOutcome.Ok,
            new List<StationResult>
            {
                new StationResult(0, PresenceState.Present, 0.9, 0.5),
                new StationResult(1, PresenceState.Present, 0.9, 0.5)
            }, DateTime.UtcNow, 5);

        private static InspectionResult NgStation1(Recipe r) => new InspectionResult(
            r.ModelCode, InspectionOutcome.Ng,
            new List<StationResult>
            {
                new StationResult(0, PresenceState.Present, 0.9, 0.5),
                new StationResult(1, PresenceState.Absent, 0.05, 0.5)
            }, DateTime.UtcNow, 5);

        [Fact]
        public void Rising_Edge_Runs_Ok_Cycle_And_Sets_Handshake_Bits()
        {
            var plc = new SimulatedPlcClient();
            var ctrl = new HandshakeController(plc, _store, Ok);
            var map = ctrl.Map;

            plc.WriteInt16(map.ModelCodeWord, 1);   // 型号码 1
            plc.WriteBool(map.TriggerBit, true);    // 触发上升沿

            bool ran = ctrl.ProcessOnce();

            Assert.True(ran);
            Assert.True(plc.ReadBool(map.DoneBit));
            Assert.False(plc.ReadBool(map.BusyBit));
            Assert.True(plc.ReadBool(map.OkBit));
            Assert.False(plc.ReadBool(map.NgBit));
            Assert.Equal((short)PlcErrorCode.None, plc.ReadInt16(map.ErrorCodeWord));
            Assert.Equal(new ushort[] { 0 }, plc.ReadUInt16(map.DefectBitmapWord, 1)); // 无缺件
        }

        [Fact]
        public void Ng_Writes_Defect_Bitmap_And_Ng_Bit()
        {
            var plc = new SimulatedPlcClient();
            var ctrl = new HandshakeController(plc, _store, NgStation1);
            var map = ctrl.Map;

            plc.WriteInt16(map.ModelCodeWord, 1);
            plc.WriteBool(map.TriggerBit, true);
            ctrl.ProcessOnce();

            Assert.True(plc.ReadBool(map.NgBit));
            Assert.False(plc.ReadBool(map.OkBit));
            // 工位 1 缺件 → 位图第 0 字 bit1 = 2
            Assert.Equal((ushort)0b10, plc.ReadUInt16(map.DefectBitmapWord, 1)[0]);
        }

        [Fact]
        public void No_Matching_Recipe_Writes_Error_And_Ng()
        {
            var plc = new SimulatedPlcClient();
            var ctrl = new HandshakeController(plc, _store, Ok);
            var map = ctrl.Map;

            plc.WriteInt16(map.ModelCodeWord, 999); // 无此配方
            plc.WriteBool(map.TriggerBit, true);
            ctrl.ProcessOnce();

            Assert.True(plc.ReadBool(map.NgBit));
            Assert.Equal((short)PlcErrorCode.NoRecipe, plc.ReadInt16(map.ErrorCodeWord));
        }

        [Fact]
        public void Falling_Edge_Clears_Done_Bit()
        {
            var plc = new SimulatedPlcClient();
            var ctrl = new HandshakeController(plc, _store, Ok);
            var map = ctrl.Map;

            plc.WriteInt16(map.ModelCodeWord, 1);
            plc.WriteBool(map.TriggerBit, true);
            ctrl.ProcessOnce();               // 上升沿：Done = true
            Assert.True(plc.ReadBool(map.DoneBit));

            plc.WriteBool(map.TriggerBit, false);
            ctrl.ProcessOnce();               // 下降沿：Done = false
            Assert.False(plc.ReadBool(map.DoneBit));
        }

        [Fact]
        public void No_Rising_Edge_Does_Not_Run()
        {
            var plc = new SimulatedPlcClient();
            var ctrl = new HandshakeController(plc, _store, Ok);
            // 触发位保持 false → 不检测
            Assert.False(ctrl.ProcessOnce());
        }

        [Fact]
        public void Heartbeat_Toggles()
        {
            var plc = new SimulatedPlcClient();
            var ctrl = new HandshakeController(plc, _store, Ok);
            var map = ctrl.Map;

            ctrl.ToggleHeartbeat();
            Assert.True(plc.ReadBool(map.HeartbeatBit));
            ctrl.ToggleHeartbeat();
            Assert.False(plc.ReadBool(map.HeartbeatBit));
        }

        [Fact]
        public void Inspect_Timeout_Writes_Error_And_Clears_Busy()
        {
            var plc = new SimulatedPlcClient();
            // 检测委托故意卡死;控制器超时 200ms 应写错误码+NG 并清忙,而非永久阻塞。
            Func<Recipe, InspectionResult> hang = r => { System.Threading.Thread.Sleep(5000); return Ok(r); };
            var ctrl = new HandshakeController(plc, _store, hang, null, inspectTimeoutMs: 200);
            var map = ctrl.Map;

            plc.WriteInt16(map.ModelCodeWord, 1);
            plc.WriteBool(map.TriggerBit, true);

            bool ran = ctrl.ProcessOnce(); // 应在 ~200ms 返回,不卡死

            Assert.True(ran);
            Assert.False(plc.ReadBool(map.BusyBit));   // 忙位已清
            Assert.True(plc.ReadBool(map.NgBit));       // 判 NG
            Assert.Equal((short)PlcErrorCode.Internal, plc.ReadInt16(map.ErrorCodeWord));
        }

        [Fact]
        public void Recipe_Load_Exception_Writes_Error_Result_And_Done()
        {
            File.WriteAllText(Path.Combine(_dir, "1.json"), "{ broken");
            var plc = new SimulatedPlcClient();
            var ctrl = new HandshakeController(plc, _store, Ok);
            var map = ctrl.Map;
            plc.WriteBool(map.OkBit, true); // 上周期残留 OK

            plc.WriteInt16(map.ModelCodeWord, 1);
            plc.WriteBool(map.TriggerBit, true);

            bool ran = ctrl.ProcessOnce();

            Assert.True(ran);
            Assert.True(plc.ReadBool(map.DoneBit));
            Assert.False(plc.ReadBool(map.OkBit));
            Assert.True(plc.ReadBool(map.NgBit));
            Assert.Equal((short)PlcErrorCode.NoRecipe, plc.ReadInt16(map.ErrorCodeWord));
        }

        [Fact]
        public void Defect_Bitmap_Overflow_Writes_Error_Result_And_Done()
        {
            var overflowPath = Path.Combine(_dir, "2.json");
            File.WriteAllText(overflowPath,
                "{\"ModelCode\":\"2\",\"Stations\":[{\"Index\":128,\"Roi\":{\"X\":0,\"Y\":0,\"Width\":10,\"Height\":10},\"Threshold\":0.5,\"Enabled\":true}]}");
            var plc = new SimulatedPlcClient();
            var ctrl = new HandshakeController(plc, _store, r => new InspectionResult(
                r.ModelCode,
                InspectionOutcome.Ng,
                new List<StationResult> { new StationResult(128, PresenceState.Absent, 0, 0.5) },
                DateTime.UtcNow,
                1));
            var map = ctrl.Map;
            plc.WriteBool(map.OkBit, true);

            plc.WriteInt16(map.ModelCodeWord, 2);
            plc.WriteBool(map.TriggerBit, true);

            bool ran = ctrl.ProcessOnce();

            Assert.True(ran);
            Assert.True(plc.ReadBool(map.DoneBit));
            Assert.False(plc.ReadBool(map.OkBit));
            Assert.True(plc.ReadBool(map.NgBit));
            Assert.Equal((short)PlcErrorCode.Internal, plc.ReadInt16(map.ErrorCodeWord));
        }

        [Fact]
        public void Timeout_Leaves_Cycle_In_Flight_And_Rejects_Next_Trigger()
        {
            var started = new ManualResetEventSlim(false);
            var release = new ManualResetEventSlim(false);
            Func<Recipe, InspectionResult> hang = r =>
            {
                started.Set();
                release.Wait(3000);
                return Ok(r);
            };
            var plc = new SimulatedPlcClient();
            var ctrl = new HandshakeController(plc, _store, hang, null, inspectTimeoutMs: 100);
            var map = ctrl.Map;
            plc.WriteInt16(map.ModelCodeWord, 1);

            plc.WriteBool(map.TriggerBit, true);
            ctrl.ProcessOnce();
            Assert.True(started.IsSet);

            plc.WriteBool(map.TriggerBit, false);
            ctrl.ProcessOnce();
            plc.WriteBool(map.TriggerBit, true);
            ctrl.ProcessOnce();

            Assert.True(plc.ReadBool(map.NgBit));
            Assert.Equal((short)PlcErrorCode.Internal, plc.ReadInt16(map.ErrorCodeWord));
            release.Set();
        }

        [Fact]
        public void Timeout_Background_Task_Exit_Is_Logged()
        {
            var started = new ManualResetEventSlim(false);
            var release = new ManualResetEventSlim(false);
            Func<Recipe, InspectionResult> hang = r =>
            {
                started.Set();
                release.Wait(3000);
                return Ok(r);
            };
            var plc = new SimulatedPlcClient();
            var ctrl = new HandshakeController(plc, _store, hang, null, inspectTimeoutMs: 100);
            var map = ctrl.Map;
            var logged = new ManualResetEventSlim(false);
            ctrl.Log += m =>
            {
                if (m.Contains("超时后台任务已退出")) logged.Set();
            };

            plc.WriteInt16(map.ModelCodeWord, 1);
            plc.WriteBool(map.TriggerBit, true);
            ctrl.ProcessOnce();
            Assert.True(started.IsSet);
            release.Set();

            Assert.True(logged.Wait(1000));
        }

        [Fact]
        public void Sequence_Mode_Echoes_Request_Sequence_When_Result_Is_Written()
        {
            var plc = new SimulatedPlcClient();
            var map = new HandshakeAddressMap { UseSequence = true };
            var ctrl = new HandshakeController(plc, _store, Ok, map);

            plc.WriteInt16(map.ModelCodeWord, 1);
            plc.WriteInt16(map.RequestSequenceWord, 42);
            plc.WriteBool(map.TriggerBit, true);

            Assert.True(ctrl.ProcessOnce());
            Assert.True(plc.ReadBool(map.DoneBit));
            Assert.Equal((short)42, plc.ReadInt16(map.AckSequenceWord));
        }

        [Fact]
        public void Sequence_Mode_Does_Not_Reprocess_Same_Sequence()
        {
            int count = 0;
            var plc = new SimulatedPlcClient();
            var map = new HandshakeAddressMap { UseSequence = true };
            var ctrl = new HandshakeController(plc, _store, r => { count++; return Ok(r); }, map);

            plc.WriteInt16(map.ModelCodeWord, 1);
            plc.WriteInt16(map.RequestSequenceWord, 42);
            plc.WriteBool(map.TriggerBit, true);
            Assert.True(ctrl.ProcessOnce());

            plc.WriteBool(map.TriggerBit, false);
            ctrl.ProcessOnce();
            plc.WriteBool(map.TriggerBit, true);
            Assert.False(ctrl.ProcessOnce());

            Assert.Equal(1, count);
        }
    }
}
