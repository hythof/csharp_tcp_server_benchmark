using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace CSharpResearchTcpServer
{
    class StateObject
    {
        public Socket WorkSocket;
        public byte[] Buffer;
        public int Rest;
        public int BodyLength;
        public bool IsHeader;

        public StateObject(Socket sock, int length)
        {
            WorkSocket = sock;
            Buffer = new byte[length];
        }
    }

    class AsyncSocketServer : ServerBase
    {
        ManualResetEvent allDone = new ManualResetEvent(false);

        public AsyncSocketServer(IPEndPoint endpoint) : base(endpoint)
        {
        }

        public override void Run()
        {
            var listener = new Socket(
                AddressFamily.InterNetwork,
                SocketType.Stream,
                ProtocolType.Tcp);
            listener.Bind(Listen);
            listener.Listen(backlog);

            while (true)
            {
                allDone.Reset();
                listener.BeginAccept(
                    new AsyncCallback(acceptCallback),
                    listener);
                allDone.WaitOne();
            }
        }

        void acceptCallback(IAsyncResult ar)
        {
            allDone.Set();
            Interlocked.Increment(ref AcceptCount);
            var listener = (Socket)ar.AsyncState;
            var handler = listener.EndAccept(ar);

            setSocketOption(listener);

            var state = new StateObject(handler, bufferSize);
            state.IsHeader = true;
            state.Rest = headerSize;
            handler.BeginReceive(
                state.Buffer,
                0,
                state.Rest,
                0,
                new AsyncCallback(readCallback),
                state);
        }

        void readCallback(IAsyncResult ar)
        {
            Interlocked.Increment(ref ReadCount);
            var state = (StateObject)ar.AsyncState;
            var handler = state.WorkSocket;
            int length = handler.EndReceive(ar);

            if (length == 0)
            {
                Interlocked.Increment(ref CloseByInvalidStream);
                Interlocked.Increment(ref CloseCount);
                handler.Shutdown(SocketShutdown.Both);
                handler.Close();
                return;
            }

            state.Rest -= length;
            if (state.IsHeader)
            {
                if(state.Rest > 0)
                {
                    handler.BeginReceive(
                        state.Buffer,
                        state.Rest,
                        headerSize - state.Rest,
                        0,
                        new AsyncCallback(readCallback),
                        state);
                }
                else if (state.Rest == 0)
                {
                    state.BodyLength = BitConverter.ToInt32(state.Buffer, 0);
                    if(state.BodyLength == 0)
                    {
                        Interlocked.Increment(ref CloseByPeerCount);
                        Interlocked.Increment(ref CloseCount);
                        handler.Shutdown(SocketShutdown.Both);
                        handler.Close();
                        return;
                    }
                    if(state.BodyLength > state.Buffer.Length)
                    {
                        Interlocked.Increment(ref CloseByInvalidStream);
                        Interlocked.Increment(ref CloseCount);
                        Console.WriteLine("too big length");
                        return;
                    }
                    state.Rest = state.BodyLength;
                    state.IsHeader = false;
                    handler.BeginReceive(
                        state.Buffer,
                        0,
                        state.Rest,
                        0,
                        new AsyncCallback(readCallback),
                        state);
                }
                else
                {
                    Console.WriteLine(string.Format("BUG. buffer over flow. recv={0} rest={1}", length, state.Rest));
                }
            }
            else
            {
                if(state.Rest > 0)
                {
                    handler.BeginReceive(
                        state.Buffer,
                        state.BodyLength - state.Rest,
                        state.Rest,
                        0,
                        new AsyncCallback(readCallback),
                        state);
                }
                else if (state.Rest == 0)
                {
                    var rest = state.BodyLength;
                    while (rest > 0)
                    {
                        rest -= handler.Send(state.Buffer, state.BodyLength - rest, rest, 0);
                        Interlocked.Increment(ref WriteCount);
                    }
                    state.IsHeader = true;
                    state.Rest = headerSize;
                    handler.BeginReceive(
                        state.Buffer,
                        0,
                        state.Rest,
                        0,
                        new AsyncCallback(readCallback),
                        state);
                }
                else
                {
                    Console.WriteLine("BUG. buffer over flow");
                }
            }
        }
    }
}
