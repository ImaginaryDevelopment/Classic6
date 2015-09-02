using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net.Sockets;
using System.Net;

namespace Classic6
{
    public class RemoteClient
    {
        public TcpClient TcpClient { get; set; }
        public Queue<byte[]> PacketQueue { get; set; }
        public string Username { get; set; }
        public EndPoint EndPoint { get; set; }
        public bool IsOp { get; set; }
        public Vector3 Position { get; set; }
        public byte Yaw { get; set; }
        public byte Heading { get; set; }
        public sbyte ID { get; set; }
        public bool LoggedIn { get; set; }

        public RemoteClient()
        {
            PacketQueue = new Queue<byte[]>();
            LoggedIn = false;
        }
    }
}
