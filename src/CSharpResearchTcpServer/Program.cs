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
        const int concurrentCount = 1000;
        const int loopCount = 1000;
        const int payloadLength = 1000;

        static void Main(string[] args)
        {
#if DEBUG
            Console.WriteLine("Build : DEBUG");
#endif
#if RELEASE
            Console.WriteLine("Build : RELEASE");
#endif
            var benchmark = new Benchmark(
                loopCount,
                concurrentCount,
                payloadLength,
                new ServerBase[]
                {
                    new AwaitServer(new IPEndPoint(IPAddress.Loopback, 17001)),
                    new AsyncSocketServer(new IPEndPoint(IPAddress.Loopback, 17002)),
                    new ThreadPoolServer(new IPEndPoint(IPAddress.Loopback, 17003)),
                }
            );

            if (args.Length >= 1)
            {
                var cmd = args[0];
                if (cmd == "server")
                {
                    benchmark.BootServer().Wait();
                }
                else if (cmd == "client")
                {
                    var ip = IPAddress.Parse(args[1]);
                    benchmark.BootClient(ip).Wait();
                }
                else
                {
                    Console.WriteLine("Command not found " + cmd);
                }
            }
            else
            {
                Task.WaitAll(
                        benchmark.BootServer(),
                        Task.Delay(100).ContinueWith(_ => benchmark.BootClient(IPAddress.Loopback))
                    );
            }
        }
    }

    class Benchmark
    {
        readonly int loopCount;
        readonly int concurrentCount;
        readonly int payloadLength;
        static readonly byte[] terminatePacket = new byte[] { 0, 0, 0, 0 };
        readonly byte[] headerPacket;
        readonly byte[] payloadPacket;
        ServerBase[] servers;

        public Benchmark(int loopCount,
            int concurrentCount,
            int payloadLength,
            ServerBase[] servers)
        {
            this.servers = servers;
            this.loopCount = loopCount;
            this.concurrentCount = concurrentCount;
            this.payloadLength = payloadLength;

            headerPacket = BitConverter.GetBytes(this.payloadLength);
            payloadPacket = Enumerable.Range(1, payloadLength).Select(x => (byte)x).ToArray();
        }

        public Task BootServer()
        {
            var serverTask = Task.WhenAll(servers.Select(x => Task.Factory.StartNew(x.Run)));
            var consoleTask = Task.Run(() =>
            {
                while (true)
                {
                    Console.ReadLine();
                    int t1Max;
                    int t2Max;
                    ThreadPool.GetMaxThreads(out t1Max, out t2Max);
                    int t1;
                    int t2;
                    ThreadPool.GetAvailableThreads(out t1, out t2);
                    Console.WriteLine("Thread: worker({0}/{1}) io({2}/{3})",
                        t1Max - t1,
                        t1Max,
                        t2Max - t2,
                        t2Max
                    );
                    foreach (var server in servers)
                    {
                        Console.WriteLine(server);
                    }
                }
            });
            return Task.WhenAll(serverTask, consoleTask);
        }

        public async Task BootClient(IPAddress ip)
        {
            var sw = new Stopwatch();
            Console.WriteLine("Connections : {0:#,0}", concurrentCount);
            Console.WriteLine("Payload     : {0:#,0}", payloadLength);
            Console.WriteLine("Count       : {0:#,0}", loopCount);

            ThreadPool.SetMinThreads(1024, 8);

            foreach (var server in servers)
            {
                await Task.Delay(1000).ConfigureAwait(false); // cooldown
                var end = server.Listen;
                sw.Reset();
                sw.Restart();
                var tasks = Enumerable.Range(0, concurrentCount).Select(_ => connectAndRequestResponse(end)).ToArray();
                await Task.WhenAll(tasks).ConfigureAwait(false);
                sw.Stop();

                var error = tasks.Sum(x => x.Result);
                var seconds = sw.Elapsed.TotalSeconds;
                Console.WriteLine("{0,-18}: Request Per Seconds({1:#,0}) elapsed({2:0.00}s) error({3})",
                    server.GetType().Name,
                    concurrentCount * loopCount / seconds,
                    seconds,
                    error);
            }
            Console.WriteLine("done. close this window if enter some key.");
            Console.ReadLine();
            System.Environment.Exit(0);
        }

        async Task<int> connectAndRequestResponse(IPEndPoint end)
        {
            int error = 0;
            using (var client = new TcpClient())
            {
                client.Client.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.NoDelay, true);
                try
                {
                    await client.ConnectAsync(end.Address, end.Port).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    //Console.WriteLine(ex.Message);
                    return 1;
                }

                using (var stream = client.GetStream())
                {
                    var buf = new byte[payloadLength];
                    for (var i = 0; i < loopCount; ++i)
                    {
                        await stream.WriteAsync(headerPacket, 0, headerPacket.Length).ConfigureAwait(false);
                        await stream.WriteAsync(payloadPacket, 0, payloadPacket.Length).ConfigureAwait(false);

                        var rest = payloadLength;
                        while (rest > 0)
                        {
                            rest -= await stream.ReadAsync(buf, 0, buf.Length).ConfigureAwait(false);
                        }
                        if (memcmp(payloadPacket, buf, payloadLength) != 0)
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
        public readonly IPEndPoint Listen;
        protected const int headerSize = 4;
        protected const int backlog = 1000;
        protected const int bufferSize = 1000;
        protected const char terminate = '\n';

        public ServerBase(IPEndPoint endpoint)
        {
            Listen = endpoint;
        }

        abstract public void Run();

        protected void setSocketOption(Socket sock)
        {
            sock.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.NoDelay, true);
        }

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