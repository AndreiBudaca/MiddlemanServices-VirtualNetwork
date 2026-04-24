using MiddleManClient.ServerContracts;

namespace VirtualNetwork.VirtualAdapter
{
  public class LinuxNetworkAdapter : IVirtualNetworkAdapter
  {
    public Task Receive(ServerContext context, IAsyncEnumerable<byte[]> dataStream)
    {
      throw new NotImplementedException();
    }

    public Task Start()
    {
      throw new NotImplementedException();
    }
  }
}