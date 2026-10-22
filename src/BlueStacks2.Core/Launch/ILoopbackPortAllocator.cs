using System.Net;
using System.Net.Sockets;

namespace BlueStacks2.Core.Launch;

public interface ILoopbackPortAllocator
{
    int Allocate();
}

public sealed class TcpLoopbackPortAllocator : ILoopbackPortAllocator
{
    public int Allocate()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }
}
