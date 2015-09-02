using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net.Sockets;
using System.Threading;
using System.Web;
using System.Net;
using System.IO;
using System.IO.Compression;

namespace Classic6
{
    public class ClassicServer
    {
        #region Properties

        public int MaxPlayers { get; set; }
        public string ServerName { get; set; }
        public bool IsPrivate { get; set; }
        public string ServerUrl { get; private set; }
        public string ServerSalt { get; set; }
        public string MessageOfTheDay { get; set; }
        public Level Level { get; set; }

        #endregion

        #region Variables

        private TcpListener listener;
        private Timer netWorker;
        private Timer heartBeat;
        public List<RemoteClient> clients { get; private set; }
        private int port;
        private Random rand;
        private StreamWriter logWriter;
        private sbyte NextID = 0;
        private byte TicksToPing = 50;

        #region Constants

        private const byte MinecraftVersion = 7;
        private const string SaltCharacters = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";

        #endregion

        #endregion

        #region Initialization

        public ClassicServer()
        {
            clients = new List<RemoteClient>();
            rand = new Random();
            Level = new Level(64, 64, 64);
            logWriter = new StreamWriter("server.log");
            ServerUrl = "";
        }

        #endregion

        #region Public Methods

        public void Start(int port)
        {
            this.port = port;
            ServerSalt = "";

            

            IPEndPoint localEP = new IPEndPoint(IPAddress.Parse("0.0.0.0"), port); // Listen on all interfaces
            listener = new TcpListener(localEP);
            listener.Start();

            heartBeat = new Timer(new TimerCallback(DoHeartbeat), null, 0, 45000); // Send heartbeat every 45 seconds
            netWorker = new Timer(new TimerCallback(DoWork), null, 0, 50);
        }

        public void SendChat(string chat, sbyte from)
        {
            EnqueueToAllClients(new byte[] { (byte)PacketID.ChatMessage, (byte)from }.Concat(MakeString(chat)).ToArray());
        }

        #endregion

        #region Workers

        private const string RequestFormat = "http://minecraft.net/heartbeat.jsp?port={0}&max={1}&name={2}&public={3}&version={4}&salt={5}&users={6}";

        private void DoHeartbeat(object o)
        {
            // This is not asyncronous because the whole thing runs on a different thread

            // Generate a salt
            lock (ServerSalt)
            {
                ServerSalt = "";
                for (int i = 0; i < 16; i++)
                    ServerSalt += SaltCharacters[rand.Next(SaltCharacters.Length)];

                string reqString = string.Format(RequestFormat, port, MaxPlayers,
                    Uri.EscapeDataString(ServerName), IsPrivate ? "False" : "True",
                    MinecraftVersion, "", clients.Count);

                try
                {
                    HttpWebRequest req = (HttpWebRequest)WebRequest.Create(reqString);
                    WebResponse resp = req.GetResponse();
                    StreamReader reader = new StreamReader(resp.GetResponseStream());
                    string response = reader.ReadToEnd();
                    Console.WriteLine(response);
                    ServerUrl = response;
                }
                catch
                {
                }
            }
        }

        private void DoWork(object o)
        {
            TicksToPing--;
            lock (listener)
            {
                if (listener.Pending())
                {
                    RemoteClient c = new RemoteClient()
                    {
                        TcpClient = listener.AcceptTcpClient(),
                    };
                    Log("New connection from " + c.TcpClient.Client.RemoteEndPoint.ToString());
                    c.EndPoint = c.TcpClient.Client.RemoteEndPoint;
                    clients.Add(c);
                }
                List<RemoteClient> newClientList = new List<RemoteClient>();
                foreach (RemoteClient c in clients)
                {
                    if (!c.TcpClient.Connected)
                    {
                        Log("Lost connection from " + c.EndPoint);
                        if (newClientList.Contains(c))
                            newClientList.Remove(c);
                        EnqueueToAllClients(CreateDespawnPlayer(c.ID));
                        c.LoggedIn = false;
                        continue;
                    }
                    newClientList.Add(c);
                    try
                    {
                        while (c.TcpClient.Connected && c.TcpClient.Available > 0) // Read available data
                        {
                            PacketID packet = (PacketID)c.TcpClient.GetStream().ReadByte();
                            switch (packet)
                            {
                                case PacketID.Identification:
                                    byte protocol = (byte)c.TcpClient.GetStream().ReadByte();
                                    string username = ReadString(c.TcpClient.GetStream());
                                    string key = ReadString(c.TcpClient.GetStream());
                                    c.TcpClient.GetStream().ReadByte(); // Unused byte
                                    c.Username = username;

                                    c.PacketQueue.Enqueue(CreateInformationPacket(this, c));
                                    c.PacketQueue.Enqueue(CreateLevelInitializePacket());
                                    MemoryStream ms = new MemoryStream();
                                    GZipStream s = new GZipStream(ms, CompressionMode.Compress, true);
                                    s.Write(BitConverter.GetBytes(IPAddress.HostToNetworkOrder(Level.Data.Length)), 0, sizeof(int));
                                    s.Write(Level.Data, 0, Level.Data.Length);
                                    s.Close();
                                    byte[] data = ms.GetBuffer();
                                    ms.Close();

                                    Console.WriteLine("Map length: " + Level.Data.Length);

                                    double numChunks = data.Length / 1042;
                                    double chunksSent = 0;
                                    for (int i = 0; i < data.Length; i += 1024)
                                    {
                                        byte[] chunkData = new byte[1024];

                                        short chunkLength = 1024;
                                        if (data.Length - i < chunkLength)
                                            chunkLength = (short)(data.Length - i);

                                        Array.Copy(data, i, chunkData, 0, chunkLength);

                                        byte[] b = new byte[] { (byte)PacketID.LevelDataChunk };
                                        b = b.Concat(MakeShort(chunkLength)).ToArray();
                                        Console.WriteLine("Chunk length: " + chunkLength);
                                        b = b.Concat(chunkData).ToArray();
                                        b = b.Concat(new byte[] { (byte)((chunksSent / numChunks) * 100) }).ToArray();

                                        c.PacketQueue.Enqueue(b);
                                        chunksSent++;
                                    }
                                    byte[] finalize = new byte[] { (byte)PacketID.LevelFinalize };
                                    finalize = finalize.Concat(MakeShort(Level.Width)).ToArray();
                                    finalize = finalize.Concat(MakeShort(Level.Width)).ToArray();
                                    finalize = finalize.Concat(MakeShort(Level.Width)).ToArray();
                                    c.PacketQueue.Enqueue(finalize);
                                    c.ID = NextID;
                                    NextID++;
                                    if (NextID < 0)
                                        NextID = 0;
                                    c.Position = Level.Spawn.Clone();
                                    c.PacketQueue.Enqueue(CreatePositionAndOrientation(-1, c.Position.X,
                                            c.Position.Y, c.Position.Z, c.Yaw, c.Heading));
                                    foreach (RemoteClient client in from cl in clients
                                                                    where cl.ID != c.ID
                                                                    select cl)
                                    {
                                        client.PacketQueue.Enqueue(CreateSpawnPlayer(c.ID, c.Username, c.Position.X,
                                            c.Position.Y, c.Position.Z, c.Yaw, c.Heading));
                                        client.PacketQueue.Enqueue(CreatePositionAndOrientation(c.ID, c.Position.X,
                                            c.Position.Y, c.Position.Z, c.Yaw, c.Heading));
                                        c.PacketQueue.Enqueue(CreateSpawnPlayer(client.ID, client.Username, client.Position.X,
                                            client.Position.Y, client.Position.Z, client.Yaw, client.Heading));
                                    }
                                    //SendChat(c.Username + " joined the game", -1);
                                    break;
                                case PacketID.PositionAndOrientation:
                                    c.TcpClient.GetStream().ReadByte(); // Discard
                                    c.Position.X = ReadFloat(c.TcpClient.GetStream());
                                    c.Position.Y = ReadFloat(c.TcpClient.GetStream());
                                    c.Position.Z = ReadFloat(c.TcpClient.GetStream());
                                    c.Yaw = (byte)c.TcpClient.GetStream().ReadByte();
                                    c.Heading = (byte)c.TcpClient.GetStream().ReadByte();

                                    foreach (RemoteClient client in from cl in clients
                                                                    where cl.ID != c.ID
                                                                    select cl)
                                    {
                                        client.PacketQueue.Enqueue(CreatePositionAndOrientation(c.ID, c.Position.X,
                                            c.Position.Y, c.Position.Z, c.Yaw, c.Heading));
                                    }
                                    break;
                                case PacketID.ClientSetBlock:
                                    short x = ReadShort(c.TcpClient.GetStream());
                                    short y = ReadShort(c.TcpClient.GetStream());
                                    short z = ReadShort(c.TcpClient.GetStream());
                                    byte mode = (byte)c.TcpClient.GetStream().ReadByte();
                                    byte type = (byte)c.TcpClient.GetStream().ReadByte();
                                    if (mode == 0)
                                        type = 0;
                                    Level.SetBlock(new Vector3(x, y, z), type);
                                    foreach (RemoteClient client in from cl in clients
                                                                    where cl.ID != c.ID
                                                                    select cl)
                                    {
                                        client.PacketQueue.Enqueue(CreateServerPlaceBlock(x, y, z, type));
                                    }
                                    break;
                                case PacketID.ChatMessage:
                                    c.TcpClient.GetStream().ReadByte();
                                    string msg = ReadString(c.TcpClient.GetStream());
                                    msg = "<" + c.Username + "> " + msg;
                                    if (msg.Length > 64)
                                        msg = msg.Remove(63);
                                    SendChat(msg, c.ID);
                                    break;
                                default:
                                    // bad packet
                                    break;
                            }
                        }
                    }
                    catch
                    {
                        Log("Lost connection from " + c.EndPoint);
                        if (newClientList.Contains(c))
                            newClientList.Remove(c);
                        EnqueueToAllClients(CreateDespawnPlayer(c.ID));
                        c.LoggedIn = false;
                        continue;
                    }
                    if (!c.TcpClient.Connected)
                    {
                        Log("Lost connection from " + c.EndPoint);
                        if (newClientList.Contains(c))
                            newClientList.Remove(c);
                        EnqueueToAllClients(CreateDespawnPlayer(c.ID));
                        c.LoggedIn = false;
                        continue;
                    }
                }
                clients = new List<RemoteClient>(newClientList);
                newClientList.Clear();
                foreach (RemoteClient c in clients) // Write pending data
                {
                    try
                    {
                        while (c.TcpClient.Connected && c.PacketQueue.Count != 0)
                        {
                            byte[] packet = c.PacketQueue.Dequeue();
                            c.TcpClient.GetStream().Write(packet, 0, packet.Length);
                            Log("Server to client [" + c.EndPoint.ToString() + "]: " + packet[0] + " Length: " + packet.Length);
                        }
                        if (c.TcpClient.Connected)
                            newClientList.Add(c);
                        else
                        {
                            Log("Lost connection from " + c.EndPoint);
                            if (newClientList.Contains(c))
                                newClientList.Remove(c);
                            EnqueueToAllClients(CreateDespawnPlayer(c.ID));
                            c.LoggedIn = false;
                            break;
                        }
                    }
                    catch
                    {
                        Log("Lost connection from " + c.EndPoint);
                        if (newClientList.Contains(c))
                            newClientList.Remove(c);
                        EnqueueToAllClients(CreateDespawnPlayer(c.ID));
                        c.LoggedIn = false;
                        continue;
                    }
                }
                clients = new List<RemoteClient>(newClientList);
            }
        }

        #endregion

        #region Packets

        internal static byte[] CreateDespawnPlayer(sbyte pID)
        {
            return new byte[] { (byte)PacketID.DespawnPlayer, (byte)pID };
        }

        internal static byte[] CreateServerPlaceBlock(short x, short y, short z, byte value)
        {
            byte[] b = new byte[] { (byte)PacketID.ServerSetBlock, };
            b = b.Concat(MakeShort(x)).ToArray();
            b = b.Concat(MakeShort(y)).ToArray();
            b = b.Concat(MakeShort(z)).ToArray();
            b = b.Concat(new byte[] { value, }).ToArray();
            return b;
        }

        internal static byte[] CreateSpawnPlayer(sbyte pID, string name, float x, float y, float z, byte yaw, byte heading)
        {
            byte[] b = new byte[] { (byte)PacketID.SpawnPlayer, (byte)pID };
            b = b.Concat(MakeString(name)).ToArray();
            b = b.Concat(MakeFloat(x)).ToArray();
            b = b.Concat(MakeFloat(y)).ToArray();
            b = b.Concat(MakeFloat(z)).ToArray();
            b = b.Concat(new byte[] { yaw, heading }).ToArray();
            return b;
        }

        internal static byte[] CreatePositionAndOrientation(sbyte pID, float x, float y, float z, byte yaw, byte heading)
        {
            byte[] b = new byte[] { (byte)PacketID.PositionAndOrientation, (byte)pID };
            b = b.Concat(MakeFloat(x)).ToArray();
            b = b.Concat(MakeFloat(y)).ToArray();
            b = b.Concat(MakeFloat(z)).ToArray();
            b = b.Concat(new byte[] { yaw, heading }).ToArray();
            return b;
        }

        internal static byte[] CreateInformationPacket(ClassicServer server, RemoteClient c)
        {
            byte[] b = new byte[] { (byte)PacketID.Identification, MinecraftVersion, };
            b = b.Concat(MakeString(server.ServerName)).ToArray();
            b = b.Concat(MakeString(server.MessageOfTheDay)).ToArray();
            b = b.Concat(new byte[] { (byte)(c.IsOp ? 0x64 : 0) }).ToArray();
            return b;
        }

        internal static byte[] CreateLevelInitializePacket()
        {
            return new byte[] { (byte)PacketID.LevelInitialize, };
        }

        internal static byte[] CreateLevelDataChunkPacket(byte[] data, byte percentageComplete, short chunkLength)
        {
            byte[] b = new byte[] { (byte)PacketID.LevelDataChunk };
            b = b.Concat(MakeShort(chunkLength)).ToArray();
            b = b.Concat(data).ToArray();
            b = b.Concat(new byte[] { percentageComplete }).ToArray();
            return b;
        }

        #endregion

        #region Helpers

        private void EnqueueToAllClients(byte[] Packet)
        {
            foreach (RemoteClient c in clients)
                c.PacketQueue.Enqueue(Packet);
        }

        private void Log(string s)
        {
            try
            {
                logWriter.WriteLine(s);
                if (s.Length < 150)
                    Console.WriteLine(s);
                logWriter.Flush();
            }
            catch { }
        }

        internal static byte[] MakeString(string s)
        {
            return Encoding.ASCII.GetBytes(s.PadRight(64, '\x00'));
        }

        internal static byte[] MakeFloat(float f)
        {
            short s = (short)(f * 32);
            return MakeShort(s);
        }

        internal static byte[] MakeShort(short s)
        {
            return BitConverter.GetBytes(IPAddress.HostToNetworkOrder(s));
        }

        internal static float ReadFloat(Stream stream)
        {
            return (float)ReadShort(stream) / 32;
        }

        internal static short ReadShort(Stream stream)
        {
            short s = BitConverter.ToInt16(new byte[] { (byte)stream.ReadByte(), (byte)stream.ReadByte() }, 0);
            return IPAddress.HostToNetworkOrder(s);
        }

        internal static byte[] ReadArray(Stream stream)
        {
            byte[] b = new byte[1024];
            stream.Read(b, 0, 1024);
            return b;
        }

        internal static string ReadString(Stream stream)
        {
            // I think this is ASCII?
            byte[] b = new byte[64];
            stream.Read(b, 0, 64);
            return Encoding.ASCII.GetString(b).Trim('\x00', ' ');
        }

        internal static string DumpArray(byte[] resp)
        {
            string res = "";
            foreach (byte b in resp)
                res += b.ToString("x2") + ":";
            return res;
        }

        #endregion
    }
}
