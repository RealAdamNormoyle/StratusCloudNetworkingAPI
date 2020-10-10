using System;
using System.Net;
using StratusCloudNetworking;

namespace TransportLayerTesting
{
    class Program
    {
        public static TransportLayer TransportLayer = new TransportLayer();

        static void Main(string[] args)
        {
            TransportLayer.onConnectedToRemote += OnConnectedToRemote;
            TransportLayer.onReceivedMessage += OnReceivedMessage;
            TransportLayer.onSentTCPToRemote += OnSentTCPToRemote;
            TransportLayer.onSentUDPToRemote += OnSentUDPToRemote;

            TransportLayer.Initialize();
            TransportLayer.SendTCP(new NetworkMessage(), new IPEndPoint(IPAddress.Parse("52.17.186.16"), 2728));
            TransportLayer.SendUDP(new NetworkMessage(), new IPEndPoint(IPAddress.Parse("52.17.186.16"), 2729));
            TransportLayer.SendUDP(new NetworkMessage(), new IPEndPoint(IPAddress.Parse("52.17.186.16"), 2729));

        }

        private static void OnSentUDPToRemote(IPEndPoint endPoint)
        {
            throw new NotImplementedException();
        }

        private static void OnSentTCPToRemote(IPEndPoint endPoint)
        {
            throw new NotImplementedException();
        }

        private static void OnReceivedMessage(IPEndPoint endPoint, MessageWrapper message)
        {
            throw new NotImplementedException();
        }

        private static void OnConnectedToRemote(IPEndPoint endPoint)
        {
            throw new NotImplementedException();
        }
    }
}
