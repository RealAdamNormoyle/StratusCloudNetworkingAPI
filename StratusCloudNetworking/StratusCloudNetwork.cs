using System;
using System.IO;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;

namespace StratusCloudNetworking
{
    public static class StratusCloudNetwork
    {
        public const string masterServer = "192.168.1.37";
        public const string databaseServer = "http://192.168.1.16/gameserver/masterserver.php";
        public static HttpClient httpClient = new HttpClient();
    }

    public class ClientConnection
    {
        public int uid;
        public Socket socket;
        public string nickName;
        public int bufferSize;
        public byte[] incomingBuffer = new byte[1024];
        public List<byte> totalBuffer = new List<byte>();
        public NetworkMessage lastSentMessage;
        internal object ip;
        public string room;
        internal byte[] outgoingBuffer;
    }

    [System.Serializable]
    public class ServerReference
    {
        public string uid;
        public string ip;
        public RoomReference[] rooms;
    }

    [System.Serializable]
    public class RoomReference
    {
        public string uid;
        public bool isPlaying;
        public int clients;
    }

    [System.Serializable]
    public struct ServerDiags
    {
        public float cpuLoad;
        public float networkLoad;
        public int uptime;
    }

    public class Room
    {
        public string uid = Guid.NewGuid().ToString();
        public List<Connection> clients = new List<Connection>();
        public string level;
        public bool isPlaying;
        public Dictionary<string, ClientState> clientStates = new Dictionary<string, ClientState>();

        public ClientState[] GetSateData()
        {
            var l = new List<ClientState>();
            foreach (var item in clientStates)
            {
                l.Add(item.Value);
            }
            return l.ToArray();
        }
    }

}
