using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace CSharpResearchTcpServer
{
    class Program
    {
        static readonly int[] ports = new int[] { 17001, 17002, 17003 };
        const int backlog = 1024;
        const int concurrentCount = 500;
        const int loopCount = 100;
        const int payloadLength = 1000;
        static readonly byte[] headerPacket = BitConverter.GetBytes(payloadLength);
        static readonly byte[] terminatePacket = new byte[] { 0, 0, 0, 0 };
        static readonly byte[] payloadPacket = new byte[payloadLength];

        static void Main(string[] args)
        {
#if DEBUG
            Console.WriteLine("debug...");
#endif
#if RELEASE
            Console.WriteLine("release...");
#endif
            for(var i=0; i< payloadPacket.Length; ++i)
            {
                payloadPacket[i] = (byte)i;
            }

            if(args.Length >= 1)
            {
                var cmd = args[0];
                if(cmd == "server")
                {
                    bootServer().Wait();
                }
                else if(cmd == "client")
                {
                    var ip = IPAddress.Parse(args[1]);
                    bootClient(ip).Wait();
                }
            }
            Task.WaitAll(
                    bootServer(),
                    Task.Delay(100).ContinueWith(_ => bootClient(IPAddress.Loopback))
                );
        }

        static Task bootServer()
        {
            var s1 = new AwaitServer();
            var s2 = new AsyncSocketServer();
            var s3 = new ThreadPoolServer();
            return Task.WhenAll(
                runServer(s1, ports[0]),
                runServer(s2, ports[1]),
                runServer(s3, ports[2]),
                Task.Run(() => {
                    while(true)
                    {
                        Console.ReadLine();
                        Console.WriteLine(s1);
                        Console.WriteLine(s2);
                        Console.WriteLine(s3);
                    }
                })
            );
        }

        static Task runServer(ServerBase server, int port)
        {
            return Task.Run(() => server.Run(new IPEndPoint(IPAddress.Any, port)));
        }

        static async Task bootClient(IPAddress ip)
        {
            var sw = new Stopwatch();
            foreach(var port in ports)
            {
                var end = new IPEndPoint(ip, port);
                sw.Restart();
                var tasks = Enumerable.Range(0, concurrentCount).Select(_ => connectAndRequestResponse(end)).ToArray();
                await Task.WhenAll(tasks).ConfigureAwait(false);
                sw.Stop();

                var error = tasks.Sum(x => x.Result);
                Console.WriteLine("{0}: request per seconds({1}) error({2})",
                    port,
                    concurrentCount * loopCount / sw.Elapsed.TotalSeconds,
                    error);
            }
            Console.WriteLine("done. close this window if enter some key.");
            Console.ReadLine();
            System.Environment.Exit(0);
        }

        static async Task<int> connectAndRequestResponse(IPEndPoint end)
        {
            int error = 0;
            using (var client = new TcpClient())
            {
                await client.ConnectAsync(end.Address, end.Port).ConfigureAwait(false);
                using (var stream = client.GetStream())
                {
                    var buf = new byte[payloadLength];
                    for (var i = 0; i < loopCount; ++i)
                    {
                        var rest = payloadLength;
                        await stream.WriteAsync(headerPacket, 0, headerPacket.Length).ConfigureAwait(false);
                        await stream.WriteAsync(payloadPacket, 0, payloadPacket.Length).ConfigureAwait(false);
                        while (rest > 0)
                        {
                            rest -= await stream.ReadAsync(buf, 0, buf.Length).ConfigureAwait(false);
                        }
                        if(memcmp(payloadPacket, buf, payloadLength) != 0)
                        {
                            ++error;
                        }
                    }
                    await stream.WriteAsync(terminatePacket, 0, terminatePacket.Length).ConfigureAwait(false);
                }
            }
            return error;
        }

        [DllImport("msvcrt.dll", CallingConvention = CallingConvention.Cdecl)]
        static extern int memcmp(byte[] b1, byte[] b2, long count);
    }

    abstract class ServerBase
    {
        public int AcceptCount;
        public int ReadCount;
        public int WriteCount;
        public int CloseCount;
        public int CloseByPeerCount;
        public int CloseByInvalidStream;
        protected const int headerSize = 4;
        protected const int backlog = 1024;
        protected const int bufferSize = 1024;
        protected const char terminate = '\n';

        abstract public void Run(IPEndPoint end);

        public override string ToString()
        {
            return string.Format("accept({0}) close({1}) peer({2}) + invalid({3}) read({4}) write({5}) : {6}",
                AcceptCount,
                CloseCount,
                CloseByPeerCount,
                CloseByInvalidStream,
                ReadCount,
                WriteCount,
                GetType().Name
            );
        }
    }
}