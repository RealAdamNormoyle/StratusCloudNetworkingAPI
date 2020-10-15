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

        public static TransportLayer TransportLayer = new TransportLayer();

        public void OnDestroy()
        {
            TransportLayer.Dispose();
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

            TransportLayer.onConnectedToRemote += OnConnectedToRemote;
            TransportLayer.onReceivedMessage += OnReceivedMessage;
            TransportLayer.onSentTCPToRemote += OnSentTCPToRemote;
            TransportLayer.onSentUDPToRemote += OnSentUDPToRemote;
            TransportLayer.onRemoteConnected += OnRemoteConnected;
            TransportConfig c = new TransportConfig()
            {
                gameClient = true,
                masterInPort = 2727,
                masterOutPort = 2727,
                tcpInPort = 2728,
                tcpOutPort = 2728,
                udpInPort = 2729,
                udpOutPort = 2729,
            };

            TransportLayer.Initialize(c);
        }

        private void OnRemoteConnected(IPEndPoint endPoint)
        {

        }

        private  void OnSentUDPToRemote(IPEndPoint endPoint)
        {

        }

        private  void OnSentTCPToRemote(IPEndPoint endPoint)
        {

        }

        private  void OnReceivedMessage(IPEndPoint endPoint, MessageWrapper message)
        {
            Debug.Log($"Incoming Message from {endPoint} | {message.message.data}");
            ParseNetworkMessage(message.message, endPoint);
        }

        private  void OnConnectedToRemote(IPEndPoint endPoint)
        {

        }


        #region MasterServer

        public void ConnectToMaster()
        {
            Debug.Log("Attempting to connect to master server");
            Instance.m_masterConnection = new Connection();
            IPHostEntry ipHostInfo = Dns.GetHostEntry(masterIP);
            IPAddress ipAddress = ipHostInfo.AddressList[0];
            IPEndPoint remoteEP = new IPEndPoint(ipAddress, 2727);
            Instance.m_masterConnection.endPoint = remoteEP;
            TransportLayer.ConnectTo(remoteEP, OnConnectedToMaster);
        }

        private void OnConnectedToMaster()
        {
            m_connectedToMaster = true;
            Debug.Log("Connected to master server");

            if (onConnectedToMaster != null)
                Invoke(onConnectedToMaster);
        }

        public void Master_SendMessage(NetworkMessage m)
        {
            TransportLayer.SendTCP(m, Instance.m_masterConnection.endPoint);
            Debug.Log($"Finished Sending to Master");
        }

        #endregion

        #region Server

        private void ConnectToServer(string ip)
        {
            Debug.Log($"connecting to server : {ip}");
            IPEndPoint remoteEP = new IPEndPoint(IPAddress.Parse(ip), 2728);
            Instance.m_serverConnection = new Connection();
            Instance.m_serverConnection.ip = ip;
            Instance.m_serverConnection.endPoint = remoteEP;
            TransportLayer.ConnectTo(remoteEP, ServerConnectCallback);
        }
        private void ServerConnectCallback()
        {
            try
            {
                TransportLayer.StartUDPListener(m_serverConnection.ip);
                clientUpdateTimer = new Timer(OnStateUpdateTimer,null ,50, 50);
                NetworkMessage msg = new NetworkMessage();
                msg.eventID = (int)NetworkEvent.ClientRegister;
                msg.SetData(new { uid = m_uid });
                msg.UID = m_uid;
                
                Server_SendMessage(msg,false);
                if (onConnectedToServer != null)
                    Invoke(onConnectedToServer);

            }
            catch (Exception e)
            {
                Debug.Log(e);
            }
        }

        public static void Server_SendMessage(NetworkMessage msg,bool udp = true)
        {
            Debug.Log($"Server_SendMessage {msg.data}");
            msg.UID = m_uid;
            IPEndPoint remoteEP = new IPEndPoint(Instance.m_serverConnection.endPoint.Address, (udp)? TransportLayer.Config.udpOutPort: Instance.m_serverConnection.endPoint.Port);
            if(udp)
                TransportLayer.SendUDP(msg, remoteEP);
            else
                TransportLayer.SendTCP(msg, remoteEP);

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

        private void ParseNetworkMessage(NetworkMessage message,IPEndPoint endPoint)
        {
            if(message == null || message.UID == m_uid)
            {
                Debug.Log($"Ignoring Message {message.UID}");
                return;
            }

            Debug.Log($"ParseNetworkMessage {message.eventID} {message.data}");
            switch ((NetworkEvent)message.eventID)
            {
                case NetworkEvent.MasterMatchResponse:
                    ConnectToServer(message.GetDataProperty<string>("ip", NetworkMessage.PropType.String).Replace("\\", "").Replace("\"", ""));
                    break;
                case NetworkEvent.ServerAck:
                    //RemoteGameStateUpdate(message.data);
                    break;
                case NetworkEvent.ObjectSpawn:
                case NetworkEvent.PlayerSpawn:
                    string name = message.GetDataProperty<string>("name", NetworkMessage.PropType.String).Replace("\"", "");
                    string id = message.GetDataProperty<string>("uid", NetworkMessage.PropType.String).Replace("\"", "");
                    Debug.Log($"PlayerSpawn {message.data}");
                    Debug.Log("ObjectSpawn");
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

        private void OnStateUpdateTimer(object st)
        {
            Debug.Log("OnStateUpdateTimer");
            NetworkMessage msg = new NetworkMessage();
            msg.eventID = (int)NetworkEvent.ClientStateUpdate;
            ClientState state = new ClientState();
            state.clientUID = m_uid;
            state.time = DateTime.Now.ToString();
            List<ObjectData> d = new List<ObjectData>();
            foreach (var item in spawnedObjects)
            {
                if (item.Value.isLocalObject)
                {
                    var da = item.Value.GetData();
                    d.Add(da);
                }
            }
            state.objectJsonData = d.ToArray();
            var json = JsonUtility.ToJson(state);
            Debug.Log(json);

            msg.SetData(json);
            msg.UID = m_uid;
            Server_SendMessage(msg);
        }

        void ParseClientState(SimpleJSON.JSONNode item)
        {
            if (item["clientUID"] == m_uid)
                return;


            if (item["objectJsonData"] != null)
            {
                Debug.Log($"RemoteGameStateUpdate : {item["objectJsonData"]}");

                foreach (var objj in item["objectJsonData"].AsArray)
                {
                    var objData = objj.Value;
                    Debug.Log($"RemoteGameStateUpdate : {objData}");
                    ObjectData obj = ObjectData.FromJson(objData.AsObject);

                    if (spawnedObjects.ContainsKey(obj.uid))
                    {
                        spawnedObjects[obj.uid].SetData(obj);
                    }
                    else
                    {
                        Instance.Invoke(() =>
                        {
                            SpawnRemoteObject(obj.name, obj.uid)?.SetData(obj);
                        });

                    }

                }
            }
        }

        void RemoteGameStateUpdate(string data)
        {
            if (string.IsNullOrEmpty(data))
                return;
            //data = "{states :" + data + "}";
            Debug.Log($"RemoteGameStateUpdate : {data}");

            //data = data.Replace("\\", "");
            var states = SimpleJSON.JSON.Parse(data);

            if (states == null)
                return;

            if (states.IsArray)
            {
                foreach (var i in states.AsArray)
                {
                    var item = i.Value;
                    ParseClientState(item);
                }
            }
            else
            {
                ParseClientState(states);
            }
        }

        #region Objects

        public Dictionary<string, GameObject> registeredPrefabs = new Dictionary<string, GameObject>();
        public static Dictionary<string, NetworkObject> spawnedObjects = new Dictionary<string, NetworkObject>();


        public void RegisterNetworkPrefab(GameObject obj,string name)
        {
            if(registeredPrefabs.ContainsKey(name))
            {
                Debug.Log($"Object {name} allready registered");
                return;
            }

            registeredPrefabs.Add(name, obj);
        }

        public NetworkObject SpawnRemoteObject(string name, string uid)
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
                return ngobj;
            }

            return null;
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

            var msg = new NetworkMessage();
            msg.UID = m_uid;
            msg.eventID = (int)NetworkEvent.PlayerSpawn;
            msg.SetData(new { name = "PLAYER", uid = objUid });
            Server_SendMessage(msg,false);
            callback.Invoke(gobj);
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
            Server_SendMessage(msg,false);
            
        }

        #endregion
    }
}
