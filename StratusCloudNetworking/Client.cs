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
        public string masterIP = "52.17.186.16";

        string m_uid;
        bool m_connectedToMaster;
        ClientConnection m_masterConnection;
        ClientConnection m_serverConnection;

        byte[] m_incomingBuffer;
        public Timer stateUpdateTimer;
        public GameObject localPlayerPrefab;
        public GameObject remotePlayerPrefab;

        // ManualResetEvent instances signal completion.  
        private ManualResetEvent connectDone = new ManualResetEvent(false);
        private ManualResetEvent sendDone = new ManualResetEvent(false);
        private ManualResetEvent receiveDone = new ManualResetEvent(false);
        public ManualResetEvent messageParsed = new ManualResetEvent(false);


        //[Serializable] public class OnConnectedToMasterEvent : Action { }
        public Action onConnectedToMaster;

        public Action onConnectedToServer;

        public Action<string,string> onGameStart;


        public Action<NetworkMessage> onReceivedMessage;

        public Action onDisconnect;

        public List<Action> pending = new List<Action>();

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

        public void ConnectToMaster()
        {
            Debug.Log("Attempting to connect to master server");
            //connectDone.Reset();
            m_masterConnection = new ClientConnection();

            IPHostEntry ipHostInfo = Dns.GetHostEntry(masterIP);
            IPAddress ipAddress = ipHostInfo.AddressList[0];
            IPEndPoint remoteEP = new IPEndPoint(ipAddress, 2727);
            m_masterConnection.socket = new Socket(ipAddress.AddressFamily, System.Net.Sockets.SocketType.Stream, ProtocolType.Tcp);

            // Connect to the remote endpoint.  
            m_masterConnection.socket.BeginConnect(remoteEP,
                new AsyncCallback(MasterConnectCallback), m_masterConnection);
            //connectDone.WaitOne();
        }

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
            SendMessage(m_masterConnection, msg);
        }

        private void MasterConnectCallback(IAsyncResult ar)
        {
            connectDone.Set();

            try
            {


                // Complete the connection.  
                m_masterConnection.socket.EndConnect(ar);
                m_connectedToMaster = true;
                Debug.Log("Connected to master server");
                // Signal that the connection has been made.  
                //NetworkMessage msg = new NetworkMessage();
                //msg.eventID = (int)NetworkEvent.ClientRegister;
                //msg.SetData(new { uid = m_uid });
                //msg.UID = m_uid;
                //SendMessage(m_masterConnection, msg);

                if (onConnectedToMaster != null)
                    onConnectedToMaster.Invoke();
            }
            catch (Exception e)
            {
                m_connectedToMaster = false;
                Debug.Log($"There was an error connecting to the master server : {e}");
            }


        }

        private void ConnectToServer(string ip)
        {
            Debug.Log($"connecting to server : {ip}");

            //IPHostEntry ipHostInfo = Dns.GetHostEntry(ip);
            //IPAddress ipAddress = ipHostInfo.AddressList[0];
            IPEndPoint remoteEP = new IPEndPoint(IPAddress.Parse(ip), 2728);
            m_serverConnection = new ClientConnection();
            m_serverConnection.socket = new Socket(remoteEP.Address.AddressFamily, System.Net.Sockets.SocketType.Stream, ProtocolType.Tcp);

            // Connect to the remote endpoint.  
            m_serverConnection.socket.BeginConnect(remoteEP,new AsyncCallback(ServerConnectCallback), m_serverConnection);

        }

        private void ServerConnectCallback(IAsyncResult ar)
        {

            try
            {
                // Complete the connection.  
                m_serverConnection.socket.EndConnect(ar);

                Debug.Log($"connected to server");
                NetworkMessage msg = new NetworkMessage();
                msg.eventID = (int)NetworkEvent.ClientRegister;
                msg.SetData(new { uid = m_uid });
                msg.UID = m_uid;
                SendMessage(m_serverConnection, msg);

                if (onConnectedToServer != null)
                    onConnectedToServer.Invoke();
            }
            catch (Exception e)
            {
                Debug.Log(e);

            }


        }

        private void SendMessage(ClientConnection conn, NetworkMessage msg)
        {
            conn.lastSentMessage = msg;
            BinaryFormatter bf = new BinaryFormatter();
            MemoryStream st = new MemoryStream();
            bf.Serialize(st, msg);
            byte[] buffer = st.GetBuffer();

            byte[] byteDataLength = BitConverter.GetBytes(buffer.Length);
            conn.outgoingBuffer = buffer;
            conn.socket.BeginSend(byteDataLength, 0, byteDataLength.Length, 0, new AsyncCallback(OnSendMessageHeader), conn);
        }

        void OnSendMessageHeader(IAsyncResult ar)
        {
            var conn = ((ClientConnection)ar.AsyncState);
            conn.socket.EndSend(ar);
            Debug.Log($"send { conn.outgoingBuffer.Length} bytes");
            conn.socket.BeginSend(conn.outgoingBuffer, 0, conn.outgoingBuffer.Length, 0, new AsyncCallback(SendCallback), conn);

        }

        private void SendCallback(IAsyncResult ar)
        {
            sendDone.Set();
            try
            {
                // Retrieve the socket from the state object.
                var state = ((ClientConnection)ar.AsyncState);
                // Complete sending the data to the remote device.  
                int bytesSent = state.socket.EndSend(ar);
                state.bufferSize = 0;
                state.incomingBuffer = new byte[1024];
                Debug.Log($"Waiting for message from {state.ip}");

                state.socket.BeginReceive(state.incomingBuffer, 0, 1024, 0, new AsyncCallback(ReadCallback), state);

            }
            catch (Exception e)
            {
                Debug.Log(e);
            }
        }

        private void ReadCallback(IAsyncResult ar)
        {
            ClientConnection conn = (ClientConnection)ar.AsyncState;
            Socket handler = conn.socket;

            try
            {
                int bytesRead = handler.EndReceive(ar);

                if (bytesRead > 0)
                {
                    Debug.Log($"incoming {bytesRead} bytes");
                    if (bytesRead == 4)
                    {
                        //This is a message header
                        var foo = new List<byte>(conn.incomingBuffer);
                        var bar = foo.GetRange(0, bytesRead).ToArray();
                        conn.bufferSize = BitConverter.ToInt32(bar, 0);
                        Debug.Log($"Incoming Message : {conn.bufferSize} bytes");
                        conn.incomingBuffer = new byte[1024];
                        conn.totalBuffer.Clear();
                        Debug.Log($"Waiting for message from {conn.ip}");
                        handler.BeginReceive(conn.incomingBuffer, 0, 1024, 0, new AsyncCallback(ReadCallback), conn);

                    }
                    else if (conn.bufferSize > 0)
                    {
                        var foo = new List<byte>(conn.incomingBuffer);
                        foo.RemoveRange(bytesRead, conn.incomingBuffer.Length - bytesRead);
                        conn.totalBuffer.AddRange(foo);
                        Debug.Log($" message size {conn.totalBuffer.Count} / {conn.bufferSize}");

                        if (conn.totalBuffer.Count >= conn.bufferSize)
                        {
                            messageParsed.Reset();
                            BinaryFormatter bf = new BinaryFormatter();
                            NetworkMessage message = bf.Deserialize(new MemoryStream(conn.totalBuffer.ToArray())) as NetworkMessage;
                            Debug.Log($"Got message {message.eventID} from {conn.ip}");


                            ParseNetworkMessage(message, conn);
                            messageParsed.WaitOne();

                            conn.incomingBuffer = new byte[1024];
                            conn.bufferSize = 0;
                            conn.totalBuffer.Clear();
                            Debug.Log($"Waiting for message from {conn.ip}");
                            handler.BeginReceive(conn.incomingBuffer, 0, 1024, 0, new AsyncCallback(ReadCallback), conn);

                        }
                        else
                        {
                            conn.incomingBuffer = new byte[1024];
                            Debug.Log($"Waiting for message from {conn.ip}");

                            conn.socket.BeginReceive(conn.incomingBuffer, 0, 1024, 0, new AsyncCallback(ReadCallback), conn);
                        }

                    }
                }

            }
            catch (Exception e)
            {
                Debug.Log(e);
            }
        }

        private void ParseNetworkMessage(NetworkMessage message, ClientConnection conn)
        {
            if (onReceivedMessage != null)
                onReceivedMessage.Invoke(message);

            switch ((NetworkEvent)message.eventID)
            {
                case NetworkEvent.MasterMatchResponse:
                    ConnectToServer(message.GetDataProperty<string>("ip", NetworkMessage.PropType.String).Replace("\\", "").Replace("\"", ""));
                    break;
                case NetworkEvent.ServerAck:
                    RemoteGameStateUpdate(message.data);
                    break;
                case NetworkEvent.ObjectSpawn:
                    string name = message.GetDataProperty<string>("name", NetworkMessage.PropType.String);
                    string id = message.GetDataProperty<string>("uid", NetworkMessage.PropType.String);
                    Instance.Invoke(()=>{SpawnRemoteObject(name, id); });
                    break;
                case NetworkEvent.GameStateUpdate:
                    RemoteGameStateUpdate(message.data);
                    break;
                case NetworkEvent.GameStart:
                    stateUpdateTimer = new Timer(OnStateUpdateTimer, null, 200, 200);         
                    Instance.Invoke(() => { onGameStart.Invoke(message.GetDataProperty<string>("level", NetworkMessage.PropType.String), message.GetDataProperty<string>("mode", NetworkMessage.PropType.String)); });
                    break;
            }

            messageParsed.Set();

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
                    s.Add(item.Key, item.Value.GetData());
                }
            }

            msg.SetData(new { uid = m_uid, states = s});
            msg.UID = m_uid;
            SendMessage(m_masterConnection, msg);
        }

        void RemoteGameStateUpdate(string data)
        {
            var states = SimpleJSON.JSON.Parse(data)["states"].AsArray;
            if (states == null)
                return;

            foreach (var item in states)
            {
                if (spawnedObjects.ContainsKey(item.Key))
                {
                    spawnedObjects[item.Key].SetData(item.Value);
                }
                else
                {
                    Instance.Invoke(() =>
                    {
                        SpawnRemoteObject(item.Value["name"], item.Value["uid"]);
                    });
                        spawnedObjects[item.Key].SetData(item.Value);

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
                var d = SimpleJSON.JSON.Parse(item);
                if (!spawnedObjects[d["uid"]].isLocalObject)
                {
                    spawnedObjects[d["uid"]].SetData(item);
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

        public GameObject SpawnLocalPlayer()
        {
            var gobj = Instantiate(localPlayerPrefab);
            var ngobj = gobj.GetComponent<NetworkObject>();
            if (ngobj == null)
                ngobj = gobj.AddComponent<NetworkObject>();

            string objUid = ngobj.CreateLocalObject("PLAYER");
            spawnedObjects.Add(objUid, ngobj);

            var msg = new NetworkMessage();
            msg.UID = m_uid;
            msg.eventID = (int)NetworkEvent.ObjectSpawn;
            msg.SetData(new { name = "PLAYER", uid = objUid });
            sendDone.Reset();
            SendMessage(m_serverConnection, msg);
            sendDone.WaitOne();

            return gobj;
        }


        public GameObject SpawnNetworkObject(string obj)
        {
            if (!registeredPrefabs.ContainsKey(name))
            {
                Debug.Log($"Object {name} not registered");
                return null;
            }

            var gobj = Instantiate(registeredPrefabs[obj]);
            var ngobj = gobj.GetComponent<NetworkObject>();
            if (ngobj == null)
                ngobj = gobj.AddComponent<NetworkObject>();

            string objUid = ngobj.CreateLocalObject(obj);
            spawnedObjects.Add(objUid, ngobj);

            var msg = new NetworkMessage();
            msg.UID = m_uid;
            msg.eventID = (int)NetworkEvent.ObjectSpawn;
            msg.SetData(new {name = obj,uid = objUid });
            SendMessage(m_serverConnection, msg);
            return gobj;
        }

        #endregion
    }
}
