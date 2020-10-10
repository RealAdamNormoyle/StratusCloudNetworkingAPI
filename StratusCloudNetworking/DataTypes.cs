using Newtonsoft.Json;
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


    public class StateObject
    {
        public ManualResetEvent sendDone = new ManualResetEvent(false);
        public Connection connection;
        public byte[] buffer = new byte[1024];

    }

    [System.Serializable]
    public class MessagePacket
    {
        public int messageID;
        public int packetID;
        public int dataSize;
        public byte packetType;
        public byte[] packetData;

        public void Parse(byte[] data)
        {
            messageID = BitConverter.ToInt32(data, 0);
            packetID = BitConverter.ToInt32(data, 4);
            dataSize = BitConverter.ToInt32(data, 8);
            packetType = data[12];
            var l = new List<byte>(data);
            packetData = l.GetRange(13,dataSize).ToArray();
        }

        public byte[] Serialize()
        {
            BinaryFormatter bf = new BinaryFormatter();
            MemoryStream st = new MemoryStream();
            bf.Serialize(st, this);
            return st.GetBuffer();
        }

        public class Factory
        {
            public static int messageCount;

            public static List<MessagePacket> PacketsFromMessage (NetworkMessage m)
            {
                var messageID = messageCount++;
                List<MessagePacket> packets = new List<MessagePacket>();
                BinaryFormatter bf = new BinaryFormatter();
                MemoryStream st = new MemoryStream();
                bf.Serialize(st, m);
                var buffer = new List<byte>(st.GetBuffer());         
                var n = buffer.Count / 1000;

                for (int i = 0; i <= n; i++)
                {
                    var index = (1000 * i);
                    var dataCount = (Math.Min(1000, buffer.Count - index));
                    MessagePacket p = new MessagePacket();
                    p.messageID = messageID;
                    p.packetID = i;
                    p.packetType = 0;
                    p.packetData = buffer.GetRange(index, dataCount).ToArray();
                    packets.Add(p);
                }

                return packets;
            }

            public static NetworkMessage MessageFromPackets(List<MessagePacket> packets)
            {
                NetworkMessage m = new NetworkMessage();
                List<byte> data = new List<byte>();
                foreach (var item in packets)
                {
                    data.AddRange(item.packetData);
                }

                BinaryFormatter bf = new BinaryFormatter();
                m = bf.Deserialize(new MemoryStream(data.ToArray())) as NetworkMessage;

                return m;
            }
            
        }
    }



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
        public Socket udp_socket;
        public Dictionary<int, List<MessagePacket>> messageBuffers = new Dictionary<int, List<MessagePacket>>();
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
        public bool isHostedRoom;
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
        GameStart,
        StartHostedRoom,
        HostedRoomCreated,
        ConnectToHostedRoom
    }
}
