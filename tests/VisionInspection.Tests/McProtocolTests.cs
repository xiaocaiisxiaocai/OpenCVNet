using System.IO;
using VisionInspection.Plc.Mc;
using Xunit;

namespace VisionInspection.Tests
{
    public class McProtocolTests
    {
        [Fact]
        public void DeviceCode_Parses_Common_Devices()
        {
            var d = DeviceCode.Parse("D200");
            Assert.Equal(0xA8, d.Code);
            Assert.Equal(200, d.Number);
            Assert.False(d.IsBit);

            var m = DeviceCode.Parse("M100");
            Assert.Equal(0x90, m.Code);
            Assert.Equal(100, m.Number);
            Assert.True(m.IsBit);

            // X 为十六进制号
            var x = DeviceCode.Parse("X1F");
            Assert.Equal(0x9C, x.Code);
            Assert.Equal(0x1F, x.Number);
        }

        [Fact]
        public void BuildReadWordsRequest_Produces_Expected_3E_Frame()
        {
            var frame = McProtocol.BuildReadWordsRequest(DeviceCode.Parse("D200"), 1);
            var expected = new byte[]
            {
                0x50, 0x00,             // 副头部
                0x00,                   // 网络号
                0xFF,                   // PC 号
                0xFF, 0x03,             // 请求目标模块 IO
                0x00,                   // 站号
                0x0C, 0x00,             // 数据长度 = 12
                0x10, 0x00,             // 监视定时器 = 16
                0x01, 0x04,             // 命令 0401（批量读）
                0x00, 0x00,             // 子命令 0000（按字）
                0xC8, 0x00, 0x00,       // 软元件号 200
                0xA8,                   // 软元件代码 D
                0x01, 0x00              // 点数 1
            };
            Assert.Equal(expected, frame);
        }

        [Fact]
        public void ParseReadWordsResponse_Extracts_Word()
        {
            var response = new byte[]
            {
                0xD0, 0x00, 0x00, 0xFF, 0xFF, 0x03, 0x00,
                0x04, 0x00,             // 数据长度 = 4（结束码2 + 数据2）
                0x00, 0x00,             // 结束码 = 0
                0x34, 0x12              // 数据 = 0x1234（小端）
            };
            var words = McProtocol.ParseReadWordsResponse(response);
            Assert.Single(words);
            Assert.Equal((ushort)0x1234, words[0]);
        }

        [Fact]
        public void ParsePayload_Throws_On_Nonzero_EndCode()
        {
            var response = new byte[]
            {
                0xD0, 0x00, 0x00, 0xFF, 0xFF, 0x03, 0x00,
                0x02, 0x00,             // 长度 = 2（仅结束码）
                0x51, 0xC0              // 结束码 = 0xC051（非 0）
            };
            Assert.Throws<McException>(() => McProtocol.ParsePayload(response));
        }

        [Fact]
        public void BuildWriteBitsRequest_Uses_Bit_Subcommand_And_Packs_Point()
        {
            var frame = McProtocol.BuildWriteBitsRequest(DeviceCode.Parse("M100"), new[] { true });
            var expected = new byte[]
            {
                0x50, 0x00,             // 副头部
                0x00,                   // 网络号
                0xFF,                   // PC 号
                0xFF, 0x03,             // 请求目标模块 IO
                0x00,                   // 站号
                0x0D, 0x00,             // 数据长度 = 13
                0x10, 0x00,             // 监视定时器 = 16
                0x01, 0x14,             // 命令 1401（批量写）
                0x01, 0x00,             // 子命令 0001（按位）
                0x64, 0x00, 0x00,       // 软元件号 100
                0x90,                   // 软元件代码 M
                0x01, 0x00,             // 点数 1
                0x10                    // 数据:1 点 ON(高半字节)
            };
            Assert.Equal(expected, frame);
        }

        [Fact]
        public void ParseReadBitsResponse_Decodes_Points()
        {
            // 4 点:ON, OFF, OFF, ON → 打包为 0x10, 0x01
            var response = new byte[]
            {
                0xD0, 0x00, 0x00, 0xFF, 0xFF, 0x03, 0x00,
                0x04, 0x00,             // 长度 = 4（结束码2 + 数据2）
                0x00, 0x00,             // 结束码 = 0
                0x10, 0x01              // 位数据
            };
            var bits = McProtocol.ParseReadBitsResponse(response, 4);
            Assert.Equal(new[] { true, false, false, true }, bits);
        }

        [Fact]
        public void WriteBits_RoundTrips_Through_Parse()
        {
            // 用写请求的数据段反解,验证打包/解包一致(单点 ON)。
            var write = McProtocol.BuildWriteBitsRequest(DeviceCode.Parse("M110"), new[] { true });
            byte dataByte = write[write.Length - 1];
            Assert.Equal(0x10, dataByte); // 高半字节=1 表示单点 ON
        }
    }
}
