using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using VisionInspection.Plc.Mc;
using Xunit;

namespace VisionInspection.Tests
{
    public class MelsecMcClientNetworkTests
    {
        [Fact]
        public void ReadInt16_Handles_Response_Split_Across_Tcp_Packets()
        {
            using (var server = new FakeMcServer(stream =>
            {
                ReadRequest(stream);
                var response = ReadWordResponse(0x1234);
                stream.Write(response, 0, 5);
                Thread.Sleep(30);
                stream.Write(response, 5, response.Length - 5);
            }))
            using (var client = new MelsecMcClient("127.0.0.1", server.Port, 1000))
            {
                client.Connect();

                Assert.Equal((short)0x1234, client.ReadInt16("D200"));
            }
        }

        [Fact]
        public void ReadInt16_Handles_Two_Responses_Coalesced_In_Stream()
        {
            using (var server = new FakeMcServer(stream =>
            {
                ReadRequest(stream);
                var first = ReadWordResponse(0x1111);
                var second = ReadWordResponse(0x2222);
                var combined = new byte[first.Length + second.Length];
                Array.Copy(first, 0, combined, 0, first.Length);
                Array.Copy(second, 0, combined, first.Length, second.Length);
                stream.Write(combined, 0, combined.Length);
                ReadRequest(stream);
            }))
            using (var client = new MelsecMcClient("127.0.0.1", server.Port, 1000))
            {
                client.Connect();

                Assert.Equal((short)0x1111, client.ReadInt16("D200"));
                Assert.Equal((short)0x2222, client.ReadInt16("D201"));
            }
        }

        [Fact]
        public void Protocol_Error_Does_Not_Drop_Connection()
        {
            using (var server = new FakeMcServer(stream =>
            {
                ReadRequest(stream);
                var error = ErrorResponse(0xC051);
                stream.Write(error, 0, error.Length);
                ReadRequest(stream);
                var ok = ReadWordResponse(0x3333);
                stream.Write(ok, 0, ok.Length);
            }))
            using (var client = new MelsecMcClient("127.0.0.1", server.Port, 1000))
            {
                client.Connect();

                Assert.Throws<McException>(() => client.ReadInt16("D200"));
                Assert.True(client.IsConnected);
                Assert.Equal((short)0x3333, client.ReadInt16("D200"));
            }
        }

        private static byte[] ReadWordResponse(ushort value)
        {
            return new byte[]
            {
                0xD0, 0x00, 0x00, 0xFF, 0xFF, 0x03, 0x00,
                0x04, 0x00,
                0x00, 0x00,
                (byte)(value & 0xFF), (byte)(value >> 8)
            };
        }

        private static byte[] ErrorResponse(ushort endCode)
        {
            return new byte[]
            {
                0xD0, 0x00, 0x00, 0xFF, 0xFF, 0x03, 0x00,
                0x02, 0x00,
                (byte)(endCode & 0xFF), (byte)(endCode >> 8)
            };
        }

        private static void ReadRequest(NetworkStream stream)
        {
            var buffer = new byte[256];
            stream.Read(buffer, 0, buffer.Length);
        }

        private sealed class FakeMcServer : IDisposable
        {
            private readonly TcpListener _listener;
            private readonly Thread _thread;
            private readonly Action<NetworkStream> _handle;

            public FakeMcServer(Action<NetworkStream> handle)
            {
                _handle = handle;
                _listener = new TcpListener(IPAddress.Loopback, 0);
                _listener.Start();
                Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
                _thread = new Thread(Run) { IsBackground = true };
                _thread.Start();
            }

            public int Port { get; }

            private void Run()
            {
                try
                {
                    using (var socket = _listener.AcceptTcpClient())
                    using (var stream = socket.GetStream())
                    {
                        _handle(stream);
                    }
                }
                catch
                {
                }
            }

            public void Dispose()
            {
                _listener.Stop();
                _thread.Join(1000);
            }
        }
    }
}
