using System;
using System.Collections.Generic;
using System.Text;

namespace StratusCloudNetworking
{
    public class AppSpace
    {
        public string appUID;
        public int maxRooms;
        public int maxClients;
        public List<Room> rooms = new List<Room>();
    }

    [System.Serializable]
    public class NetworkMessage
    {
        public byte eventCode;
        public byte sendOption;
        public byte[] data;
    }

    [System.Serializable]
    public class RoomSettings
    {
        public int maxClients;
        public string level;
        public string gameMode;
    }

    [System.Serializable]
    public class ClientInfo
    {

        public int clientID;
        public string appUID;
        public int appVersion;
        public string nickName;
    }

    [System.Serializable]
    public class AppDatabase
    {
        public List<App> apps;
    }

    [System.Serializable]
    public class App
    {
        public string uid;
        public int maxClients;
        public int maxRooms;
    }
}
