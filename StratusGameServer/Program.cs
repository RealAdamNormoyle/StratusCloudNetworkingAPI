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
        public static string localIP;
        public static string uid;
        public static bool connectedToMaster;
        static Socket listenSocket;
        static Socket masterSocket;
        static Connection masterServer;
        static List<Connection> activeConnections = new List<Connection>();
        static int PlayerCount { get { return activeConnections.Count; } }

        public static Timer heartbeatTimer;
        public static ConcurrentBag<Room> rooms = new ConcurrentBag<Room>();
        public static int maxRooms = 20;
        public static int maxPlayersPerRoom = 50;

        // ManualResetEvent instances signal completion.  
        private static ManualResetEvent connectDone =
            new ManualResetEvent(false);
        private static ManualResetEvent sendDone =
            new ManualResetEvent(false);
        private static ManualResetEvent receiveDone =
            new ManualResetEvent(false);
        private static ManualResetEvent listenDone =
            new ManualResetEvent(false);
        public static ManualResetEvent messageParsed = new ManualResetEvent(false);



        static void Main(string[] args)
        {
            uid = Guid.NewGuid().ToString();
            rooms = new ConcurrentBag<Room>();
            for (int i = 0; i < maxRooms; i++)
            {
                rooms.Add(new Room());
            }

            localIP = new System.Net.WebClient().DownloadString("https://api.ipify.org").Trim();

            //ConnectToMaster();
            Task.Factory.StartNew(ConnectToMaster);
            StartListening();
        }

        private static void OnHeartbeatTimer(object state)
        {
            NetworkMessage msg = new NetworkMessage();
            msg.UID = uid;
            msg.eventID = (int)NetworkEvent.ServerHeartbeat;
            SendMessage(masterServer, msg);
        }

        public static void ConnectToMaster()
        {
            IPHostEntry ipHostInfo = Dns.GetHostEntry(masterIP);
            IPAddress ipAddress = ipHostInfo.AddressList[0];
            Console.WriteLine(ipAddress.MapToIPv4().ToString());
            IPEndPoint remoteEP = new IPEndPoint(ipAddress, 2727);
            masterSocket = new Socket(ipAddress.AddressFamily, System.Net.Sockets.SocketType.Stream, ProtocolType.Tcp);
            // Connect to the remote endpoint.  
            masterSocket.BeginConnect(remoteEP,
                new AsyncCallback(MasterConnectCallback), masterSocket);
            
        }

        private static void MasterConnectCallback(IAsyncResult ar)
        {
            try
            {
                // Retrieve the socket from the state object.  
                Socket client = (Socket)ar.AsyncState;

                // Complete the connection.  
                client.EndConnect(ar);

                Console.WriteLine("Socket connected to {0} with uid {1}",
                    client.RemoteEndPoint.ToString(),uid);

                // Signal that the connection has been made.  
                connectDone.Set();
                sendDone.Reset();
                Connection conn = new Connection();
                conn.socket = client;
                conn.isServer = true;
                conn.uid = uid;
                masterServer = conn;
                NetworkMessage msg = new NetworkMessage();
                msg.eventID = (int)NetworkEvent.ServerRegister;
                msg.SetData(new { uid = uid ,ip = localIP});
                msg.UID = uid;
                SendMessage(conn, msg);
                sendDone.WaitOne();

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

        public static void StartListening()
        {

            //IPHostEntry ipHostInfo = Dns.GetHostEntry(Dns.GetHostName());
            //IPAddress ipAddress = ipHostInfo.AddressList[0];
            //Console.WriteLine(Dns.GetHostName());

            IPEndPoint localEndPoint = new IPEndPoint((IPAddress)Dns.GetHostAddresses("ec2-52-17-186-16.eu-west-1.compute.amazonaws.com").GetValue(0), 2728);
            Console.WriteLine(localEndPoint.Address.MapToIPv4());
            Console.WriteLine($"StartListening {localEndPoint.Address.MapToIPv4()}");
            //IPEndPoint localEndPoint = new IPEndPoint(ipAddress, 2728);
            listenSocket = new Socket(localEndPoint.AddressFamily, System.Net.Sockets.SocketType.Stream, ProtocolType.Tcp);

            try
            {
                listenSocket.Bind(localEndPoint);
                listenSocket.Listen(100);
                listenSocket.Blocking = false;
                while (true)
                {
                    // Set the event to nonsignaled state.  
                    listenDone.Reset();

                    // Start an asynchronous socket to listen for connections.  
                    Console.WriteLine("Waiting for a connection...");
                    listenSocket.BeginAccept(new AsyncCallback(AcceptCallback), listenSocket);

                    // Wait until a connection is made before continuing.  
                    listenDone.WaitOne();
                }

            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }

        }

        public static void AcceptCallback(IAsyncResult ar)
        {

            // Signal the main thread to continue.  
            listenDone.Set();

            try
            {
                // Get the socket that handles the client request.  
                Socket listener = (Socket)ar.AsyncState;
                Socket handler = listener.EndAccept(ar);

                // Create the state object.  
                Connection state = new Connection();
                state.socket = handler;
                Console.WriteLine($"Connection Accepted :{state.address}");
                handler.Blocking = false;


                handler.BeginReceive(state.incomingBuffer, 0, 1024, 0, new AsyncCallback(ReadCallback), state);

            }
            catch (Exception)
            {

                throw;
            }
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
                    if (bytesRead == 0)
                    {
                        var foo = new List<byte>(state.incomingBuffer);
                        var bar = foo.GetRange(0, bytesRead).ToArray();
                        state.bufferSize = BitConverter.ToInt32(bar, 0);
                        Console.WriteLine($"Incoming Message : {state.bufferSize} bytes");
                        state.incomingBuffer = new byte[1024];
                        state.totalBuffer.Clear();
                        handler.BeginReceive(state.incomingBuffer, 0, 1024, 0, new AsyncCallback(ReadCallback), state);

                    }
                    else if(state.bufferSize > 0)
                    {
                        var foo = new List<byte>(state.incomingBuffer);
                        foo.RemoveRange(bytesRead, state.incomingBuffer.Length-bytesRead);
                        state.totalBuffer.AddRange(foo);

                        if (state.totalBuffer.Count == state.bufferSize)
                        {
                            messageParsed.Reset();
                            BinaryFormatter bf = new BinaryFormatter();
                            NetworkMessage message = bf.Deserialize(new MemoryStream(state.totalBuffer.ToArray())) as NetworkMessage;
                            Console.WriteLine($"Got message {message.eventID} from {state.ip}");

                            ParseNetworkMessage(message, state);
                            messageParsed.WaitOne();

                            state.incomingBuffer = new byte[1024];
                            state.bufferSize = 0;
                            state.totalBuffer.Clear();
                            state.socket.BeginReceive(state.incomingBuffer, 0, 1024, 0, new AsyncCallback(ReadCallback), state);
                        
                        }
                        else
                        {
                            state.incomingBuffer = new byte[1024];
                            state.socket.BeginReceive(state.incomingBuffer, 0, 1024, 0, new AsyncCallback(ReadCallback), state);
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

                    conn.uid = message.UID;
                    activeConnections.Add(conn);
                    AssignClientToRoom(conn);
                    SendStateUpdate();
                    break;
                case NetworkEvent.ClientStateUpdate:
                    foreach (var item in rooms)
                    {
                        if (item.uid == conn.room)
                        {
                            if (item.clientStates.ContainsKey(conn.uid))
                                item.clientStates[conn.uid] = message.data;
                            else
                                item.clientStates.Add(conn.uid, message.data);

                            NetworkMessage m = new NetworkMessage();
                            m.UID = conn.uid;
                            m.eventID = (int)NetworkEvent.GameStateUpdate;
                            m.SetData(new { states = item.clientStates });
                            SendMessage(conn, m);




                            break;
                        }
                    }
                    break;
            }

            messageParsed.Set();
        }

        public static void SendMessage(Connection conn, NetworkMessage message)
        {
            Console.WriteLine($"Sending message {message.eventID} to {conn.ip}");
            message.UID = uid;
            BinaryFormatter bf = new BinaryFormatter();
            MemoryStream st = new MemoryStream();
            bf.Serialize(st, message);
            byte[] buffer = st.GetBuffer();
            byte[] byteDataLength = BitConverter.GetBytes(buffer.Length);
            conn.outgoingBuffer = buffer;
            Console.WriteLine($"sending { BitConverter.ToInt32(byteDataLength)} bytes");

            conn.socket.BeginSend(byteDataLength, 0, byteDataLength.Length, 0, new AsyncCallback(OnMessageHeaderSent), conn);
        }

        public static void OnMessageHeaderSent(IAsyncResult ar)
        {
            var conn = ((Connection)ar.AsyncState);
            Console.WriteLine($"send { conn.outgoingBuffer.Length} bytes");
            conn.socket.EndSend(ar);
            conn.socket.BeginSend(conn.outgoingBuffer, 0, conn.outgoingBuffer.Length, 0, new AsyncCallback(SendCallback), conn);
        }

        private static void SendCallback(IAsyncResult ar)
        {
            sendDone.Set();

            try
            {

                var conn = ((Connection)ar.AsyncState);
                // Retrieve the socket from the state object.  
                Socket client = ((Connection)ar.AsyncState).socket;
                // Complete sending the data to the remote device.  
                int bytesSent = client.EndSend(ar);
                Console.WriteLine($"sent message {bytesSent} bytes");

                conn.incomingBuffer = new byte[1024];
                conn.bufferSize = 0;
                conn.totalBuffer.Clear();
                conn.socket.BeginReceive(conn.incomingBuffer, 0, 1024, 0, new AsyncCallback(ReadCallback), conn);

            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }
        }
    
        public static void SendStateUpdate()
        {
            ServerReference s = new ServerReference();
            s.ip = localIP;
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
            SendMessage(masterServer, msg);
        }
    
        public static void AssignClientToRoom(Connection conn)
        {
            foreach (var item in rooms)
            {
                if(!item.isPlaying && item.clients.Count < maxPlayersPerRoom)
                {
                    conn.room = item.uid;
                    item.clients.Add(conn);
                    NetworkMessage m = new NetworkMessage();
                    m.UID = conn.uid;
                    m.eventID = (int)NetworkEvent.ServerAck;
                    m.SetData(new { states = item.clientStates,room = conn.room });
                    SendMessage(conn, m);
                    return;
                }
            }
        }
    }
}
