using System;
using StratusCloudNetworking;

class Program
{
    public static Server server;

    static void Main(string[] args)
    {
        server = new Server();
        server.logCallback = Logcallbacks;
        server.StartServer(new ConnectionSettings() { port = 3434, protocol = System.Net.Sockets.ProtocolType.Tcp, socketType = SocketType.Default });

        while (true)
        {
            System.Threading.Thread.Sleep(100);
        }

    }

    public static void Logcallbacks(string log)
    {
        Console.Out.WriteLine(log);
    }
}

