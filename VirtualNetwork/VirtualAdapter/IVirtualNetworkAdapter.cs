using MiddleManClient.ServerContracts;

namespace VirtualNetwork.VirtualAdapter
{
  public interface IVirtualNetworkAdapter
  {
    public Task Start();

    public Task Receive(ServerContext context, IAsyncEnumerable<byte[]> dataStream);
  }
}