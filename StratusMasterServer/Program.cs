using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.Serialization.Formatters.Binary;
using StratusCloudNetworking;
using System.Threading;

namespace StratusMasterServer
{
    public class Program
    {
        static List<Connection> activeConnections = new List<Connection>();
        static Dictionary<string, Connection> registeredServers = new Dictionary<string, Connection>();
        static Dictionary<IPEndPoint, Connection> allServersByEP = new Dictionary<IPEndPoint, Connection>();

        static List<Connection> clientMatchmakingQue = new List<Connection>();
        public static Timer serverTick;

        public static TransportLayer TransportLayer = new TransportLayer();


        static void Main(string[] args)
        {
            serverTick = new Timer(OnServerTick, null, 0, 20);
            TransportLayer.onConnectedToRemote += OnConnectedToRemote;
            TransportLayer.onReceivedMessage += OnReceivedMessage;
            TransportLayer.onSentTCPToRemote += OnSentTCPToRemote;
            TransportLayer.onSentUDPToRemote += OnSentUDPToRemote;

            TransportConfig c = new TransportConfig()
            {
                masterServer = true,
                tcpInPort = 2727,
                tcpOutPort = 2728,
            };

            TransportLayer.Initialize(c);
        }

        private static void OnServerTick(object state)
        {
            ProcessMatchMakingQue();
        }

        private static void ProcessMatchMakingQue()
        {
            if (clientMatchmakingQue.Count == 0)
                return;

            var client = clientMatchmakingQue[0];
            clientMatchmakingQue.RemoveAt(0);

            foreach (var item in registeredServers)
            {
                var sref = item.Value.serverReference;
                Console.WriteLine(sref);

                if (sref != null)
                {
                    foreach (var room in sref.rooms)
                    {
                        if (client.isHostedRoom)
                        {
                            if(!room.isPlaying && room.clients == 0)
                            {
                                NetworkMessage message = new NetworkMessage();
                                message.eventID = (int)NetworkEvent.StartHostedRoom;
                                message.SetData(new { ip = sref.ip });
                                Console.WriteLine($"Found Empty Room {sref.ip}");
                                TransportLayer.SendTCP(message,client.endPoint);
                                return;
                            }
                        }
                        else
                        {
                            if(!room.isPlaying && room.clients < 50)
                            {
                                NetworkMessage message = new NetworkMessage();
                                message.eventID = (int)NetworkEvent.MasterMatchResponse;
                                message.SetData(new { ip = sref.ip });
                                Console.WriteLine($"Match Response {sref.ip}");
                                TransportLayer.SendTCP(message, client.endPoint);

                                return;
                            }
                        }
                    }
                }
            }

            Console.WriteLine($"Could not find room");
            clientMatchmakingQue.Add(client);
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

        private static void ParseNetworkMessage(NetworkMessage message, IPEndPoint conn)
        {
            //var data = SimpleJSON.JSON.Parse(message.data);
            //Console.WriteLine($"Message Event : {message.eventID}");

            Console.WriteLine($"Got message {message.eventID} from {conn}");
            Connection connection;
            allServersByEP.TryGetValue(conn, out connection);
            if(connection == null)
            {
                connection = new Connection();
                connection.uid = message.UID;
                connection.endPoint = conn;
            }

            switch ((NetworkEvent)message.eventID)
            {
                case NetworkEvent.ServerRegister:
                    connection = new Connection();
                    connection.uid = message.UID;
                    connection.ip = message.GetDataProperty<string>("ip", NetworkMessage.PropType.String);
                    
                    if (!registeredServers.ContainsKey(connection.uid))
                    {
                        Console.WriteLine($"Server registered with uid {connection.uid}");
                        registeredServers.Add(connection.uid, connection);
                        allServersByEP.Add(conn, connection);
                        Console.WriteLine(message.GetDataProperty<string>("serverReference", NetworkMessage.PropType.String));
                        registeredServers[connection.uid].serverReference = ParseServerReference(message.GetDataProperty<string>("serverReference", NetworkMessage.PropType.String));
                        NetworkMessage m = new NetworkMessage();
                        m.eventID = (int)NetworkEvent.ServerRegister;
                        m.UID = "MASTER";
                        TransportLayer.SendTCP(m, conn);

                    }
                    break;
                case NetworkEvent.ServerHeartbeat:
                    //Console.WriteLine($"Server Heartbeat with uid {conn.uid}");
                    connection.lastActive = DateTime.Now;
                    registeredServers[message.UID] = connection;
                    break;
                case NetworkEvent.ClientMatchRequest:
                    if (!clientMatchmakingQue.Contains(connection))
                        clientMatchmakingQue.Add(connection);

                    Console.WriteLine($"Client Match Request {connection.uid}");
                    break;
                case NetworkEvent.StartHostedRoom:
                    connection.isHostedRoom = true;
                    if (!clientMatchmakingQue.Contains(connection))
                        clientMatchmakingQue.Add(connection);

                    break;
                case NetworkEvent.ServerStateUpdate:
                    Console.WriteLine($"ServerStateUpdate {connection.ip}");
                    if (registeredServers.ContainsKey(message.UID))
                    {
                        registeredServers[message.UID].serverReference = (ServerReference)message.GetDataProperty<ServerReference>("serverReference",NetworkMessage.PropType.Object);
                        
                    }
                    break;
            }

        }

        static ServerReference ParseServerReference(string json)
        {
            ServerReference s = new ServerReference();
            var node = SimpleJSON.JSON.Parse(json);
            s.ip = node["ip"].ToString();
            s.uid = node["uid"].ToString();
            var rs = new List<RoomReference>();
            foreach (var item in node["rooms"])
            {
                var room = new RoomReference();
                room.uid = item.Value["uid"].ToString();
                room.clients = (int)item.Value["clients"];
                room.isPlaying = (bool)item.Value["isPlaying"];
                rs.Add(room);

            }
            s.rooms = rs.ToArray();
            return s;
        }
        
    }
}




