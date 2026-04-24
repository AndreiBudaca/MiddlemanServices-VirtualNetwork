using MiddleManClient.ServerContracts;
using VirtualNetwork.Neworking;

namespace VirtualNetwork.VirtualAdapter
{
  public class WindowsNetworkAdapter(Router router) : IVirtualNetworkAdapter
  {
    private readonly Router router = router;

    public Task Receive(ServerContext context, IAsyncEnumerable<byte[]> dataStream)
    {
      throw new NotImplementedException();
    }

    public async Task Start()
    {
      var ownIp = await router.GetOwnIpAddress();
      Console.WriteLine($"Virtual network adapter started with IP address: {ownIp}");
      
      // TO DO: use Wintun to create a virtual network adapter, and forward any received data to the router for processing, and send any data from the router to the appropriate destination through the virtual adapter
    }
  }
}