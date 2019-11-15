using System;
using System.IO;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using Newtonsoft.Json;

namespace StratusCloudNetworking
{
    public static class StratusCloudNetwork
    {
        public const string masterServer = "192.168.1.16";
        public const string databaseServer = "http://192.168.1.16/gameserver/masterserver.php";
        public static HttpClient httpClient = new HttpClient();
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
                socket.BeginReceive(incomingBuffer,0,1024, SocketFlags.None, ReceiveData,socket);
            }
            catch (Exception e)
            {
                OnConnectionError(e);
            }
        }

        private void ReceiveData(IAsyncResult ar)
        {
            int count = 0;
            Socket client = null;

            try
            {
                client = (Socket)ar.AsyncState;
                count = client.EndReceive(ar);
            }
            catch(SocketException e)
            {

                OnConnectionError(e);

                return;
            }


            if (count < 1)
                return;

            byte[] dataBuffer = new byte[count];
            Array.Copy(incomingBuffer, dataBuffer, count);
            bufferStream = new MemoryStream(dataBuffer);
            NetworkMessage message = (NetworkMessage)binaryFormatter.Deserialize(bufferStream);
            bufferStream.Close();

            switch ((NetworkEventType)message.eventCode)
            {
                case NetworkEventType.ClientConnectionData:
                    OnConnectedToMaster();
                    break;
                case NetworkEventType.ServerConnectionData:
                    SendClientInfoToServer();
                    break;
                case NetworkEventType.CreateRoomResponse:

                    break;
                default:
                    OnNetworkMessageReceived(message);
                    break;
            }


            Console.Out.WriteLine(string.Format("Received Data, total bytes : {0}, EndPoint : {1}, Event : {2}", count, client.RemoteEndPoint.ToString(),message.eventCode));
            client.BeginReceive(incomingBuffer,0,1024, SocketFlags.None, ReceiveData, client);

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
            buffer = ((MemoryStream)stream).ToArray();
            stream.Close();

            SendNetworkMessage(new NetworkMessage() { eventCode = (byte)NetworkEventType.ClientConnectionData, sendOption = (byte)SendOptions.Server ,data = buffer});
        }

        void SendNetworkMessage(NetworkMessage message)
        {
            try
            {
                bufferStream = new MemoryStream();
                binaryFormatter.Serialize(bufferStream, message);
                sendBuffer = ((MemoryStream)bufferStream).ToArray();
                bufferStream.Close();
                socket.BeginSend(sendBuffer, 0, sendBuffer.Length, SocketFlags.None, new AsyncCallback(OnSend), socket);

            } catch (Exception e)
            {
                OnConnectionError(e);
            }

            Console.Out.WriteLine(string.Format("Sent Data, total bytes : {0}, EndPoint : {1}, Event : {2}", sendBuffer.Length, socket.RemoteEndPoint.ToString(),message.eventCode));


        }

        private void OnSend(IAsyncResult ar)
        {
            Socket client = (Socket)ar.AsyncState;
            int count = client.EndSend(ar);
        }
    }

    public class Server
    {
        ConnectionSettings connectionSettings;
        Socket socket;
        byte[] incomingBuffer = new byte[1024];
        byte[] sendBuffer = new byte[1024];

        public Action<string> logCallback;

        BinaryFormatter binaryFormatter = new BinaryFormatter();
        Stream bufferStream;

        List<ClientConnection> clientConnections = new List<ClientConnection>();
        List<App> apps = new List<App>();

        public void StartServer(ConnectionSettings settings)
        {
            ResfreshDatabaseInformation();
            connectionSettings = settings;
            socket = new Socket(AddressFamily.InterNetwork, System.Net.Sockets.SocketType.Stream, settings.protocol);
            socket.Bind(new IPEndPoint(IPAddress.Any, settings.port));
            socket.Listen(1000);
            socket.BeginAccept(new AsyncCallback(ConnectionAccept), null);
            logCallback("Server Started");
        }

        public async void ResfreshDatabaseInformation()
        {
            var response = await StratusCloudNetwork.httpClient.GetStringAsync(StratusCloudNetwork.databaseServer);
            apps = JsonConvert.DeserializeObject<List<App>>(response);
            logCallback("App Database Updated");

        }

        private void ConnectionAccept(IAsyncResult ar)
        {
            Socket client = socket.EndAccept(ar);
            var conn = new ClientConnection() { ID = clientConnections.Count, socket = client };
            clientConnections.Add(conn);

            NetworkMessage msg = new NetworkMessage()
            {
                eventCode =
                (byte)StratusCloudNetworking.NetworkEventType.ServerConnectionData,
                sendOption = (byte)SendOptions.All,
                data = new byte[0]
            };

            socket.BeginAccept(new AsyncCallback(ConnectionAccept), null);

            SendNetworkMessage(msg, conn);

            logCallback("Client Connecting");
        }

        private void ReceiveData(IAsyncResult ar)
        {
            int count = 0;
            Socket client = null;
            try
            {
                client = (Socket)ar.AsyncState;
                count = client.EndReceive(ar);

            }
            catch (Exception e)
            {
                logCallback(e.Message);
                return;
            }


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
                    NetworkMessage msg;

                    if (CheckValidAppID(data.appUID))
                    {
                        if (connection.socket != null)
                        {
                            connection.appVersion = data.appVersion;
                            connection.appID = data.appUID;
                            connection.nickName = data.nickName;
                        }
                        stream.Close();
                        stream = new MemoryStream();
                        binaryFormatter.Serialize(stream, data);
                        var buffer = ((MemoryStream)stream).ToArray();

                         msg = new NetworkMessage()
                        {
                            eventCode = message.eventCode,
                            sendOption = message.sendOption,
                            data = buffer
                        };

                        logCallback(string.Format("Client Connected, ID: {0}, Nickname: {1}", data.clientID, data.nickName));


                    }
                    else
                    {
                         msg = new NetworkMessage()
                        {
                            eventCode = (byte)NetworkEventType.WrongAppID,
                            sendOption = message.sendOption,
                            data = null
                        };

                        clientConnections.Remove(connection);
                    }

                    SendNetworkMessage(msg, connection);
                    connection.socket.Close();
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


            Console.Out.WriteLine(string.Format("Received Data, total bytes : {0}, EndPoint : {1}, Event : {2}", count,client.RemoteEndPoint.ToString(),message.eventCode));
            client.BeginReceive(incomingBuffer, 0, 1024, SocketFlags.None, ReceiveData, client);

        }

        void SendNetworkMessage(NetworkMessage message, ClientConnection sendingClient)
        {
            try
            {
                bufferStream = new MemoryStream();
                binaryFormatter.Serialize(bufferStream, message);
                sendBuffer = ((MemoryStream)bufferStream).ToArray();
                bufferStream.Close();
                sendingClient.socket.BeginSend(sendBuffer, 0, sendBuffer.Length, SocketFlags.None, new AsyncCallback(OnSend), sendingClient.socket);
                 
            }
            catch (Exception e)
            {
                logCallback(e.Message);
            }


            logCallback(string.Format("Sent Data, total bytes : {0}, EndPoint : {1}, Event : {2}", sendBuffer.Length, sendingClient.socket.RemoteEndPoint.ToString(),message.eventCode));
            sendingClient.socket.BeginReceive(incomingBuffer, 0, incomingBuffer.Length, SocketFlags.None, new AsyncCallback(ReceiveData), sendingClient.socket);

        }

        private void OnSend(IAsyncResult ar)
        {
            Socket client = (Socket)ar.AsyncState;
            int count = client.EndSend(ar);
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

        bool CheckValidAppID(string id)
        {
            foreach (var item in apps)
            {
                if (item.uid == id)
                    return true;
            }

            return false;
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

        public int clientID;
        public string appUID;
        public int appVersion;
        public string nickName;
    }

    [System.Serializable]
    public class AppDatabase
    {
        public List<App> apps;
    }

    [System.Serializable]
    public class App
    {
        public string uid;
        public int maxClients;
        public int maxRooms;
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
        JoinRoomResponse,
        WrongAppID
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
        public int ID;
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
