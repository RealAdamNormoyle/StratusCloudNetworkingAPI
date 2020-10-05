using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace StratusCloudNetworking
{
    [System.Serializable]
    public class ClientState
    {
        public int clientUID;
        public string time;
        public List<string> objectsJsonData = new List<string>();

    }

    [System.Serializable]
    public class ClientInfo
    {
        public int clientID;
        public string appUID;
        public int appVersion;
        public string nickName;
    }

    [Serializable]
    public class Connection
    {
        public Socket socket;
        public IPAddress address { get { return ((IPEndPoint)(socket.RemoteEndPoint)).Address; } }
        public string ip;
        public string uid;
        public bool isServer;
        public int totalPlayers;
        public ServerState serverState;
        public DateTime lastActive;
        public List<byte> totalBuffer = new List<byte>();
        public byte[] incomingBuffer = new byte[1024];
        public byte[] outgoingBuffer = new byte[1024];
        public int bufferSize;
        public int maxPlayers;
        public ServerReference serverReference;
        public string room;
    }

    [Serializable]
    public class NetworkMessage
    {
        public string UID;
        public int eventID;
        public string data;

        public T GetDataProperty<T>(string property,PropType type)
        {
            switch (type)
            {
                case PropType.String:
                    return (T)(object)SimpleJSON.JSON.Parse(data)[property].ToString();
                case PropType.Int:
                    return (T)(object)SimpleJSON.JSON.Parse(data)[property].AsInt;
                case PropType.Float:
                    return (T)(object)SimpleJSON.JSON.Parse(data)[property].AsFloat;
                case PropType.Bool:
                    return (T)(object)SimpleJSON.JSON.Parse(data)[property].AsBool;
                case PropType.Object:
                    return (T)(object)JsonConvert.DeserializeObject<T>(SimpleJSON.JSON.Parse(data)[property].ToString());
                    
            }

            return default(T);
        }

        public void SetData(object obj)
        {
            data = JsonConvert.SerializeObject(obj);
        }

        public enum PropType
        {
            Bool,
            String,
            Int,
            Float,
            Object
        }
    }

    [Serializable]
    public enum ServerState
    {
        Waiting,
        Playing
    }

    [Serializable]
    public enum NetworkEvent
    {
        ServerRegister,
        ServerHeartbeat,
        ClientRegister,
        ClientMatchRequest,
        MasterMatchResponse,
        ServerStateUpdate,
        ClientStateUpdate,
        MasterAck,
        ClientAck,
        ServerAck,
        CustomMessage,
        ObjectSpawn,
        GameStateUpdate,
        PlayerSpawn,
        GameStart
    }
}
