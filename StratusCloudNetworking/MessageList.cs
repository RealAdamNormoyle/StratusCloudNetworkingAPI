using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;

namespace StratusCloudNetworking
{
    public class MessageList
    {
        List<MessageWrapper> list = new List<MessageWrapper>();
        public bool isBusy;

        public int AddMessageToQue(NetworkMessage msg, string uid)
        {
            MessageWrapper m = new MessageWrapper(msg);
            m.recipient = uid;
            list.Add(m);
            return list.Count;
        }

        public MessageWrapper GetNextMessage()
        {
            if (list.Count > 0)
            {
                var l = list[0];
                list.RemoveAt(0);
                return l;
            }

            return null;
        }
    }


    public class MessageWrapper
    {
        public string recipient;
        public Connection client;
        public NetworkMessage message;
        public byte[] buffer = new byte[1024];
        public byte[] totalBuffer = new byte[0];
        public byte[] sizeBytes = new byte[0];
        public int bufferSize;

        public MessageWrapper(NetworkMessage m)
        {
            message = m;
            BinaryFormatter bf = new BinaryFormatter();
            MemoryStream st = new MemoryStream();
            bf.Serialize(st, m);
            buffer = st.GetBuffer();
            sizeBytes = BitConverter.GetBytes(buffer.Length);
            bufferSize = buffer.Length;
        }

        public MessageWrapper(byte[] size) 
        {
            sizeBytes = size;
            bufferSize = BitConverter.ToInt32(size,0);
        }

        public MessageWrapper() { }

        public void AddToBuffer(byte[] bytes,int read)
        {
            var l = new List<byte>(totalBuffer);
            var r = new List<byte>(bytes);
            l.AddRange(r.GetRange(0,read));
            totalBuffer = l.ToArray();
            if(bufferSize == totalBuffer.Length)
            {
                BinaryFormatter bf = new BinaryFormatter();
                message = bf.Deserialize(new MemoryStream(totalBuffer)) as NetworkMessage;
            }
        }

        public NetworkMessage GetMessage()
        {
            if (message == null)
            {
                try
                {
                    BinaryFormatter bf = new BinaryFormatter();
                    message = bf.Deserialize(new MemoryStream(totalBuffer)) as NetworkMessage;
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                    return message;
                }
            }

            return message;
        }
    
    }
}
