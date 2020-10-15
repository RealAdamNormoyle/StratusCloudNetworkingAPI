using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using System.Threading;
using UnityEngine;

namespace StratusCloudNetworking
{
    [System.Serializable]
    public class V3
    {
        public float x;
        public float y;
        public float z;

        public V3(float X,float Y,float Z)
        {
            x = X;
            y = Y;
            z = Z;
        }

        public V3(Vector3 vector)
        {
            x = vector.x;
            y = vector.y;
            z = vector.z;
        }

        public Vector3 ToVector3()
        {
            return new Vector3(x,y,z);
        }
    }

    [System.Serializable]
    public class V4
    {
        public float x;
        public float y;
        public float z;
        public float w;

        public V4(float X, float Y, float Z, float W)
        {
            x = X;
            y = Y;
            z = Z;
            w = W;

        }

        public V4(Quaternion vector)
        {
            x = vector.x;
            y = vector.y;
            z = vector.z;
            w = vector.w;

        }

        public Quaternion ToQuaternion()
        {
            return new Quaternion(x, y, z,w);
        }
    }


    [System.Serializable]
    public class MessagePacket
    {
        public int messageID;
        public int packetID;
        public int dataSize;
        public byte packetType;
        public byte[] packetData;

        public bool Parse(byte[] data)
        {
            if (data.Length < 10)
                return false;

            messageID = BitConverter.ToInt32(data, 0);
            packetID = BitConverter.ToInt32(data, 4);
            dataSize = BitConverter.ToInt32(data, 8);
            Console.WriteLine($"Parsing Packet [{data.Length}]: {(data.Length - 13)} {dataSize}");

            packetType = data[12];
            packetData = new byte[0];
            if ((data.Length - 13) < dataSize)
                return false;

            var l = new List<byte>(data);
            packetData = l.GetRange(13,dataSize).ToArray();
            Console.WriteLine($"Parsing Packet [{data.Length}]: {messageID} {packetID} {dataSize} , {packetData.Length}");
            return true;
        }

        public byte[] Serialize()
        {
            var l = new List<byte>();
            dataSize = packetData.Length;
            l.AddRange(BitConverter.GetBytes(messageID));
            l.AddRange(BitConverter.GetBytes(packetID));
            l.AddRange(BitConverter.GetBytes(dataSize));
            l.Add(packetType);
            l.AddRange(packetData);
            Console.WriteLine($"Packing Packet [{l.Count}]: {messageID} {packetID} {dataSize} , {packetData.Length}");

            return l.ToArray();
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

                try
                {
                    if (data.Count > 0)
                    {
                        BinaryFormatter bf = new BinaryFormatter();
                        m = bf.Deserialize(new MemoryStream(data.ToArray())) as NetworkMessage;
                    }

                }
                catch (Exception)
                {

                    Console.WriteLine("FAILED parsing netwokrMessage");
                }

                return m;
            }
            
        }
    }

    [System.Serializable]
    public class ClientState
    {
        public string clientUID;
        public string time;
        public ObjectData[] objectJsonData;

        public static ClientState FromJson(string json)
        {
            //JsonMapper.ToObject<ClientState>(json);

            ClientState n = new ClientState();
            var d = SimpleJSON.JSON.Parse(json);
            n.clientUID = d["clientUID"];
            n.time = d["time"];
            List<ObjectData> datas = new List<ObjectData>();

            if (d["objectJsonData"].AsArray != null)
            {
                Console.WriteLine("HAS OBJECT DATA");

                foreach (var objj in d["objectJsonData"].AsArray)
                {
                    var objData = objj.Value;
                    Console.WriteLine(objData.Value.ToString());
                    ObjectData obj = ObjectData.FromJson(objData.AsObject);
                    datas.Add(obj);
                }
            }
            n.objectJsonData = datas.ToArray();
            return n;
        }
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
        public string uid;
        public string ip;
        public IPEndPoint endPoint;
        public Dictionary<int, List<MessagePacket>> messageBuffers = new Dictionary<int, List<MessagePacket>>();
        public DateTime lastActive;
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

        public void SetData(string obj)
        {
            data = obj;
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
