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

        static Socket socket;
        static List<Connection> activeConnections = new List<Connection>();
        static Dictionary<string, Connection> registeredServers = new Dictionary<string, Connection>();


        public static ManualResetEvent allDone = new ManualResetEvent(false);
        public static ManualResetEvent messageParsed = new ManualResetEvent(false);
        static List<Connection> clientMatchmakingQue = new List<Connection>();
        public static Timer serverTick;

        static void Main(string[] args)
        {
            serverTick = new Timer(OnServerTick, null, 0, 20);
            StartListening();

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
                        if(!room.isPlaying && room.clients < 50)
                        {
                            NetworkMessage message = new NetworkMessage();
                            message.eventID = (int)NetworkEvent.MasterMatchResponse;

                            message.SetData(new { ip = item.Value.ip });
                            Console.WriteLine($"Match Response {item.Value.ip}");

                            SendMessage(client, message);
                            return;
                        }
                    }
                }
            }

            Console.WriteLine($"Could not find room");
            clientMatchmakingQue.Add(client);
        }

        public static void StartListening()
        {

            IPEndPoint localEndPoint = new IPEndPoint((IPAddress)Dns.GetHostAddresses("ec2-52-17-186-16.eu-west-1.compute.amazonaws.com").GetValue(0), 2727);
            //IPEndPoint localEndPoint = new IPEndPoint((IPAddress)Dns.GetHostAddresses("localhost").GetValue(0), 2727);

            Console.WriteLine(localEndPoint.Address.ToString());
            socket = new Socket(localEndPoint.AddressFamily, System.Net.Sockets.SocketType.Stream, ProtocolType.Tcp);
            try
            {
                socket.Bind(localEndPoint);
                socket.Listen(100);

                while (true)
                {
                    // Set the event to nonsignaled state.  
                    allDone.Reset();

                    // Start an asynchronous socket to listen for connections.  
                    Console.WriteLine("Waiting for a connection...");
                    socket.BeginAccept(new AsyncCallback(AcceptCallback), socket);

                    // Wait until a connection is made before continuing.  
                    allDone.WaitOne();
                }

            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }

        }

        private static void AcceptCallback(IAsyncResult ar)
        {
            // Signal the main thread to continue.  
            allDone.Set();

            // Get the socket that handles the client request.  
            Socket listener = (Socket)ar.AsyncState;
            Socket handler = listener.EndAccept(ar);

            // Create the state object.  
            Connection state = new Connection();
            state.socket = handler;
            Console.WriteLine($"Connection Accepted :{state.address}");
            Console.WriteLine("Waiting for message");

            activeConnections.Add(state);
            handler.BeginReceive(state.incomingBuffer, 0, 1024, 0, new AsyncCallback(ReadCallback), state);

        }

        private static void ReadCallback(IAsyncResult ar)
        {
            Connection state = (Connection)ar.AsyncState;
            Socket handler = state.socket;

            try
            {
                int bytesRead = handler.EndReceive(ar);
                if (bytesRead > 0)
                {
                    //Console.WriteLine($"incoming {bytesRead} bytes");
                    if(bytesRead == 4)
                    {
                        //This is a message header
                        var foo = new List<byte>(state.incomingBuffer);
                        var bar = foo.GetRange(0, bytesRead).ToArray();
                        state.bufferSize = BitConverter.ToInt32(bar, 0);
                        //Console.WriteLine($"Incoming Message : {state.bufferSize} bytes");
                        state.incomingBuffer = new byte[1024];
                        state.totalBuffer.Clear();
                        handler.BeginReceive(state.incomingBuffer, 0, 1024, 0, new AsyncCallback(ReadCallback), state);

                    }
                    else if(state.bufferSize > 0)
                    {
                        var foo = new List<byte>(state.incomingBuffer);
                        foo.RemoveRange(bytesRead, state.incomingBuffer.Length - bytesRead);
                        state.totalBuffer.AddRange(foo);

                        //Console.WriteLine($" message size {state.totalBuffer.Count} / {state.bufferSize}");

                        if (state.totalBuffer.Count == state.bufferSize)
                        {

                            messageParsed.Reset();
                            BinaryFormatter bf = new BinaryFormatter();
                            Console.WriteLine($" {state.totalBuffer.Count} bytes");
                            NetworkMessage message = bf.Deserialize(new MemoryStream(state.totalBuffer.ToArray())) as NetworkMessage;
                            state.uid = message.UID;
                            Console.WriteLine($"Finished receiving data from :{state.address} : {state.totalBuffer.Count} bytes");

                            ParseNetworkMessage(message, state);

                            messageParsed.WaitOne();
                            Console.WriteLine("Waiting for message");

                            state.bufferSize = 0;
                            state.incomingBuffer = new byte[1024];
                            state.totalBuffer.Clear();
                            handler.BeginReceive(state.incomingBuffer, 0, 1024, 0, new AsyncCallback(ReadCallback), state);
                        }
                        else
                        {
                            Console.WriteLine("Waiting for message");

                            state.incomingBuffer = new byte[1024];
                            handler.BeginReceive(state.incomingBuffer, 0, 1024, 0, new AsyncCallback(ReadCallback), state);
                        }

                    }

                }

            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
        }

        private static void ParseNetworkMessage(NetworkMessage message, Connection conn)
        {
            //var data = SimpleJSON.JSON.Parse(message.data);
            //Console.WriteLine($"Message Event : {message.eventID}");
            Console.WriteLine($"Got message {message.eventID} from {conn.address}");

            switch ((NetworkEvent)message.eventID)
            {
                case NetworkEvent.ServerRegister:
                    conn.uid = message.UID;
                    conn.ip = message.GetDataProperty<string>("ip", NetworkMessage.PropType.String);
                    
                    if (!registeredServers.ContainsKey(conn.uid))
                    {
                        Console.WriteLine($"Server registered with uid {conn.uid}");
                        registeredServers.Add(conn.uid, conn);
                        Console.WriteLine(message.GetDataProperty<string>("serverReference", NetworkMessage.PropType.String));
                        registeredServers[conn.uid].serverReference = ParseServerReference(message.GetDataProperty<string>("serverReference", NetworkMessage.PropType.String));
                        NetworkMessage m = new NetworkMessage();
                        m.eventID = (int)NetworkEvent.ServerRegister;
                        SendMessage(conn, m);

                    }
                    break;
                case NetworkEvent.ServerHeartbeat:
                    //Console.WriteLine($"Server Heartbeat with uid {conn.uid}");
                    conn.lastActive = DateTime.Now;
                    registeredServers[message.UID] = conn;
                    break;
                case NetworkEvent.ClientMatchRequest:
                    if (!clientMatchmakingQue.Contains(conn))
                        clientMatchmakingQue.Add(conn);

                    Console.WriteLine($"Client Match Request {conn.address}");
                    break;
                case NetworkEvent.ServerStateUpdate:
                    Console.WriteLine($"ServerStateUpdate {conn.ip}");
                    if (registeredServers.ContainsKey(message.UID))
                    {
                        registeredServers[message.UID].serverReference = (ServerReference)message.GetDataProperty<ServerReference>("serverReference",NetworkMessage.PropType.Object);
                        
                    }
                    break;
            }

            messageParsed.Set();

        }

        public static void SendMessage(Connection conn, NetworkMessage message)
        {
            Console.WriteLine($"Sending message {message.eventID} to {conn.address}");
            message.UID = "MASTER";
            BinaryFormatter bf = new BinaryFormatter();
            MemoryStream st = new MemoryStream();
            bf.Serialize(st, message);
            byte[] buffer = st.GetBuffer();
            byte[] byteDataLength = BitConverter.GetBytes(buffer.Length);
            conn.outgoingBuffer = buffer;
            //Console.WriteLine($"sending { BitConverter.ToInt32(byteDataLength)} bytes");

            conn.socket.BeginSend(byteDataLength, 0, byteDataLength.Length, 0, new AsyncCallback(OnMessageHeaderSent), conn);

        }

        public static void OnMessageHeaderSent(IAsyncResult ar)
        {
            var conn = ((Connection)ar.AsyncState);
            conn.socket.EndSend(ar);
            //Console.WriteLine($"send { conn.outgoingBuffer.Length} bytes");
            conn.socket.BeginSend(conn.outgoingBuffer, 0, conn.outgoingBuffer.Length, 0, new AsyncCallback(SendCallback), conn);
        }

        private static void SendCallback(IAsyncResult ar)
        {
            try
            {
                var conn = ((Connection)ar.AsyncState);
                // Retrieve the socket from the state object.  
                Socket client = conn.socket;
                // Complete sending the data to the remote device.  
                int bytesSent = conn.socket.EndSend(ar);
                //Console.WriteLine("Sent {0} bytes to server.", bytesSent);

                //conn.incomingBuffer = new byte[1024];
                //conn.bufferSize = 0;
                //conn.totalBuffer.Clear();
                //conn.socket.BeginReceive(conn.incomingBuffer, 0, 1024, 0, new AsyncCallback(ReadCallback), conn);
            
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
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




