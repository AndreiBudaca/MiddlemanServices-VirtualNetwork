using MiddleManClient.MethodProcessing.MethodDiscovery.Attributes;
using MiddleManClient.ServerContracts;

namespace VirtualNetwork.VirtualAdapter
{
  public interface IVirtualNetworkAdapter
  {
    public Task Start();

    [MiddleManMethod]
    public Task Receive(byte[] packet);
  }
}