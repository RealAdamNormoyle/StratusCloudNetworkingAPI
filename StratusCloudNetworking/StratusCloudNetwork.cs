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
