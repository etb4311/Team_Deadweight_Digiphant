using System;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace TeamDeadweight
{
    // Non-blocking local UDP listener that returns only the newest datagram (or null).
    public class LatestUdp : IDisposable
    {
        readonly UdpClient socket;

        public LatestUdp(int port)
        {
            try
            {
                socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, port));
                socket.Client.Blocking = false;
            }
            catch (Exception e) when (e is SocketException || e is ArgumentOutOfRangeException) { socket = null; }
        }

        public string Poll()
        {
            if (socket == null) return null;
            try
            {
                byte[] latest = null;
                var sender = new IPEndPoint(IPAddress.Loopback, 0);
                for (int i = 0; i < 64 && socket.Available > 0; i++) latest = socket.Receive(ref sender);
                return latest == null ? null : Encoding.UTF8.GetString(latest);
            }
            catch (SocketException) { return null; }
        }

        public void Dispose() => socket?.Close();
    }
}
