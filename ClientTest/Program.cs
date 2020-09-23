using System;
using StratusCloudNetworking;

namespace ClientTest
{
    class Program
    {

  
        private static void OnNetworkMessageRecieved(NetworkMessage obj)
        {
            
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
