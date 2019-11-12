using System;
using System.IO;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;

namespace StratusCloudNetworking
{
    public static class StratusCloudNetwork
    {
        public const string masterServer = "masterserver.net";


    }

    public class Client
    {
        ClientSettings clientSettings;
        ConnectionSettings connectionSettings;
        Socket socket;
        byte[] incomingBuffer = new byte[1024];
        byte[] sendBuffer = new byte[1024];

        public Action OnConnectedToMaster;
        public Action<Exception> OnConnectionError;
        public Action<NetworkMessage> OnNetworkMessageReceived;

        BinaryFormatter binaryFormatter = new BinaryFormatter();
        Stream bufferStream;

        public void StartClient(ConnectionSettings _connectionSettings, ClientSettings _clientSettings)
        {
            connectionSettings = _connectionSettings;
            clientSettings = _clientSettings;
            socket = new Socket(AddressFamily.InterNetwork, System.Net.Sockets.SocketType.Stream, _connectionSettings.protocol);
            ConnectToMasterServer();
        }

        public void ConnectToMasterServer()
        {
            try
            {
                socket.Connect(new IPEndPoint(IPAddress.Parse(StratusCloudNetwork.masterServer), connectionSettings.port));
            }
            catch (Exception e)
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
            bufferStream = new MemoryStream(dataBuffer);
            //Parse Network Message here
            NetworkMessage message = (NetworkMessage)binaryFormatter.Deserialize(bufferStream);
            bufferStream.Close();

            switch ((NetworkEventType)message.eventCode)
            {
                case NetworkEventType.ServerConnectionData:
                    OnConnectedToMaster();
                    SendClientInfoToServer();
                    break;
                case NetworkEventType.CreateRoomResponse:

                    break;
            }



            OnNetworkMessageReceived(message);
        }

        void SendClientInfoToServer()
        {
            ClientInfo info = new ClientInfo();
            info.appUID = clientSettings.appUID;
            info.appVersion = clientSettings.appVersion;
            info.nickName = clientSettings.nickName;

            byte[] buffer = new byte[1024];
            Stream stream = new MemoryStream();
            binaryFormatter.Serialize(stream, info);
            stream.Read(buffer, 0, (int)(stream.Length));
            stream.Close();

            SendNetworkMessage(new NetworkMessage() { eventCode = (byte)NetworkEventType.ClientConnectionData, sendOption = (byte)SendOptions.Server ,data = buffer});
        }

        void SendNetworkMessage(NetworkMessage message)
        {
            try
            {
                binaryFormatter.Serialize(bufferStream, message);
                bufferStream.Read(sendBuffer, 0, (int)(bufferStream.Length));
                socket.Send(sendBuffer);
                bufferStream.Close();

            } catch (Exception e)
            {
                OnConnectionError(e);
            }
        }

    }

    public class Server
    {
        ConnectionSettings connectionSettings;
        Socket socket;
        byte[] incomingBuffer = new byte[1024];
        byte[] sendBuffer = new byte[1024];

        BinaryFormatter binaryFormatter = new BinaryFormatter();
        Stream bufferStream;

        List<ClientConnection> clientConnections = new List<ClientConnection>();

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
            clientConnections.Add(new ClientConnection() { ID = clientConnections.Count.ToString(), socket = client });
            client.BeginReceive(incomingBuffer, 0, incomingBuffer.Length, SocketFlags.None, new AsyncCallback(ReceiveData), client);
        }

        private void ReceiveData(IAsyncResult ar)
        {
            Socket client = (Socket)ar.AsyncState;
            int count = socket.EndReceive(ar);
            byte[] dataBuffer = new byte[count];
            Array.Copy(incomingBuffer, dataBuffer, count);

            bufferStream = new MemoryStream(dataBuffer);
            //Parse Network Message here
            NetworkMessage message = (NetworkMessage)binaryFormatter.Deserialize(bufferStream);
            bufferStream.Close();
            //Parse Network Message here
            switch ((NetworkEventType)message.eventCode)
            {
                case NetworkEventType.ClientConnectionData:
                    Stream stream = new MemoryStream(message.data);
                    ClientInfo data = (ClientInfo)binaryFormatter.Deserialize(stream);

                    var connection = GetConnectionFromSocket(client);
                    if(connection.socket != null)
                    {
                        connection.appVersion = data.appVersion;
                        connection.appID = data.appUID;
                        connection.nickName = data.nickName;
                    }

                    break;
                case NetworkEventType.CreateRoomRequest:

                    break;
                default:
                    var c = GetConnectionFromSocket(client);
                    if (c.socket != null)
                    {

                        SendNetworkMessage(message, c);
                    }
                    break;
            }
        }

        void SendNetworkMessage(NetworkMessage message, ClientConnection sendingClient)
        {
            try
            {
                binaryFormatter.Serialize(bufferStream, message);
                bufferStream.Read(sendBuffer, 0, (int)(bufferStream.Length));
                socket.Send(sendBuffer);
                bufferStream.Close();
                 
            }
            catch (Exception e)
            {

            }
        }

        ClientConnection GetConnectionFromSocket(Socket socket)
        {
            foreach (var item in clientConnections)
            {
                if (item.socket.RemoteEndPoint == socket.RemoteEndPoint)
                {
                    return item;
                }
            }

            return new ClientConnection();
        }

    }

    public class AppSpace
    {
        public string appUID;
        public int maxRooms;
        public int maxClients;
        public List<Room> rooms = new List<Room>();
    }

    [System.Serializable]
    public class NetworkMessage
    {
        public byte eventCode;
        public byte sendOption;
        public byte[] data;
    }

    [System.Serializable]
    public class RoomSettings
    {
        public int maxClients;
        public string level;
        public string gameMode;
    }

    [System.Serializable]
    public class ClientInfo{

        public string appUID;
        public int appVersion;
        public string nickName;

    }

    public enum NetworkEventType
    {
        ServerConnectionData,
        ClientConnectionData,
        RoomListRequest,
        RoomListResponse,
        CreateRoomRequest,
        CreateRoomResponse,
        JoinRoomRequest,
        JoinRoomResponse
    }

    public enum SendOptions
    {
        Server,
        All
    }

    public struct ClientSettings
    {
        public string appUID;
        public int appVersion;
        public string nickName;

    }

    public struct ClientConnection
    {
        public string ID;
        public string currentRoom;
        public Socket socket;
        public int appVersion;
        public string nickName;
        public string appID;

    }

    public struct ConnectionSettings
    {
        public SocketType socketType;
        public ProtocolType protocol;
        public int port;
    }

    public struct Room
    {
        public List<ClientConnection> clients;
        public int maxClients;
        public string level;
        public string gameMode;
    }

    public enum SocketType
    {
        Default,
        WebSocket
    }

}
