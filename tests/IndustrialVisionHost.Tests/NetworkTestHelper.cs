using System.Net;
using System.Net.Sockets;

namespace IndustrialVisionHost.Tests;

internal static class NetworkTestHelper
{
    public static int GetFreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
