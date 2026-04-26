using MiddleManClient.MethodProcessing.MethodDiscovery.Attributes;
using MiddleManClient.ServerContracts;

namespace VirtualNetwork.VirtualAdapter
{
  public interface IVirtualNetworkAdapter
  {
    public Task Start();

    [MiddleManMethod]
    public Task Receive1(byte[] packet);

    [MiddleManMethod]
    public Task Receive2(byte[] packet);

    [MiddleManMethod]
    public Task Receive3(byte[] packet);

    [MiddleManMethod]
    public Task Receive4(byte[] packet);

    [MiddleManMethod]
    public Task Receive5(byte[] packet);

    [MiddleManMethod]
    public Task Receive6(byte[] packet);

    [MiddleManMethod]
    public Task Receive7(byte[] packet);

    [MiddleManMethod]
    public Task Receive8(byte[] packet);

    [MiddleManMethod]
    public Task Receive9(byte[] packet);

    [MiddleManMethod]
    public Task Receive10(byte[] packet);

    [MiddleManMethod]
    public Task Receive11(byte[] packet);

    [MiddleManMethod]
    public Task Receive12(byte[] packet);
  }
}