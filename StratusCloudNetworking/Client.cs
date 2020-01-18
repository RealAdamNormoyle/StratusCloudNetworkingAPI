using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;

namespace StratusCloudNetworking
{
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
                socket.BeginReceive(incomingBuffer, 0, 1024, SocketFlags.None, ReceiveData, socket);
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
            catch (SocketException e)
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


            Console.Out.WriteLine(string.Format("Received Data, total bytes : {0}, EndPoint : {1}, Event : {2}", count, client.RemoteEndPoint.ToString(), message.eventCode));
            client.BeginReceive(incomingBuffer, 0, 1024, SocketFlags.None, ReceiveData, client);

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

            SendNetworkMessage(new NetworkMessage() { eventCode = (byte)NetworkEventType.ClientConnectionData, sendOption = (byte)SendOptions.Server, data = buffer });
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

            }
            catch (Exception e)
            {
                OnConnectionError(e);
            }

            Console.Out.WriteLine(string.Format("Sent Data, total bytes : {0}, EndPoint : {1}, Event : {2}", sendBuffer.Length, socket.RemoteEndPoint.ToString(), message.eventCode));


        }

        private void OnSend(IAsyncResult ar)
        {
            Socket client = (Socket)ar.AsyncState;
            int count = client.EndSend(ar);
        }
    }


}
