using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;

namespace StratusCloudNetworking
{
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
        List<Room> rooms = new List<Room>();

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


            Console.Out.WriteLine(string.Format("Received Data, total bytes : {0}, EndPoint : {1}, Event : {2}", count, client.RemoteEndPoint.ToString(), message.eventCode));
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


            logCallback(string.Format("Sent Data, total bytes : {0}, EndPoint : {1}, Event : {2}", sendBuffer.Length, sendingClient.socket.RemoteEndPoint.ToString(), message.eventCode));
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
    
    
        void CreateNewRoom()
        {
            
        }
    }

}
