
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace CSharpResearchTcpServer
{
    class ThreadPoolServer : ServerBase
    {
        public override void Run(IPEndPoint end)
        {
            var listener = new TcpListener(end);
            listener.Start(backlog);
            var callback = new WaitCallback(handleTcpClient);
            while (true)
            {
                var client = listener.AcceptTcpClient();
                Interlocked.Increment(ref AcceptCount);
                ThreadPool.QueueUserWorkItem(callback, client);
            }
        }

        void handleTcpClient(object state)
        {
            var client = (TcpClient)state;
            try
            {
                using (var s = client.GetStream())
                {
                    handleNetworkStream(s);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
            finally
            {
                Interlocked.Increment(ref CloseCount);
                client.Close();
            }
        }

        void handleNetworkStream(NetworkStream stream)
        {
            var buffer = new byte[bufferSize];
            while(true)
            {
                if(!fill(stream, buffer, headerSize))
                {
                    Interlocked.Increment(ref CloseByInvalidStream);
                    return;
                }
                var length = BitConverter.ToInt32(buffer, 0);
                if(length == 0)
                {
                    Interlocked.Increment(ref CloseByPeerCount);
                    return;
                }
                if (!fill(stream, buffer, length))
                {
                    Interlocked.Increment(ref CloseByInvalidStream);
                    return;
                }
                stream.Write(buffer, 0, length);
                Interlocked.Increment(ref WriteCount);
            }
        }

        bool fill(NetworkStream stream, byte[] buffer, int rest)
        {
            int offset = 0;
            while (rest > 0)
            {
                var length = stream.Read(buffer, offset, rest);
                Interlocked.Increment(ref ReadCount);
                if (length == 0)
                {
                    return false;
                }
                rest -= length;
                offset += rest;
            }
            return true;
        }
    }
}