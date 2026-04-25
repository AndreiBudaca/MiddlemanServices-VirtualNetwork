using System.Net;
using MiddleManClient.MethodProcessing.MethodDiscovery.Attributes;
using MiddleManClient.ServerContracts;
using VirtualNetwork.Config;
using VirtualNetwork.Neworking.AddressManagement;
using VirtualNetwork.Neworking.Models;
using VirtualNetwork.Neworking.Requests;

namespace VirtualNetwork.Neworking
{
  public class Router(AppConfig config)
  {
    private readonly AppConfig config = config;
    private readonly HostConfiguration hostConfig = new(config.Network);

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

      var gatewayUrl = GenerateClientUrl(config.MiddlemanUrl, gateway.Id, gateway.Name, "Connect");
      var response = await RequestHandler.MakeHttpRequest(gatewayUrl, config.MiddlemanJwt, config.Network.Name);

      var adressString = await RequestHandler.HandleResponse<string>(response);
      if (string.IsNullOrEmpty(adressString)) throw new Exception("Failed to get IP address from gateway");

      return IPAddress.Parse(adressString);
    }

    [MiddleManMethod]
    public ClientDetails? GetClientDetails(string ipAddress)
    {
      if (!config.IsGateway) return null;
      if (string.IsNullOrEmpty(ipAddress)) return null;

      var ip = IPAddress.Parse(ipAddress);

      var clientDetails = hostConfig.GetClientByIp(ip);
      return clientDetails;
    }

    public async Task Send(Stream data, string destinationIp, int destinationPort)
    {
      if (string.IsNullOrEmpty(destinationIp)) return;
      var destinationIpParsed = IPAddress.Parse(destinationIp);

      if (!hostConfig.IsInVirtualSubnet(destinationIpParsed))
      {
        Console.WriteLine($"Destination IP {destinationIp} is outside of the virtual subnet. Dropping packet.");
        return;
      }

      IEnumerable<ClientDetails> clients;

      if (hostConfig.IsBroadcastAddress(destinationIpParsed))
      {
        clients = hostConfig.GetAllClients().Where(client => !client.Id.Equals(gateway.Id) && !client.Name.Equals(gateway.Name));
      }
      else
      {
        var clientDetails = config.IsGateway ? GetClientDetails(destinationIp) : await QueryGateway(destinationIp);

        if (clientDetails == null)
        {
          Console.WriteLine($"No client found for destination IP {destinationIp}. Dropping packet.");
          return;
        }

        clients = [clientDetails];
      }

      foreach (var clientDetails in clients)
      {
        var clientUrl = GenerateClientUrl(config.MiddlemanUrl, clientDetails.Id, clientDetails.Name, "Receive");
        var response = await RequestHandler.MakeHttpRequest(clientUrl, config.MiddlemanJwt, data);

        if (!response.IsSuccessStatusCode) 
        {
          Console.WriteLine($"Failed to send data to client. Status code: {response.StatusCode}");
        }
      }
    }

    public bool IsInVirtualSubnet(IPAddress ipAddress) => hostConfig.IsInVirtualSubnet(ipAddress);

    public string GetAddressMask() => config.Network.AddressMask;

    public IPAddress GetGatewayAddress() => hostConfig.GetGatewayAddress();

    public IPAddress GetNetworkAddress() => hostConfig.GetNetworkAddress();

    private async Task<ClientDetails?> QueryGateway(string destinationIp)
    {
      var gatewayUrl = GenerateClientUrl(config.MiddlemanUrl, config.Network.GatewayId, config.Network.GatewayName, "GetClientDetails");
      var response = await RequestHandler.MakeHttpRequest(gatewayUrl, config.MiddlemanJwt, destinationIp);

      var responseContent = await response.Content.ReadAsStringAsync();
      Console.WriteLine($"Gateway response for IP {destinationIp}: {responseContent}");
      return await RequestHandler.HandleResponse<ClientDetails?>(response);
    }

    private static string GenerateClientUrl(string middlemanUrl, string clientId, string clientName, string method) =>
      $"{middlemanUrl}/client-portal/{clientId}/{clientName}/{method}";
  }
}