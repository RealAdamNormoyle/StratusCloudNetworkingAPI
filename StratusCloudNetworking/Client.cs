using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;
using System.Runtime;


namespace StratusCloudNetworking
{
    public class NetworkClient : MonoBehaviour
    {
        public static NetworkClient Instance;
        public static MessageList sendMessageList = new MessageList();
        public static MessageList sendUDPMessageList = new MessageList();

        public string masterIP = "52.17.186.16";

        public static string m_uid;
        public static bool m_connectedToMaster;

        public Connection m_masterConnection;
        public Connection m_serverConnection;

        public Timer clientUpdateTimer;
        public Timer messageSendLoop;
        public GameObject localPlayerPrefab;
        public GameObject remotePlayerPrefab;

        public Action onConnectedToMaster;
        public Action onConnectedToServer;
        public Action onMatchStarted;
        public Action onPlayerJoined;
        public Action<string,string> onGameStart;
        public Action<NetworkMessage> onReceivedMessage;
        public Action onDisconnect;
        public List<Action> pending = new List<Action>();
        private static ManualResetEvent udpReceived = new ManualResetEvent(false);

        public void OnDestroy()
        {
            DisconnectAll();
        }

        public void DisconnectAll()
        {
            m_serverConnection.socket.Close();
            m_serverConnection.udp_socket.Close();
            m_masterConnection.socket.Close();
            messageSendLoop.Dispose();
            clientUpdateTimer.Dispose();
        }

        public void Update()
        {
            this.InvokePending();
        }

        public void Invoke(Action fn)
        {
            lock (this.pending)
            {
                this.pending.Add(fn);
            }
        }

        private void InvokePending()
        {
            lock (this.pending)
            {
                foreach (Action action in this.pending)
                {
                    action();
                }

                this.pending.Clear();
            }
        }

        public void Start()
        {
            Instance = this;
            m_uid = Guid.NewGuid().ToString();
        }


        #region MasterServer

        public void ConnectToMaster()
        {
            Debug.Log("Attempting to connect to master server");
            Instance.m_masterConnection = new Connection();
            IPHostEntry ipHostInfo = Dns.GetHostEntry(masterIP);
            IPAddress ipAddress = ipHostInfo.AddressList[0];
            IPEndPoint remoteEP = new IPEndPoint(ipAddress, 2727);
            Instance.m_masterConnection.socket = new Socket(ipAddress.AddressFamily, System.Net.Sockets.SocketType.Stream, ProtocolType.Tcp);
            Instance.m_masterConnection.socket.BeginConnect(remoteEP,
                new AsyncCallback(MasterConnectCallback), Instance.m_masterConnection);
        }

        private void MasterConnectCallback(IAsyncResult ar)
        {
            try
            {
                // Complete the connection.  
                Instance.m_masterConnection.socket.EndConnect(ar);
                m_connectedToMaster = true;
                Debug.Log("Connected to master server");

                MessageWrapper wrapper = new MessageWrapper();
                Instance.m_masterConnection.socket.BeginReceive(wrapper.buffer, 0, 1024, 0, new AsyncCallback(Master_ReadCallback), wrapper);

                if (onConnectedToMaster != null)
                    Invoke(onConnectedToMaster);
            }
            catch (Exception e)
            {
                m_connectedToMaster = false;
                Debug.Log($"There was an error connecting to the master server : {e}");
            }
        }

        public void Master_SendMessage(NetworkMessage m)
        {
            MessageWrapper wrapper = new MessageWrapper(m);
            Instance.m_masterConnection.socket.BeginSend(wrapper.sizeBytes, 0, wrapper.sizeBytes.Length, 0, new AsyncCallback(Master_OnSendMessageHeader), wrapper);
        }

        public void Master_OnSendMessageHeader(IAsyncResult ar)
        {
            var wrapper = ((MessageWrapper)ar.AsyncState);
            Instance.m_masterConnection.socket.EndSend(ar);
            Debug.Log($"send to Master { wrapper.bufferSize} bytes");
            Instance.m_masterConnection.socket.BeginSend(wrapper.buffer, 0, wrapper.bufferSize, 0, new AsyncCallback(Master_SendCallback), wrapper);
        }

        public void Master_SendCallback(IAsyncResult ar)
        {
            var wrapper = ((MessageWrapper)ar.AsyncState);
            int bytesSent = Instance.m_masterConnection.socket.EndSend(ar);
            Debug.Log($"Finished Sending to Master {bytesSent} Bytes");
        }

        public void Master_ReadCallback(IAsyncResult ar)
        {
            MessageWrapper wrapper = (MessageWrapper)ar.AsyncState;

            try
            {
                int bytesRead = Instance.m_masterConnection.socket.EndReceive(ar);

                if (bytesRead > 0)
                {

                    if (bytesRead == 4)
                    {
                        //This is a message header
                        var foo = new List<byte>(wrapper.buffer);
                        var bar = foo.GetRange(0, bytesRead).ToArray();
                        wrapper.sizeBytes = bar;
                        wrapper.bufferSize = BitConverter.ToInt32(bar, 0);
                        wrapper.buffer = new byte[1024];
                        Instance.m_masterConnection.socket.BeginReceive(wrapper.buffer, 0, 1024, 0, new AsyncCallback(Master_ReadCallback), wrapper);

                    }
                    else
                    {
                        wrapper.AddToBuffer(wrapper.buffer, bytesRead);
                        wrapper.buffer = new byte[1024];

                        if (wrapper.totalBuffer.Length >= wrapper.bufferSize)
                        {
                            MessageWrapper w = new MessageWrapper();
                            Instance.m_masterConnection.socket.BeginReceive(w.buffer, 0, 1024, 0, new AsyncCallback(Master_ReadCallback), w);
                            ParseNetworkMessage(wrapper);
                        }
                        else
                        {
                            wrapper.buffer = new byte[1024];
                            Instance.m_masterConnection.socket.BeginReceive(wrapper.buffer, 0, 1024, 0, new AsyncCallback(Master_ReadCallback), wrapper);
                        }

                    }
                }

            }
            catch (Exception e)
            {
                Debug.Log(e);
                MessageWrapper w = new MessageWrapper();
                Instance.m_masterConnection.socket.BeginReceive(w.buffer, 0, 1024, 0, new AsyncCallback(Master_ReadCallback), w);

            }
        }

        #endregion

        #region Server

        private void ConnectToServer(string ip)
        {
            Debug.Log($"connecting to server : {ip}");
            IPEndPoint remoteEP = new IPEndPoint(IPAddress.Parse(ip), 2728);
            Instance.m_serverConnection = new Connection();
            Instance.m_serverConnection.ip = ip;
            Instance.m_serverConnection.socket = new Socket(remoteEP.Address.AddressFamily, System.Net.Sockets.SocketType.Stream, ProtocolType.Tcp);
            Instance.m_serverConnection.socket.BeginConnect(remoteEP, new AsyncCallback(ServerConnectCallback), Instance.m_serverConnection);
        }
        private void ServerConnectCallback(IAsyncResult ar)
        {
            try
            {
                Instance.m_serverConnection.socket.EndConnect(ar);

                clientUpdateTimer = new Timer(OnStateUpdateTimer, null, 200, 200);
                messageSendLoop = new Timer(OnMessageSendLoop, null, 100, 100);

                MessageWrapper wrapper = new MessageWrapper();
                Instance.m_serverConnection.socket.BeginReceive(wrapper.buffer, 0, 1024, 0, new AsyncCallback(Server_ReadCallback), wrapper);
                Debug.Log($"connected to server");
                NetworkMessage msg = new NetworkMessage();
                msg.eventID = (int)NetworkEvent.ClientRegister;
                msg.SetData(new { uid = m_uid });
                msg.UID = m_uid;
                
                Server_SendMessage(msg);
                if (onConnectedToServer != null)
                    Invoke(onConnectedToServer);

            }
            catch (Exception e)
            {
                Debug.Log(e);
            }
        }

        private void OnMessageSendLoop(object state)
        {
            if (!sendMessageList.isBusy)
            {
                var message = sendMessageList.GetNextMessage();
                if (message != null)
                {
                    SendQuedMessage(message, Instance.m_serverConnection);
                }
            }
            else
            {
                Debug.LogWarning("Message List Busy!");
            }
            
        }

        public static void Server_SendMessage(NetworkMessage msg)
        {
            msg.UID = m_uid;
            int i = sendMessageList.AddMessageToQue(msg, "SERVER");
        }

        private void Server_OnSendMessageHeader(IAsyncResult ar)
        {
            var wrapper = ((MessageWrapper)ar.AsyncState);
            Instance.m_serverConnection.socket.EndSend(ar);
            Instance.m_serverConnection.socket.BeginSend(wrapper.buffer, 0, wrapper.bufferSize, 0, new AsyncCallback(Server_SendCallback), wrapper);
        }

        private void Server_SendCallback(IAsyncResult ar)
        {
            try
            {
                var wrapper = ((MessageWrapper)ar.AsyncState);
                int bytesSent = Instance.m_serverConnection.socket.EndSend(ar);
            }
            catch (Exception e)
            {
                Debug.Log(e);
            }
            sendMessageList.isBusy = false;
        }

        private void Server_ReadCallback(IAsyncResult ar)
        {
            MessageWrapper wrapper = (MessageWrapper)ar.AsyncState;
            
            try
            {
                int bytesRead = Instance.m_serverConnection.socket.EndReceive(ar);
                if (bytesRead > 0)
                {
                    if (bytesRead == 4)
                    {
                        //This is a message header
                        var foo = new List<byte>(wrapper.buffer);
                        var bar = foo.GetRange(0, bytesRead).ToArray();
                        wrapper.sizeBytes = bar;
                        wrapper.bufferSize = BitConverter.ToInt32(bar, 0);
                        wrapper.buffer = new byte[1024];
                        Instance.m_serverConnection.socket.BeginReceive(wrapper.buffer, 0, 1024, 0, new AsyncCallback(Server_ReadCallback), wrapper);
                    }
                    else
                    {
                        wrapper.AddToBuffer(wrapper.buffer, bytesRead);
                        wrapper.buffer = new byte[1024];

                        if (wrapper.totalBuffer.Length >= wrapper.bufferSize)
                        {
                            MessageWrapper w = new MessageWrapper();
                            Instance.m_serverConnection.socket.BeginReceive(w.buffer, 0, 1024, 0, new AsyncCallback(Server_ReadCallback), w);
                            ParseNetworkMessage(wrapper);
                        }
                        else
                        {
                            wrapper.buffer = new byte[1024];
                            Instance.m_serverConnection.socket.BeginReceive(wrapper.buffer, 0, 1024, 0, new AsyncCallback(Server_ReadCallback), wrapper);
                        }
                    }
                }

            }
            catch (Exception e)
            {
                Debug.Log(e);
            }
        }

        #region UDP

        private EndPoint epFrom = new IPEndPoint(IPAddress.Any, 0);
        public void UDPListener()
        {
            try
            {
                var s = new Socket(AddressFamily.InterNetwork,SocketType.Dgram,ProtocolType.Udp);
                EndPoint tempRemoteEP = (EndPoint)epFrom;
                IPAddress hostIP = IPAddress.Parse(new System.Net.WebClient().DownloadString("https://api.ipify.org").Trim());
                IPEndPoint ep = new IPEndPoint(IPAddress.Any, 2729);
                s.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.ReuseAddress, true);         
                s.Bind(ep);

                while (true)
                {
                    udpReceived.Reset();
                    StateObject w = new StateObject();
                    w.connection = new Connection();
                    w.connection.udp_socket = s;
                    Debug.Log("Waiting For UDP");
                    w.connection.udp_socket.BeginReceiveFrom(w.buffer, 0, 1024, 0, ref tempRemoteEP, new AsyncCallback(Server_OnUDPReadCallback), w);
                    udpReceived.WaitOne();
                }
            }
            catch (Exception e)
            {
                Debug.Log(e);
            }
        }

        public void Server_OnUDPReadCallback(IAsyncResult ar)
        {
            StateObject so = (StateObject)ar.AsyncState;
            Socket s = so.connection.udp_socket;
            int read = s.EndReceiveFrom(ar, ref epFrom);
            Debug.Log("Finished Getting UDP");

            if (read > 0)
            {
                MessagePacket p = new MessagePacket();
                p.Parse(so.buffer);

                if (Instance.m_serverConnection.messageBuffers.ContainsKey(p.messageID))
                    Instance.m_serverConnection.messageBuffers.Add(p.messageID, new List<MessagePacket>());

                Instance.m_serverConnection.messageBuffers[p.messageID].Add(p);

                //End Message
                if(p.packetType == 0)
                {
                    Instance.m_serverConnection.messageBuffers[p.messageID].Sort((x, z) => { return x.packetID.CompareTo(z.packetID); });
                    List<byte> totalMessageBuffer = new List<byte>();
                    foreach (var item in Instance.m_serverConnection.messageBuffers[p.messageID])
                    {
                        totalMessageBuffer.AddRange(item.packetData);
                    }

                    NetworkMessage message = new NetworkMessage();
                    BinaryFormatter bf = new BinaryFormatter();
                    message = bf.Deserialize(new MemoryStream(totalMessageBuffer.ToArray())) as NetworkMessage;
                    MessageWrapper w = new MessageWrapper();
                    ParseNetworkMessage(w);
                }
            }
        }

        public void Server_SendUDPMessage(NetworkMessage m,Connection c)
        {
            try
            {
                Debug.Log($"Server_SendUDPMessage");

                StateObject s = new StateObject();
                s.connection = new Connection();
                Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                socket.Connect(Instance.m_serverConnection.ip, 2729);
                Debug.Log($"Server_SendUDPMessage CONNECTED");

                s.connection.udp_socket = socket;
                var packets = MessagePacket.Factory.PacketsFromMessage(m);
                for (int i = 0; i < packets.Count; i++)
                {
                    s.sendDone.Reset();
                    var buffer = packets[i].Serialize();
                    s.connection.udp_socket.BeginSend(buffer, 0, buffer.Length, SocketFlags.None, Server_SendUDPDone, s);
                    s.sendDone.WaitOne();
                }
                s.connection.udp_socket.Close();
                Debug.Log($"Server_SendUDPMessage DONE");

            }
            catch (Exception e)
            {
                Debug.Log(e);           
            }
        }

        public void Server_SendUDPDone(IAsyncResult ar)
        {
            StateObject so = (StateObject)ar.AsyncState;
            try
            {
                int bytes = so.connection.udp_socket.EndSend(ar);
                Debug.Log($"Finished Sending UDP {bytes}");
            }
            catch (Exception e)
            {
                Debug.Log(e);
            }
            so.sendDone.Set();
        }

        #endregion

        public void SendQuedMessage(MessageWrapper w, Connection c)
        {
            sendMessageList.isBusy = true;
            c.socket.BeginSend(w.sizeBytes, 0, w.sizeBytes.Length, 0, new AsyncCallback(Server_OnSendMessageHeader), w);
        }

        #endregion

        public void StartMatchMaking()
        {
            if (!m_connectedToMaster)
            {
                Debug.LogError("Cannot start matchmaking when not connected to master server");
                ConnectToMaster();
                return;
            }

            NetworkMessage msg = new NetworkMessage();
            msg.eventID = (int)(NetworkEvent.ClientMatchRequest);
            msg.UID = m_uid;
            Master_SendMessage(msg);
        }

        public void CreateHostedRoom()
        {
            if (!m_connectedToMaster)
            {
                Debug.LogError("Cannot start matchmaking when not connected to master server");
                ConnectToMaster();
                return;
            }

            NetworkMessage msg = new NetworkMessage();
            msg.eventID = (int)(NetworkEvent.StartHostedRoom);
            msg.UID = m_uid;
            Master_SendMessage(msg);
        }

        private void ParseNetworkMessage(MessageWrapper w)
        {

            var message = w.GetMessage(); 

            if(message == null)
            {
                Debug.Log("Message is null");
                return;
            }

            switch ((NetworkEvent)w.message.eventID)
            {
                case NetworkEvent.MasterMatchResponse:
                    ConnectToServer(message.GetDataProperty<string>("ip", NetworkMessage.PropType.String).Replace("\\", "").Replace("\"", ""));
                    break;
                case NetworkEvent.ServerAck:
                    //RemoteGameStateUpdate(message.data);
                    break;
                case NetworkEvent.ObjectSpawn:
                case NetworkEvent.PlayerSpawn:
                    Debug.Log("ObjectSpawn");
                    string name = message.GetDataProperty<string>("name", NetworkMessage.PropType.String);
                    string id = message.GetDataProperty<string>("uid", NetworkMessage.PropType.String);

                    Instance.Invoke(() =>
                    {
                        SpawnRemoteObject(name, id);
                    });

                    break;
                case NetworkEvent.GameStateUpdate:
                    RemoteGameStateUpdate(message.data);
                    break;
                case NetworkEvent.GameStart:
                    Debug.Log("Game Started");
                    if(onMatchStarted != null)
                        Invoke(onMatchStarted);

                    if (onGameStart != null)
                        Invoke(() => { onGameStart.Invoke(message.GetDataProperty<string>("level", NetworkMessage.PropType.String), message.GetDataProperty<string>("mode", NetworkMessage.PropType.String)); });
                    break;
                case NetworkEvent.StartHostedRoom:
                    ConnectToServer(message.GetDataProperty<string>("ip", NetworkMessage.PropType.String).Replace("\\", "").Replace("\"", ""));
                    break;
            }

        }

        private void OnStateUpdateTimer(object state)
        {
            NetworkMessage msg = new NetworkMessage();
            msg.eventID = (int)NetworkEvent.ClientStateUpdate;
            Dictionary<string, string> s = new Dictionary<string, string>();
            foreach (var item in spawnedObjects)
            {
                if (item.Value.isLocalObject)
                {
                    s.Add(item.Value.uid, item.Value.GetData());
                }
            }

            msg.SetData(new { uid = m_uid, states = s});
            msg.UID = m_uid;
            Server_SendUDPMessage(msg, Instance.m_serverConnection);
        }

        void RemoteGameStateUpdate(string data)
        {
            Debug.Log($"RemoteGameStateUpdate : {data}");
            var states = JsonMapper.ToObject<Dictionary<string, string>>(data);
            if (states == null)
                return;

            Debug.Log($"RemoteGameStateUpdate : States {states.Count}");

            foreach (var item in states)
            {

                var st = JsonMapper.ToObject<Dictionary<string, string>>(item.Value);
                Debug.Log($"RemoteGameStateUpdate : Objects {st.Count}");

                foreach (var i in st)
                {
                    var objData = JsonMapper.ToObject<ObjectData>(i.Value);
                    Debug.Log($"RemoteGameStateUpdate : Object {objData}");

                    if (spawnedObjects.ContainsKey(i.Key))
                    {
                        spawnedObjects[i.Key].SetData(objData);
                    }
                    else
                    {
                        Debug.Log($"RemoteGameStateUpdate : Spawning {i.Key}");

                        Instance.Invoke(() =>
                        {
                            SpawnRemoteObject(objData.name, objData.uid);
                            spawnedObjects[i.Key].SetData(objData);
                        });

                    }
                }
            }
        }

        #region Objects

        public Dictionary<string, GameObject> registeredPrefabs = new Dictionary<string, GameObject>();
        public Dictionary<string, NetworkObject> spawnedObjects = new Dictionary<string, NetworkObject>();

        public List<string> GetLocalObjectData()
        {
            var s = new List<string>();
            foreach (var item in spawnedObjects)
            {
                if (item.Value.isLocalObject)
                {
                    s.Add(item.Value.GetData());
                }
            }

            return s;
        }

        public void SetRemoteObjectData(List<string> s)
        {
            foreach (var item in s)
            {
                var objData = ObjectData.FromJson(item);
                if (!spawnedObjects[objData.uid].isLocalObject)
                {
                    spawnedObjects[objData.uid].SetData(objData);
                }
            }
        }

        public void RegisterNetworkPrefab(GameObject obj,string name)
        {
            if(registeredPrefabs.ContainsKey(name))
            {
                Debug.Log($"Object {name} allready registered");
                return;
            }

            registeredPrefabs.Add(name, obj);
        }

        public void SpawnRemoteObject(string name, string uid)
        {
            name = name.Replace('\"',' ');
            name = name.Trim();
            uid = uid.Replace('\"', ' ');
            uid = uid.Trim();

            if (!spawnedObjects.ContainsKey(uid))
            {
                Debug.Log($"[Network] Requested remote object spawn ({name},{uid})");
                GameObject gobj;
                if(name == "PLAYER")
                {
                     gobj = Instantiate(remotePlayerPrefab);
                }
                else
                {
                     gobj = Instantiate(registeredPrefabs[name]);
                }
                var ngobj = gobj.GetComponent<NetworkObject>();
                if (ngobj == null)
                    ngobj = gobj.AddComponent<NetworkObject>();

                ngobj.SetRemoteObject(uid,name);
                spawnedObjects.Add(uid, ngobj);
            }
        }

        public void SpawnLocalPlayer(Action<GameObject> callback)
        {

            var gobj = Instantiate(localPlayerPrefab);
            var ngobj = gobj.GetComponent<NetworkObject>();
            if (ngobj == null)
                ngobj = gobj.AddComponent<NetworkObject>();

            string objUid = ngobj.CreateLocalObject("PLAYER");
            Debug.Log($"[Network] Requested local object spawn (PLAYER,{objUid})");
            spawnedObjects.Add(objUid, ngobj);
            callback.Invoke(gobj);

            var msg = new NetworkMessage();
            msg.UID = m_uid;
            msg.eventID = (int)NetworkEvent.PlayerSpawn;
            msg.SetData(new { name = "PLAYER", uid = objUid });
            Server_SendMessage(msg);
        }

        public void SpawnNetworkObject(string obj, Action<GameObject> callback)
        {
            if (!registeredPrefabs.ContainsKey(name))
            {
                Debug.Log($"Object {name} not registered");
                return;
            }

            var gobj = Instantiate(registeredPrefabs[obj]);
            var ngobj = gobj.GetComponent<NetworkObject>();
            if (ngobj == null)
                ngobj = gobj.AddComponent<NetworkObject>();

            string objUid = ngobj.CreateLocalObject(obj);
            spawnedObjects.Add(objUid, ngobj);
            callback.Invoke(gobj);

            var msg = new NetworkMessage();
            msg.UID = m_uid;
            msg.eventID = (int)NetworkEvent.ObjectSpawn;
            msg.SetData(new {name = obj,uid = objUid });
            Server_SendMessage(msg);
            
        }

        #endregion
    }
}
