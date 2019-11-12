using System;
using System.Net.Sockets;

namespace StratusCloudNetworking
{
    public static class StratusCloudNetwork
    {
        public const string masterServer = "masterserver.net";


    }

    public class Client
    {
        ConnectionSettings connectionSettings;
        Socket socket;

        public Client(ConnectionSettings settings)
        {
            connectionSettings = settings;
        }

    }

    public class Server
    {
        ConnectionSettings connectionSettings;
        Socket socket;

        public Server(ConnectionSettings settings)
        {
            connectionSettings = settings;
        }
    }

    public struct ConnectionSettings
    {
        public SocketType socketType;
        public ConnectionType protocol;
        public int port;
    }

    public enum SocketType
    {
        Default,
        WebSocket
    }

    public enum ConnectionType
    {
        Udp,
        Tcp
    }
}
