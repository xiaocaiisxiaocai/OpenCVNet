using VisionInspection.Plc.Simulation;
using Xunit;

namespace VisionInspection.Tests
{
    public class SimulatedPlcClientTests
    {
        [Fact]
        public void Bool_Roundtrips()
        {
            var plc = new SimulatedPlcClient();
            Assert.False(plc.ReadBool("M100"));
            plc.WriteBool("M100", true);
            Assert.True(plc.ReadBool("M100"));
        }

        [Fact]
        public void Int16_Roundtrips()
        {
            var plc = new SimulatedPlcClient();
            plc.WriteInt16("D190", 1234);
            Assert.Equal((short)1234, plc.ReadInt16("D190"));
        }

        [Fact]
        public void Word_Block_Roundtrips_Across_Consecutive_Addresses()
        {
            var plc = new SimulatedPlcClient();
            plc.WriteUInt16("D200", new ushort[] { 1, 2, 3 });

            Assert.Equal((short)2, plc.ReadInt16("D201"));
            var block = plc.ReadUInt16("D200", 3);
            Assert.Equal(new ushort[] { 1, 2, 3 }, block);
        }

        [Fact]
        public void Unset_Words_Read_As_Zero()
        {
            var plc = new SimulatedPlcClient();
            Assert.Equal(new ushort[] { 0, 0 }, plc.ReadUInt16("D500", 2));
        }
    }
}
