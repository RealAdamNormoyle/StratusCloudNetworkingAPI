using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;

namespace StratusCloudNetworking
{
    public static class StratusCloudNetwork
    {
        public const string masterServer = "masterserver.net";


    }

    public class Client
    {
        ConnectionSettings connectionSettings;
        Socket socket;
        byte[] incomingBuffer = new byte[1024];

        public Action OnConnectedToMaster;
        public Action<Exception> OnConnectionError;
        public Action OnNetworkMessageReceived;


        public void StartClient(ConnectionSettings settings)
        {
            connectionSettings = settings;
            socket = new Socket(AddressFamily.InterNetwork, System.Net.Sockets.SocketType.Stream, settings.protocol);
            ConnectToMasterServer();
        }

        public void ConnectToMasterServer()
        {
            try
            {
                socket.Connect(new IPEndPoint(IPAddress.Parse(StratusCloudNetwork.masterServer), connectionSettings.port));
                OnConnectedToMaster();
            }
            catch(Exception e)
            {
                OnConnectionError(e);
                Console.Out.WriteLine(e);
            }
        }

        private void ReceiveData(IAsyncResult ar)
        {
            Socket client = (Socket)ar.AsyncState;
            int count = socket.EndReceive(ar);
            byte[] dataBuffer = new byte[count];
            Array.Copy(incomingBuffer, dataBuffer, count);

            //Parse Network Message here
            OnNetworkMessageReceived();
        }
    }

    public class Server
    {
        ConnectionSettings connectionSettings;
        Socket socket;
        List<Socket> connectedClients = new List<Socket>();
        byte[] incomingBuffer = new byte[1024];

        public void StartServer(ConnectionSettings settings)
        {
            connectionSettings = settings;
            socket = new Socket(AddressFamily.InterNetwork, System.Net.Sockets.SocketType.Stream, settings.protocol);
            socket.Bind(new IPEndPoint(IPAddress.Any, settings.port));
            socket.Listen(1000);
            socket.BeginAccept(new AsyncCallback(ConnectionAccept), null);
        }

        private void ConnectionAccept(IAsyncResult ar)
        {
            Socket client = socket.EndAccept(ar);
            connectedClients.Add(client);
            client.BeginReceive(incomingBuffer, 0, incomingBuffer.Length, SocketFlags.None, new AsyncCallback(ReceiveData), client);
        }

        private void ReceiveData(IAsyncResult ar)
        {
            Socket client = (Socket)ar.AsyncState;
            int count = socket.EndReceive(ar);
            byte[] dataBuffer = new byte[count];
            Array.Copy(incomingBuffer, dataBuffer,count);

            //Parse Network Message here

        }
    }

    public class NetworkMessage
    {
        public byte eventCode;
    }

    public enum NetworkEventType
    {

    }

    public struct ConnectionSettings
    {
        public SocketType socketType;
        public ProtocolType protocol;
        public int port;
    }

    public enum SocketType
    {
        Default,
        WebSocket
    }

}
