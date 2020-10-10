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
    public class TransportState
    {
        public Socket socket;
        public byte[] buffer = new byte[1024];
        public Dictionary<int, List<MessagePacket>> messageBuffers = new Dictionary<int, List<MessagePacket>>();
    }

    public class TransportLayer
    {
        public const int TcpPort = 2728;
        public const int UdpPort = 2729;
        public Thread tcpThread, udpThread;

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

        public void Initialize()
        {
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
                Socket soUdp = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                IPEndPoint localIpEndPoint = new IPEndPoint(IPAddress.Any, UdpPort);
                soUdp.Bind(localIpEndPoint);
                TransportState state = new TransportState();
                while (true)
                {
                    byte[] received = new byte[1024];
                    IPEndPoint tmpIpEndPoint = new IPEndPoint(IPAddress.Any, UdpPort);
                    EndPoint remoteEP = (tmpIpEndPoint);
                    int bytesReceived = soUdp.ReceiveFrom(state.buffer, ref remoteEP);
                    Console.WriteLine("INCOMING UDP");

                    if (bytesReceived > 0)
                    {
                        MessagePacket p = new MessagePacket();
                        p.Parse(state.buffer);

                        if (!state.messageBuffers.ContainsKey(p.messageID))
                            state.messageBuffers.Add(p.messageID, new List<MessagePacket>());

                        state.messageBuffers[p.messageID].Add(p);

                        //End Message
                        if (p.packetType == 0)
                        {
                            state.messageBuffers[p.messageID].Sort((x, z) => { return x.packetID.CompareTo(z.packetID); });
                            List<byte> totalMessageBuffer = new List<byte>();
                            foreach (var item in state.messageBuffers[p.messageID])
                            {
                                totalMessageBuffer.AddRange(item.packetData);
                            }

                            MessageWrapper w = new MessageWrapper(totalMessageBuffer.ToArray());
                            onReceivedMessage?.Invoke((IPEndPoint)state.socket.RemoteEndPoint, w);
                        }
                    }

                    String dataReceived = System.Text.Encoding.ASCII.GetString(received);
                }
            }
            catch (SocketException se)
            {
                Console.WriteLine("A Socket Exception has occurred!" + se.ToString());
            }
        }

        public void TcpListen()
        {
            //Create an instance of TcpListener to listen for TCP connection.
            TcpListener tcpListener = new TcpListener(IPAddress.Any,TcpPort);
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
                    p.Parse(state.buffer);

                    if (!state.messageBuffers.ContainsKey(p.messageID))
                        state.messageBuffers.Add(p.messageID, new List<MessagePacket>());

                    state.messageBuffers[p.messageID].Add(p);

                    //End Message
                    if (p.packetType == 0)
                    {
                        state.messageBuffers[p.messageID].Sort((x, z) => { return x.packetID.CompareTo(z.packetID); });
                        List<byte> totalMessageBuffer = new List<byte>();
                        foreach (var item in state.messageBuffers[p.messageID])
                        {
                            totalMessageBuffer.AddRange(item.packetData);
                        }

                        MessageWrapper w = new MessageWrapper(totalMessageBuffer.ToArray());
                        onReceivedMessage?.Invoke((IPEndPoint)state.socket.RemoteEndPoint, w);

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
            TransportState state = new TransportState();
            state.socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            state.socket.Connect(remote);
            var packets = MessagePacket.Factory.PacketsFromMessage(message);
            Console.WriteLine(packets.Count);

            for (int i = 0; i < packets.Count; i++)
            {
                var buffer = packets[i].Serialize();
                state.socket.SendTo(buffer, 0, buffer.Length, SocketFlags.None,remote);
            }
            onSentUDPToRemote?.Invoke((IPEndPoint)state.socket.RemoteEndPoint);
            state.socket.Close();
        }

        public void SendTCP(NetworkMessage message, EndPoint remote)
        {
            if (!activeTcpSates.ContainsKey(remote))
            {
                ConnectTo(remote);
            }
            
            Console.WriteLine($"Sending message {message.eventID} to {remote}");
            var packets = MessagePacket.Factory.PacketsFromMessage(message);
            for (int i = 0; i < packets.Count; i++)
            {
                var buffer = packets[i].Serialize();
                activeTcpSates[remote].socket.Send(buffer, 0, buffer.Length, SocketFlags.None);
            }

            onSentTCPToRemote?.Invoke((IPEndPoint)activeTcpSates[remote].socket.RemoteEndPoint);
        }

        public void ConnectTo(EndPoint remote)
        {
            TransportState state = new TransportState();
            state.socket = new Socket(AddressFamily.InterNetwork, System.Net.Sockets.SocketType.Stream, ProtocolType.Tcp);
            // Connect to the remote endpoint.  
            state.socket.Connect(remote);
            activeTcpSates.Add(state.socket.RemoteEndPoint, state);
            onConnectedToRemote?.Invoke((IPEndPoint)state.socket.RemoteEndPoint);
        }

    }
}
