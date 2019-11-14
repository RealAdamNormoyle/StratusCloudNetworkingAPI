using System;

namespace ClientTest
{
    class Program
    {
        public static StratusCloudNetworking.Client client = new StratusCloudNetworking.Client();

        static void Main(string[] args)
        {
            client = new StratusCloudNetworking.Client();
            var conn = new StratusCloudNetworking.ConnectionSettings() { port = 3434, socketType = StratusCloudNetworking.SocketType.Default, protocol = System.Net.Sockets.ProtocolType.Tcp };
            var sett = new StratusCloudNetworking.ClientSettings() { appUID = "1", appVersion = 1, nickName = "ClientTest" };

            client.OnConnectionError = OnError;
            client.OnConnectedToMaster = OnConnected;

            client.StartClient(conn, sett) ;

            while (true)
            {
                System.Threading.Thread.Sleep(100);
            }
        }

        public static void OnError(Exception e)
        {
            Console.Out.WriteLine(e.Message);

        }

        public static void OnConnected()
        {
            Console.Out.WriteLine("Connected");
        }
    }
}
