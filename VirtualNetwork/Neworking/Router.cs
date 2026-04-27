using System.Net;
using System.Threading;
using MiddleManClient.MethodProcessing.MethodDiscovery.Attributes;
using MiddleManClient.ServerContracts;
using VirtualNetwork.Config;
using VirtualNetwork.Context;
using VirtualNetwork.Neworking.AddressManagement;
using VirtualNetwork.Neworking.Models;
using VirtualNetwork.VirtualAdapter;

namespace VirtualNetwork.Neworking
{
  public class Router(AppConfig config)
  {
    private const int MaxBroadcastSendConcurrency = 16;
    private readonly AppConfig config = config;
    private readonly HostConfiguration hostConfig = new(config.Network);
    private readonly SemaphoreSlim broadcastSendThrottle = new(MaxBroadcastSendConcurrency, MaxBroadcastSendConcurrency);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, long> nextPacketSequenceNumbers = new();

    private readonly ClientDetails gateway = new()
    {
      Id = config.Network.GatewayId,
      Name = config.Network.GatewayName
    };

    [MiddleManMethod]
    public string Connect(string clientId, string clientName)
    {
      if (!config.IsGateway) return string.Empty;
      if (string.IsNullOrEmpty(clientName) || string.IsNullOrEmpty(clientId)) return string.Empty;

      var clientDetails = new ClientDetails
      {
        Id = clientId,
        Name = clientName
      };

      var assignedIp = hostConfig.AssignIpAddress(clientDetails);
      return assignedIp.ToString();
    }

    public async Task<IPAddress> GetOwnIpAddress()
    {
      if (config.IsGateway)
      {
        return hostConfig.GetGatewayAddress();
      }

      var response = await ConnectionContext.Connection!.InvokeAsync<string>(gateway.Id, gateway.Name, "Connect", new ParsedDirectInvocationRequest
      {
        Data = [config.Network.Id, config.Network.Name]
      });

      if (string.IsNullOrEmpty(response.Data)) throw new Exception("Failed to get IP address from gateway");
      return IPAddress.Parse(response.Data);
    }

    [MiddleManMethod]
    public ClientDetails? GetClientDetails(string ipAddress)
    {
      if (!IPAddress.TryParse(ipAddress, out var ip))
      {
        Console.WriteLine($"Received request for client details with invalid IP address: {ipAddress}");
        return null;
      }

      return GetClientDetails(ip);
    }

    public ClientDetails? GetClientDetails(IPAddress ipAddress)
    {
      var clientDetails = hostConfig.GetClientByIp(ipAddress);
      return clientDetails;
    }

    public async Task Send(byte[] data, IPAddress destinationIp)
    {
      if (!hostConfig.IsInVirtualSubnet(destinationIp))
      {
        Console.WriteLine($"Destination IP {destinationIp} is outside of the virtual subnet. Dropping packet.");
        return;
      }

      if (hostConfig.IsBroadcastAddress(destinationIp))
      {
        return;
      }

      var clientDetails = config.IsGateway ? GetClientDetails(destinationIp) : await QueryGateway(destinationIp);

      if (clientDetails == null)
      {
        Console.WriteLine($"No client found for destination IP {destinationIp}. Dropping packet.");
        return;
      }

      await SendToClientAsync(clientDetails, data);
    }

    public bool IsInVirtualSubnet(IPAddress ipAddress) => hostConfig.IsInVirtualSubnet(ipAddress);

    public string GetAddressMask() => config.Network.AddressMask;

    public IPAddress GetGatewayAddress() => hostConfig.GetGatewayAddress();

    public IPAddress GetNetworkAddress() => hostConfig.GetNetworkAddress();

    private async Task<ClientDetails?> QueryGateway(IPAddress destinationIp)
    {
      var cachedClient = hostConfig.GetClientByIp(destinationIp);
      if (cachedClient != null) return cachedClient;

      var response = await ConnectionContext.Connection!.InvokeAsync<ClientDetails>(config.Network.GatewayId, config.Network.GatewayName, "GetClientDetails", new ParsedDirectInvocationRequest
      {
        Data = [destinationIp.ToString()],
      });

      var clientDetails = response.Data;
      if (clientDetails != null)
      {
        hostConfig.CacheClientIp(clientDetails, destinationIp);
      }
      return clientDetails;
    }

    private async Task SendToClientAsync(ClientDetails clientDetails, byte[] data)
    {
      await broadcastSendThrottle.WaitAsync();
      try
      {
        var sequenceNumber = (ulong)nextPacketSequenceNumbers.AddOrUpdate(clientDetails.Id, 1, (_, current) => checked(current + 1));
        var sequencedPacket = SequencedPacketEnvelope.Wrap(sequenceNumber, data);
        var _ = await ConnectionContext.Connection!.InvokeAsync(clientDetails.Id, clientDetails.Name, GenerateReceiveMethod(), new DirectInvocationData
        {
          Data = sequencedPacket
        });
      }
      finally
      {
        broadcastSendThrottle.Release();
      }
    }

    private static readonly string[] ReceiveMethods =
    [
      "Receive1", "Receive2", "Receive3", "Receive4", "Receive5", "Receive6",
      "Receive7", "Receive8", "Receive9", "Receive10", "Receive11", "Receive12"
    ];

    private static int LastReceiveIndex;

    private static string GenerateReceiveMethod()
    {
      var index = Interlocked.Increment(ref LastReceiveIndex);
      return ReceiveMethods[(index - 1) % ReceiveMethods.Length];
    }
  }
}