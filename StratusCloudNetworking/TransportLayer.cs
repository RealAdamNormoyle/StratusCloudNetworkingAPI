using Open.Nat;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using System.Threading;

namespace StratusCloudNetworking
{
    public class TransportConfig
    {
        public bool masterServer;
        public bool gameServer;
        public bool gameClient;
        public int masterInPort;
        public int masterOutPort;
        public int tcpInPort;
        public int tcpOutPort;
        public int udpInPort;
        public int udpOutPort;
    }

    public class UdpState
    {
        public UdpClient u;
        public IPEndPoint e;
        public Dictionary<int, List<MessagePacket>> messageBuffers = new Dictionary<int, List<MessagePacket>>();

    }


    public class TransportState
    {
        public Socket socket;
        public byte[] buffer = new byte[1024];
        public Dictionary<int, List<MessagePacket>> messageBuffers = new Dictionary<int, List<MessagePacket>>();
    }

    public class TransportLayer
    {
        public TransportConfig Config;

        public Thread tcpThread, udpThread;
        public static string serverIP;

        public string localIP = new System.Net.WebClient().DownloadString("https://api.ipify.org").Trim();

        public Dictionary<EndPoint, TransportState> activeTcpSates = new Dictionary<EndPoint, TransportState>();

        public delegate void ConnectedToRemoteEventHandler(IPEndPoint endPoint);
        public ConnectedToRemoteEventHandler onConnectedToRemote;
        public delegate void SentUDPToRemoteEventHandler(IPEndPoint endPoint);
        public SentUDPToRemoteEventHandler onSentUDPToRemote;
        public delegate void SentTCPToRemoteEventHandler(IPEndPoint endPoint);
        public SentTCPToRemoteEventHandler onSentTCPToRemote;
        public delegate void ReceivedMessageEventHandler(IPEndPoint endPoint, MessageWrapper message);
        public ReceivedMessageEventHandler onReceivedMessage;
        public delegate void RemoteConnectedEventHandler(IPEndPoint endPoint);
        public RemoteConnectedEventHandler onRemoteConnected;

        public void Dispose()
        {
            tcpThread = null;
            udpThread = null;
        }

        public async void Initialize(TransportConfig conf)
        {
            AppDomain.CurrentDomain.ProcessExit += new EventHandler(CurrentDomain_ProcessExit);

            if (conf.gameClient)
            {
                try
                {
                    var discoverer = new NatDiscoverer();
                    var cts = new CancellationTokenSource(10000);
                    var device = await discoverer.DiscoverDeviceAsync(PortMapper.Upnp, cts);
                    await device.CreatePortMapAsync(new Mapping(Protocol.Udp, conf.udpInPort, conf.udpInPort, "Game"));
                }
                catch (Exception e)
                {
                    Console.WriteLine("An Exception has occurred while trying to start upnp!" + e.ToString());
                }
            }

            Config = conf;
            try
            {
                tcpThread = new Thread(new ThreadStart(TcpListen));
                tcpThread.Start();
                Console.WriteLine("Started TCP Listener Thread!\n");
            }
            catch (Exception e)
            {
                Console.WriteLine("An TCP Exception has occurred!" + e.ToString());
                tcpThread.Abort();
            }
            
            if(Config.gameServer)
            {
                try
                {
                    udpThread = new Thread(new ThreadStart(UdpListen));
                    udpThread.Start();
                    Console.WriteLine("Started UDP Receiver Thread!\n");
                }
                catch (Exception e)
                {
                    Console.WriteLine("An UDP Exception has occurred!" + e.ToString());
                    udpThread.Abort();
                }
            }
        }

        private void CurrentDomain_ProcessExit(object sender, EventArgs e)
        {
            Dispose();
        }

        public void StartUDPListener(string ip)
        {
            serverIP = ip;
            try
            {
                udpThread = new Thread(new ThreadStart(UdpListen));
                udpThread.Start();
                Console.WriteLine("Started UDP Receiver Thread!\n");
            }
            catch (Exception e)
            {
                Console.WriteLine("An UDP Exception has occurred!" + e.ToString());
                udpThread.Abort();
            }
        }

        public void UdpListen()
        {

            try
            {
                //Create a UDP socket.
                TransportState state = new TransportState();
                UdpClient listener = new UdpClient(new IPEndPoint(IPAddress.Any, Config.udpInPort));

                if (Config.gameClient)
                {
                    //IPEndPoint endpoint = new IPEndPoint(IPAddress.Parse(serverIP), 2729);
                    //listener.Connect(endpoint);
                }
                    Console.WriteLine($"Waiting UDP {listener.Client.LocalEndPoint}");

                while (true)
                {
                    byte[] received = new byte[1024];
                    IPEndPoint tmpIpEndPoint = new IPEndPoint(IPAddress.Any, 0);
                    IPEndPoint remoteEP = new IPEndPoint(IPAddress.Any, 0);
                    Console.WriteLine($"Waiting for UDP data {listener.Client.LocalEndPoint}");
                    received = listener.Receive(ref remoteEP);
                    //listener.BeginReceive(new AsyncCallback(UdpReadCallback), state);

                    int bytesReceived = state.buffer.Length;
                    Console.WriteLine($"INCOMING UDP {remoteEP}");
                    if (bytesReceived > 0)
                    {
                        MessagePacket p = new MessagePacket();
                        if (p.Parse(received))
                        {
                            if (!state.messageBuffers.ContainsKey(p.messageID))
                                state.messageBuffers.Add(p.messageID, new List<MessagePacket>());

                            state.messageBuffers[p.messageID].Add(p);

                            //End Message
                            if (p.packetType == 0)
                            {
                                state.messageBuffers[p.messageID].Sort((x, z) => { return x.packetID.CompareTo(z.packetID); });
                                var m = MessagePacket.Factory.MessageFromPackets(state.messageBuffers[p.messageID]);
                                MessageWrapper w = new MessageWrapper(m);
                                Console.WriteLine($"UDP {m.data}");

                                state.messageBuffers.Remove(p.messageID);
                                onReceivedMessage?.Invoke((IPEndPoint)remoteEP, w);
                            }
                        }
                    }

                }
            }
            catch (SocketException se)
            {
                Console.WriteLine("A Socket Exception has occurred!" + se.ToString());
                //UdpListen();
            }
        }

        public void TcpListen()
        {
            //Create an instance of TcpListener to listen for TCP connection.
            TcpListener tcpListener = new TcpListener(IPAddress.Any, Config.tcpInPort);
            try
            {
                while (true)
                {
                    tcpListener.Start();
                    TransportState state = new TransportState();
                    //Program blocks on Accept() until a client connects.
                    state.socket = tcpListener.AcceptSocket();
                    activeTcpSates.Add(state.socket.RemoteEndPoint, state);
                    state.socket.BeginReceive(state.buffer, 0, 1024, 0, new AsyncCallback(TcpReadCallback), state);
                    tcpListener.Stop();
                }
            }
            catch (SocketException se)
            {
                Console.WriteLine("A Socket Exception has occurred!" + se.ToString());
            }
        }


        public void TcpReadCallback(IAsyncResult ar)
        {
            try
            {
                TransportState state = (TransportState)ar.AsyncState;
                int read = state.socket.EndReceive(ar);
                if (read > 0)
                {
                    MessagePacket p = new MessagePacket();
                    if (p.Parse(state.buffer))
                    {
                        if (!state.messageBuffers.ContainsKey(p.messageID))
                            state.messageBuffers.Add(p.messageID, new List<MessagePacket>());

                        if (p.packetData.Length > 0)
                            state.messageBuffers[p.messageID].Add(p);

                        //End Message
                        if (p.packetType == 0)
                        {

                            state.messageBuffers[p.messageID].Sort(delegate (MessagePacket x, MessagePacket y){return ComparePackets(x, y);});
                            var m = MessagePacket.Factory.MessageFromPackets(state.messageBuffers[p.messageID]);
                            if(m != null)
                            {
                                MessageWrapper w = new MessageWrapper(m);
                                onReceivedMessage?.Invoke((IPEndPoint)state.socket.RemoteEndPoint, w);
                            }
                        
                        }
                    }
                }

                state.buffer = new byte[1024];
                state.socket.BeginReceive(state.buffer, 0, 1024, 0, new AsyncCallback(TcpReadCallback), state);

            }
            catch (Exception e )
            {
                Console.WriteLine(e);
            }
        }
   
        public void SendUDP(NetworkMessage message, EndPoint remote)
        {
            IPEndPoint ep = new IPEndPoint(((IPEndPoint)remote).Address, Config.udpOutPort);

            //if (Config.gameServer)
            //{
            //    ep = (IPEndPoint)remote;
            //}

            TransportState state = new TransportState();
            var packets = MessagePacket.Factory.PacketsFromMessage(message);

            UdpClient senderClient = new UdpClient();
            senderClient.Connect(((IPEndPoint)ep));
            
            for (int i = 0; i < packets.Count; i++)
            {
                //state.socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                //state.socket.Connect(ep);
                var buffer = packets[i].Serialize();
                Console.WriteLine($"UDP Transport {ep} > {buffer.Length}");
                //state.socket.SendTo(buffer, 0, buffer.Length, SocketFlags.None, ep);
                senderClient.Send(buffer, buffer.Length);
                //state.socket.Close();
            }

            
            onSentUDPToRemote?.Invoke((IPEndPoint)ep);
        }

        public void SendTCP(NetworkMessage message, EndPoint remote)
        {
            IPEndPoint ep =  (IPEndPoint)remote;

            if (!activeTcpSates.ContainsKey(ep))
            {
                ConnectTo(ep);
            }

            Console.WriteLine($"Sending message {message.eventID} to {ep}");
            var packets = MessagePacket.Factory.PacketsFromMessage(message);
            for (int i = 0; i < packets.Count; i++)
            {
                var buffer = packets[i].Serialize();
                activeTcpSates[ep].socket.Send(buffer, 0, buffer.Length, SocketFlags.None);
            }
            Console.WriteLine("Sent Message");

            onSentTCPToRemote?.Invoke((IPEndPoint)activeTcpSates[ep].socket.RemoteEndPoint);
        }

        public void ConnectTo(EndPoint remote, Action callback = null)
        {
            TransportState state = new TransportState();
            state.socket = new Socket(AddressFamily.InterNetwork, System.Net.Sockets.SocketType.Stream, ProtocolType.Tcp);
            // Connect to the remote endpoint.  
            activeTcpSates.Add(remote, state);
            state.socket.Connect(remote);
            state.socket.BeginReceive(state.buffer, 0, 1024, 0, new AsyncCallback(TcpReadCallback), state);
            onConnectedToRemote?.Invoke((IPEndPoint)state.socket.RemoteEndPoint);
            callback?.Invoke();
        }

        public int ComparePackets(MessagePacket p1,MessagePacket p2)
        {
            return p1.messageID.CompareTo(p2.messageID);
        }
    }
}
