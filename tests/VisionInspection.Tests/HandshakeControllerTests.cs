using System;
using System.Collections.Generic;
using System.IO;
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
                    new Station { Index = 0 },
                    new Station { Index = 1 }
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
    }
}
