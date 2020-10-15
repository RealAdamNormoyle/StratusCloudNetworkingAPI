using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.Serialization.Formatters.Binary;
using StratusCloudNetworking;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;

namespace StratusGameServer
{
    class Program
    {
        public static string masterIP = "ec2-52-17-186-16.eu-west-1.compute.amazonaws.com";
        public static string uid;
        public static bool connectedToMaster;
        static Connection masterServer;
        static List<Connection> activeConnections = new List<Connection>();
        static int PlayerCount { get { return activeConnections.Count; } }

        static Dictionary<string, Connection> allClients = new Dictionary<string, Connection>();
        static Dictionary<IPEndPoint, Connection> allClientsByEP = new Dictionary<IPEndPoint, Connection>();

        public static Timer heartbeatTimer;

        public static List<Room> rooms = new List<Room>();
        public static int maxRooms = 2;
        public static int maxPlayersPerRoom = 2;

        public static IPEndPoint localEndPoint;

        public static TransportLayer TransportLayer = new TransportLayer();

        static void Main(string[] args)
        {
            TransportLayer.onConnectedToRemote += OnConnectedToRemote;
            TransportLayer.onReceivedMessage += OnReceivedMessage;
            TransportLayer.onSentTCPToRemote += OnSentTCPToRemote;
            TransportLayer.onSentUDPToRemote += OnSentUDPToRemote;
            TransportLayer.onRemoteConnected += OnRemoteConnected;

            TransportConfig c = new TransportConfig()
            {
                gameServer = true,
                masterInPort = 2727,
                masterOutPort = 2727,
                tcpInPort = 2728,
                tcpOutPort = 2728,
                udpInPort = 2729,
                udpOutPort = 2729,
            };



            uid = Guid.NewGuid().ToString();
            rooms = new List<Room>();
            for (int i = 0; i < maxRooms; i++)
            {
                rooms.Add(new Room());
            }

            TransportLayer.Initialize(c);
            ConnectToMaster();

        }

        private static void OnRemoteConnected(IPEndPoint endPoint)
        {
            
        }

        private static void OnSentUDPToRemote(IPEndPoint endPoint)
        {

        }

        private static void OnSentTCPToRemote(IPEndPoint endPoint)
        {

        }

        private static void OnReceivedMessage(IPEndPoint endPoint, MessageWrapper message)
        {
            ParseNetworkMessage(message.GetMessage(), endPoint);
        }

        private static void OnConnectedToRemote(IPEndPoint endPoint)
        {

        }

        private static void OnHeartbeatTimer(object state)
        {
            //NetworkMessage msg = new NetworkMessage();
            //msg.UID = uid;
            //msg.eventID = (int)NetworkEvent.ServerHeartbeat;
            //TransportLayer.SendTCP(msg, masterServer.endPoint);
            SendStateUpdate();
        }

        public static void ConnectToMaster()
        {
            IPHostEntry ipHostInfo = Dns.GetHostEntry(masterIP);
            IPAddress ipAddress = ipHostInfo.AddressList[0];
            IPEndPoint remoteEP = new IPEndPoint(ipAddress, 2727);
            masterServer = new Connection();
            masterServer.endPoint = remoteEP;
            TransportLayer.ConnectTo(remoteEP,OnConnectedToMaster);
        }

        public static void OnConnectedToMaster()
        {
            ServerReference s = new ServerReference();
            s.ip = TransportLayer.localIP;
            s.rooms = new RoomReference[rooms.Count];
            int i = 0;
            foreach (var item in rooms)
            {
                RoomReference r = new RoomReference();
                r.isPlaying = item.isPlaying;
                r.clients = item.clients.Count;
                r.uid = item.uid;
                s.rooms[i] = r;
                i++;
            }

            NetworkMessage msg = new NetworkMessage();
            msg.eventID = (int)NetworkEvent.ServerRegister;
            msg.SetData(new { uid = uid, ip = TransportLayer.localIP, serverReference = s });
            msg.UID = uid;

            TransportLayer.SendTCP(msg, masterServer.endPoint);
        }
        
        private static void ParseNetworkMessage(NetworkMessage message, IPEndPoint conn)
        {
            //var data = SimpleJSON.JSON.Parse(message.data);
            if (message == null || string.IsNullOrEmpty(message.UID))
                return;

            IPEndPoint remoteEPUDP = new IPEndPoint(conn.Address, TransportLayer.Config.udpOutPort);
            IPEndPoint remoteEPTCP = new IPEndPoint(conn.Address, 2728);

            Connection connection;
            if(!allClients.TryGetValue(message.UID, out connection))
            {
                connection = new Connection();
                connection.endPoint = conn;
                connection.uid = message.UID;
                activeConnections.Add(connection);
                allClients.Add(message.UID, connection);
                allClientsByEP.Add(conn, connection);
            }

            switch ((NetworkEvent)message.eventID)
            {
                case NetworkEvent.ServerRegister:
                    connectedToMaster = true;
                    Console.WriteLine("Connected To Master");
                    SendStateUpdate();
                    heartbeatTimer = new Timer(OnHeartbeatTimer, null, 1000, 1000);
                    break;
                case NetworkEvent.ClientRegister:
                    if (PlayerCount > 50)
                        break;
                    AssignClientToRoom(connection);
                    SendStateUpdate();
                    break;
                case NetworkEvent.ClientStateUpdate:
                    var room = GetRoom(connection.room);
                    if (room == null)
                    {
                        Console.WriteLine("No Room available");
                        break;
                    }
                    ClientState state = ClientState.FromJson(message.data);

                    if (room.clientStates.ContainsKey(message.UID))
                        room.clientStates[message.UID] = state;
                    else
                        room.clientStates.Add(message.UID, state);

                    NetworkMessage m = new NetworkMessage();
                    m.UID = "SERVER";
                    m.eventID = (int)NetworkEvent.GameStateUpdate;
                    m.SetData(Newtonsoft.Json.JsonConvert.SerializeObject(room.GetSateData()));
                    Console.WriteLine($"Prepped Room state for player {conn} : {m.data}");
                    TransportLayer.SendUDP(m, conn);
                    
                    //TransportLayer.SendTCP(m, connection.endPoint);
                    Console.WriteLine($"Sent Room state to player {conn}");
                    break;
                case NetworkEvent.ObjectSpawn:
                case NetworkEvent.PlayerSpawn:
                    var r = GetRoom(connection.room);
                    Console.WriteLine($"PlayerSpawn");
                    if (r == null)
                        break;

                    foreach (var item in r.clients)
                    {
                        if (r.uid == message.UID)
                            continue;

                        TransportLayer.SendTCP(message, item.endPoint);
                        Console.WriteLine($"Sending spawned obj to player {item.endPoint}");
                    }
                    break;
            }
        }

        public static Room GetRoom(string uid) 
        {
            foreach (var item in rooms)
            {
                if (item.uid == uid)
                {
                    return item;
                }
            }

            return null;

        }

        public static Room GetMatchMakingRoom()
        {
            Room room = rooms[0];
            foreach (var item in rooms)
            {
                if(item.clients.Count < maxPlayersPerRoom && item.isPlaying == false)
                {
                    if(item.clients.Count > room.clients.Count)
                    {
                        room = item;
                    }
                }
            }
            return room;
        }

        public static void SendStateUpdate()
        {
            ServerReference s = new ServerReference();
            s.ip = TransportLayer.localIP;
            s.rooms = new RoomReference[rooms.Count];
            int i = 0;
            foreach (var item in rooms)
            {
                RoomReference r = new RoomReference();
                r.isPlaying = item.isPlaying;
                r.clients = item.clients.Count;
                r.uid = item.uid;
                s.rooms[i] = r;
                i++;
            }

            NetworkMessage msg = new NetworkMessage();
            msg.UID = uid;
            msg.SetData(new { serverReference = s});
            msg.eventID = (int)NetworkEvent.ServerStateUpdate;
            TransportLayer.SendTCP(msg, masterServer.endPoint);
        }
    
        public static void AssignClientToRoom(Connection conn)
        {
            var item = GetMatchMakingRoom();
            conn.room = item.uid;
            item.clients.Add(conn);
            //foreach (var c in item.clients)
            //{
            //    NetworkMessage ms = new NetworkMessage();
            //    ms.UID = conn.uid;
            //    ms.eventID = (int)NetworkEvent.GameStart;
            //    ms.SetData(new { level = "TestLevel", mode = "TestMode" });
            //    TransportLayer.SendTCP(ms, new IPEndPoint(c.endPoint.Address,2828));
            //}
            
        }
    }
}
