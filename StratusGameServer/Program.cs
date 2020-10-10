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
        static Connection masterServer;
        static List<Connection> activeConnections = new List<Connection>();
        static int PlayerCount { get { return activeConnections.Count; } }
        static Dictionary<string, Connection> allClients = new Dictionary<string, Connection>();
        static Dictionary<string, MessageList> clientMessageQues = new Dictionary<string, MessageList>();
        static Dictionary<string, MessageList> clientUDPMessageQues = new Dictionary<string, MessageList>();

        public static Timer heartbeatTimer;
        public static Timer messageQueLoop;

        public static List<Room> rooms = new List<Room>();
        public static int maxRooms = 2;
        public static int maxPlayersPerRoom = 2;

        public static IPEndPoint localEndPoint;

        TransportLayer TransportLayer = new TransportLayer();

        static void Main(string[] args)
        {

            uid = Guid.NewGuid().ToString();
            rooms = new List<Room>();
            for (int i = 0; i < maxRooms; i++)
            {
                rooms.Add(new Room());
            }

            //ConnectToMaster();
            Task.Factory.StartNew(ConnectToMaster);
            Task.Factory.StartNew(StartListening);
            UDPListener();
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
                Connection conn = new Connection();
                conn.socket = client;
                conn.isServer = true;
                conn.uid = uid;
                masterServer = conn;


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
                msg.eventID = (int)NetworkEvent.ServerRegister;
                msg.SetData(new { uid = uid ,ip = localIP, serverReference = s});
                msg.UID = uid;
                SendMessage(conn, msg);
                Console.WriteLine("Waiting for message");

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

        public static void StartListening()
        {

            localEndPoint = new IPEndPoint(IPAddress.Any, 2728);

            //IPEndPoint localEndPoint = new IPEndPoint((IPAddress)Dns.GetHostAddresses("localhost").GetValue(0), 2727);
            Console.WriteLine(localEndPoint.Address.MapToIPv4());
            Console.WriteLine($"StartListening {localEndPoint.Address.MapToIPv4()}");
            //IPEndPoint localEndPoint = new IPEndPoint(ipAddress, 2728);
            listenSocket = new Socket(localEndPoint.AddressFamily, System.Net.Sockets.SocketType.Stream, ProtocolType.Tcp);
            messageQueLoop = new Timer(OnProcessMessageQue, null, 100, 100);

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

        private static void OnProcessMessageQue(object state)
        {
            foreach (var item in clientMessageQues)
            {
                if (!item.Value.isBusy)
                {
                    var message = item.Value.GetNextMessage();
                    if (message != null)
                    {
                        SendQuedMessage(message, allClients[item.Key]);
                    }
                }
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
                IPEndPoint sender = new IPEndPoint(IPAddress.Any, 0);
                EndPoint tempRemoteEP = (EndPoint)sender;
                state.udp_socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                Console.WriteLine($"Connection Accepted :{state.address}");
                MessageWrapper w = new MessageWrapper();
                w.client = state;
                w.buffer = new byte[1024];
                w.client.socket.BeginReceive(w.buffer, 0, 1024, 0, new AsyncCallback(OnClientReadCallback), w);
            }
            catch (Exception e)
            {

                Console.WriteLine(e);
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
                    if (bytesRead == 4)
                    {
                        var foo = new List<byte>(state.incomingBuffer);
                        var bar = foo.GetRange(0, bytesRead).ToArray();
                        state.bufferSize = BitConverter.ToInt32(bar, 0);
                        //Console.WriteLine($"Incoming Message : {state.bufferSize} bytes");
                        state.incomingBuffer = new byte[1024];
                        state.totalBuffer.Clear();
                        handler.BeginReceive(state.incomingBuffer, 0, 1024, 0, new AsyncCallback(ReadCallback), state);

                    }
                    else
                    {
                        var foo = new List<byte>(state.incomingBuffer);
                        foo.RemoveRange(bytesRead, state.incomingBuffer.Length-bytesRead);
                        state.totalBuffer.AddRange(foo);

                        if (state.totalBuffer.Count == state.bufferSize)
                        {
                            BinaryFormatter bf = new BinaryFormatter();
                            NetworkMessage message = bf.Deserialize(new MemoryStream(state.totalBuffer.ToArray())) as NetworkMessage;
                            Console.WriteLine($"Got message {message.eventID} from {state.address}");

                            ParseNetworkMessage(message, state);

                            state.incomingBuffer = new byte[1024];
                            state.bufferSize = 0;
                            state.totalBuffer.Clear();
                            Console.WriteLine("Waiting for message");

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
                    allClients.Add(message.UID, conn);
                    clientMessageQues.Add(message.UID, new MessageList());
                    AssignClientToRoom(conn);
                    SendStateUpdate();
                    break;
                case NetworkEvent.ClientStateUpdate:
                    var room = GetRoom(conn.room);
                    if (room == null)
                        break;

                    if (room.clientStates.ContainsKey(conn.uid))
                        room.clientStates[conn.uid] = message.data;
                    else
                        room.clientStates.Add(conn.uid, message.data);

                    NetworkMessage m = new NetworkMessage();
                    m.UID = conn.uid;
                    m.eventID = (int)NetworkEvent.GameStateUpdate;
                    m.SetData(new { states = room.clientStates });
                    SendMessageToClient(conn, m);
                    Console.WriteLine($"Sent Room state to player {conn.uid}");
                    break;
                case NetworkEvent.ObjectSpawn:
                    var r = GetRoom(conn.room);
                    if (r == null)
                        break;

                    foreach (var item in r.clients)
                    {
                        SendMessageToClient(item, message);
                        Console.WriteLine($"Sending spawned obj to player {conn.uid}");
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

        public static void SendMessage(Connection conn, NetworkMessage message)
        {
            Console.WriteLine($"Sending message {message.eventID} to {conn.address}");
            message.UID = uid;
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
            //Console.WriteLine($"send { conn.outgoingBuffer.Length} bytes");
            conn.socket.EndSend(ar);
            conn.socket.BeginSend(conn.outgoingBuffer, 0, conn.outgoingBuffer.Length, 0, new AsyncCallback(SendCallback), conn);
        }

        private static void SendCallback(IAsyncResult ar)
        {
            try
            {

                var conn = ((Connection)ar.AsyncState);
                // Retrieve the socket from the state object.  
                Socket client = ((Connection)ar.AsyncState).socket;
                // Complete sending the data to the remote device.  
                int bytesSent = conn.socket.EndSend(ar);
                Console.WriteLine($"sent message {bytesSent} bytes");
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }
        }


        #region UDP
        private static EndPoint epFrom = new IPEndPoint(IPAddress.Any, 0);
        public static void SendMessageToClient(Connection conn,NetworkMessage m)
        {
            m.UID = uid;
            clientMessageQues[conn.uid].AddMessageToQue(m,conn.uid);
        }    
        public static void UDPListener()
        {
            Connection c = new Connection();
            c.udp_socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            c.udp_socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.ReuseAddress, true);
            c.udp_socket.Bind(new IPEndPoint(IPAddress.Any, 2729));
            //c.udp_socket.Connect(epFrom);
            EndPoint tempRemoteEP = (EndPoint)epFrom;

            try
            {
                while (true)
                {
                    udpReceived.Reset();
                    StateObject w = new StateObject();
                    w.connection = c;
                    Console.WriteLine("Waiting For UDP");
                    w.connection.udp_socket.BeginReceiveFrom(w.buffer, 0, 1024, 0, ref tempRemoteEP, new AsyncCallback(Client_UDPReadCallback), w);
                    udpReceived.WaitOne();
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
        }
        public static void Client_SendUDPMessage(NetworkMessage m, Connection c)
        {
            StateObject s = new StateObject();
            s.connection = new Connection();
            Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            s.connection.udp_socket = socket;
            s.connection.udp_socket.Connect(c.ip, 2729);
            var packets = MessagePacket.Factory.PacketsFromMessage(m);
            for (int i = 0; i < packets.Count; i++)
            {
                s.sendDone.Reset();
                var buffer = packets[i].Serialize();
                s.connection.udp_socket.BeginSend(buffer, 0, buffer.Length, SocketFlags.None, Client_SendUDPDone, s);
                s.sendDone.WaitOne();
            }
            s.connection.udp_socket.Close();
        }
        public static void Client_SendUDPDone(IAsyncResult ar)
        {
            StateObject so = (StateObject)ar.AsyncState;
            int bytes = so.connection.udp_socket.EndSend(ar);
            so.sendDone.Set();
            Console.WriteLine($"Sent UDP {bytes}");

        }
        private static void Client_UDPReadCallback(IAsyncResult ar)
        {
            Console.WriteLine("Got UDP");
            StateObject so = (StateObject)ar.AsyncState;
            Socket s = so.connection.udp_socket;
            int read = s.EndReceiveFrom(ar, ref epFrom);
            udpReceived.Set();
            if (read > 0)
            {
                MessagePacket p = new MessagePacket();
                p.Parse(so.buffer);

                if (!so.connection.messageBuffers.ContainsKey(p.messageID))
                    so.connection.messageBuffers.Add(p.messageID, new List<MessagePacket>());

                so.connection.messageBuffers[p.messageID].Add(p);

                //End Message
                if (p.packetType == 0)
                {
                    so.connection.messageBuffers[p.messageID].Sort((x, z) => { return x.packetID.CompareTo(z.packetID); });
                    List<byte> totalMessageBuffer = new List<byte>();
                    foreach (var item in so.connection.messageBuffers[p.messageID])
                    {
                        totalMessageBuffer.AddRange(item.packetData);
                    }

                    NetworkMessage message = new NetworkMessage();
                    BinaryFormatter bf = new BinaryFormatter();
                    message = bf.Deserialize(new MemoryStream(totalMessageBuffer.ToArray())) as NetworkMessage;
                    ParseNetworkMessage(message, so.connection);
                }
            }


        }
        #endregion

        #region Client

        public static void SendQuedMessage(MessageWrapper w,Connection c)
        {
            clientMessageQues[c.uid].isBusy = true;
            c.socket.BeginSend(w.sizeBytes, 0, w.sizeBytes.Length, 0, new AsyncCallback(OnClientMessageHeaderSent), w);

        }

        public static void OnClientMessageHeaderSent(IAsyncResult ar)
        {
            var wrapper = ((MessageWrapper)ar.AsyncState);
            var conn = allClients[wrapper.recipient];
            conn.socket.EndSend(ar);
            conn.socket.BeginSend(wrapper.buffer, 0, wrapper.buffer.Length, 0, new AsyncCallback(OnClientMessageSent), wrapper);
        }

        public static void OnClientMessageSent(IAsyncResult ar)
        {
            var wrapper = ((MessageWrapper)ar.AsyncState);
            var conn = allClients[wrapper.recipient];
            clientMessageQues[conn.uid].isBusy = false;          
            conn.socket.EndSend(ar);
        }
        
        private static void OnClientReadCallback(IAsyncResult ar)
        {
            MessageWrapper wrapper = (MessageWrapper)ar.AsyncState;

            try
            {
                int bytesRead = wrapper.client.socket.EndReceive(ar);

                if (bytesRead > 0)
                {
                    if (bytesRead == 4)
                    {
                        var foo = new List<byte>(wrapper.buffer);
                        var bar = foo.GetRange(0, bytesRead).ToArray();
                        wrapper.sizeBytes = bar;
                        wrapper.bufferSize = BitConverter.ToInt32(bar, 0);
                        wrapper.buffer = new byte[1024];
                        wrapper.client.socket.BeginReceive(wrapper.buffer, 0, 1024, 0, new AsyncCallback(OnClientReadCallback), wrapper);
                    }
                    else
                    {
                        wrapper.AddToBuffer(wrapper.buffer, bytesRead);
                        wrapper.buffer = new byte[1024];

                        if (wrapper.totalBuffer.Length >= wrapper.bufferSize)
                        {
                            MessageWrapper w = new MessageWrapper();
                            w.client = wrapper.client;
                            w.client.socket.BeginReceive(w.buffer, 0, 1024, 0, new AsyncCallback(OnClientReadCallback), w);
                            ParseClientNetworkMessage(wrapper);
                        }
                        else
                        {
                            wrapper.buffer = new byte[1024];
                            wrapper.client.socket.BeginReceive(wrapper.buffer, 0, 1024, 0, new AsyncCallback(OnClientReadCallback), wrapper);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
        }

        private static void ParseClientNetworkMessage(MessageWrapper w)
        {

            var message = w.GetMessage();

            if (message == null)
            {
                Console.WriteLine("Ignoring null message");
                return;
            }

            var conn = w.client;

            switch ((NetworkEvent)message.eventID)
            {
                case NetworkEvent.ClientRegister:
                    if (PlayerCount > 50)
                        break;


                    conn.uid = message.UID;
                    activeConnections.Add(conn);
                    allClients.Add(message.UID, conn);
                    clientMessageQues.Add(message.UID, new MessageList());
                    clientUDPMessageQues.Add(message.UID, new MessageList());

                    AssignClientToRoom(conn);
                    SendStateUpdate();
                    break;
                case NetworkEvent.ClientStateUpdate:
                    var room = GetRoom(conn.room);
                    if (room == null)
                        break;

                    if (room.clientStates.ContainsKey(message.GetDataProperty<string>("uid",NetworkMessage.PropType.String)))
                        room.clientStates[message.GetDataProperty<string>("uid", NetworkMessage.PropType.String)] = message.GetDataProperty<string>("states",NetworkMessage.PropType.String);
                    else
                        room.clientStates.Add(message.GetDataProperty<string>("uid", NetworkMessage.PropType.String), message.GetDataProperty<string>("states", NetworkMessage.PropType.String));

                    NetworkMessage m = new NetworkMessage();
                    m.UID = conn.uid;
                    m.eventID = (int)NetworkEvent.GameStateUpdate;
                    m.data = room.GetSateData();
                    Client_SendUDPMessage(m,conn);
                    Console.WriteLine($"Sent Room state to player {conn.uid}");
                    break;
                case NetworkEvent.ObjectSpawn:
                case NetworkEvent.PlayerSpawn:
                    var r = GetRoom(conn.room);
                    if (r == null)
                        break;

                    foreach (var item in r.clients)
                    {
                        SendMessageToClient(item, message);
                        Console.WriteLine($"Sending spawned obj to player {item.uid}");
                    }
                    break;
            }
        }

        #endregion

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
            var item = GetMatchMakingRoom();
            conn.room = item.uid;
            item.clients.Add(conn);
            //NetworkMessage m = new NetworkMessage();
            //m.UID = conn.uid;
            //m.eventID = (int)NetworkEvent.ServerAck;
            //m.SetData(new { states = item.clientStates,room = conn.room });
            //SendMessageToClient(conn, m);

            item.isPlaying = true;
            foreach (var c in item.clients)
            {
                NetworkMessage ms = new NetworkMessage();
                ms.UID = conn.uid;
                ms.eventID = (int)NetworkEvent.GameStart;
                ms.SetData(new { level = "TestLevel", mode = "TestMode" });
                SendMessageToClient(c, ms);
            }
            
        }
    }
}
