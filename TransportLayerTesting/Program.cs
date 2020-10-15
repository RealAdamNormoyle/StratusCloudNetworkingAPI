using System;
using System.Net;
using StratusCloudNetworking;
using SimpleJSON;
//using Open.Nat;
using System.Threading;

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

            TransportConfig c = new TransportConfig()
            {
                gameServer = true,
                masterInPort = 2727,
                masterOutPort = 2727,
                tcpInPort = 2728,
                tcpOutPort = 2728,
                udpInPort = 2729,
                udpOutPort = 2730,
            };

            TransportLayer.Initialize(c);


            Thread.Sleep(100000);


            return;
            //TransportLayer.SendTCP(new NetworkMessage(), new IPEndPoint(IPAddress.Parse("52.17.186.16"), 2728));
            //TransportLayer.SendUDP(new NetworkMessage(), new IPEndPoint(IPAddress.Parse("52.17.186.16"), 2729));
            //TransportLayer.SendUDP(new NetworkMessage(), new IPEndPoint(IPAddress.Parse("52.17.186.16"), 2729));
            var data = "[{\"clientUID\":\"89c86f8f - d2b5 - 49d3 - 955f - c382097724f8\",\"time\":\"12 / 10 / 2020 18:53:03\",\"objectJsonData\":[{\"name\":\"PLAYER\",\"uid\":\"86c0d235 - 6e10 - 45f6 - a088 - 3cc709a8d1a1\",\"pos\":true,\"rot\":true,\"position\":{\"x\":-4.612143,\"y\":-0.7,\"z\":3.4057493},\"rotation\":{\"x\":0.0,\"y\":0.9575219,\"z\":0.0,\"w\":0.2883605}}]},{\"clientUID\":\"e6879957 - 7c9d - 4905 - 9839 - dfc1f0465151\",\"time\":\"12 / 10 / 2020 18:52:18\",\"objectJsonData\":[{\"name\":\"PLAYER\",\"uid\":\"cf263148 - 56b6 - 450d - 97fa - d5370890d310\",\"pos\":true,\"rot\":true,\"position\":{\"x\":4.052859,\"y\":-0.6999998,\"z\":4.2552834},\"rotation\":{\"x\":0.0,\"y\":0.9721801,\"z\":0.0,\"w\":-0.23423457}}]}]";
            //var data = "{ \"clientUID\":\"86d575e1-f797-4bc8-93a4-1a26979daa64\",\"time\":\"12/10/2020 14:50:04\",\"objectJsonData\":[{\"name\":\"PLAYER\",\"uid\":\"771296bc-2c61-410c-a035-0a82bbbb064c\",\"pos\":true,\"rot\":true,\"position\":{\"x\":-3.348096322755567e-12,\"y\":-0.6999998688697815,\"z\":-7.403117425726705e-14},\"rotation\":{\"x\":0.0,\"y\":0.036654032766819,\"z\":0.0,\"w\":0.9993280172348023}}]}";
            var states = SimpleJSON.JSON.Parse(data);

            if (states == null)
                return;


            foreach (var i in states.AsArray)
            {

            
                var item = i.Value;

                foreach (var objj in item["objectJsonData"].AsArray)
                {
                    var objData = objj.Value;
                    ObjectData obj = new ObjectData();
                    obj.name = objData["name"];
                    obj.pos = objData["pos"];
                    var s = objData["position"];
                    obj.position = new V3(float.Parse(s["x"]), float.Parse(s["y"]), float.Parse(s["z"]));
                    obj.rot = objData["rot"];
                    s = objData["rotation"];
                    obj.rotation = new V4(float.Parse(s["x"]), float.Parse(s["y"]), float.Parse(s["z"]), float.Parse(s["w"]));
                    obj.uid = objData["uid"];

                }
            }

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
