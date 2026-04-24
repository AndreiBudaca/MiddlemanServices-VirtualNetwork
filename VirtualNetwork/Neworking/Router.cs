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
    private readonly HostConfiguration hostConfig = new(config.Network.Address, config.Network.AddressMask);

    private readonly ClientDetails gateway = new()
    {
      Id = config.Network.GatewayId,
      Name = config.Network.GatewayName
    };

    [MiddleManMethod]
    public string Connect(ServerContext context, string clientName)
    {
      if (!config.IsGateway) return string.Empty;
      if (string.IsNullOrEmpty(clientName) || string.IsNullOrEmpty(context.Request.User!.Identifier)) return string.Empty;

      var clientDetails = new ClientDetails
      {
        Id = context.Request.User.Identifier,
        Name = clientName
      };

      var assignedIp = hostConfig.AssignIpAddress(clientDetails);
      return assignedIp.ToString();
    }

    public async Task<IPAddress> GetOwnIpAddress()
    {
      if (config.IsGateway)
      {
        return IPAddress.Parse(config.Network.Address);
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
      return hostConfig.GetClientByIp(ip);
    }

    public async Task Send(Stream data, string destinationIp, int destinationPort)
    {
      if (string.IsNullOrEmpty(destinationIp) || destinationPort <= 0) return;

      var clientDetails = (config.IsGateway ? GetClientDetails(destinationIp) : await QueryGateway(destinationIp))
        ?? throw new Exception("Client details not found for the destination IP");

      var clientUrl = GenerateClientUrl(config.MiddlemanUrl, clientDetails.Id, clientDetails.Name, "Receive");
      var response = await RequestHandler.MakeHttpRequest(clientUrl, config.MiddlemanJwt, data);

      if (!response.IsSuccessStatusCode) throw new Exception($"Failed to send data to client. Status code: {response.StatusCode}");
    }

    private async Task<ClientDetails?> QueryGateway(string destinationIp)
    {
      var gatewayUrl = GenerateClientUrl(config.MiddlemanUrl, config.Network.GatewayId, config.Network.GatewayName, "GetClientDetails");
      var response = await RequestHandler.MakeHttpRequest(gatewayUrl, config.MiddlemanJwt, destinationIp);

      return await RequestHandler.HandleResponse<ClientDetails?>(response);
    }

    private static string GenerateClientUrl(string middlemanUrl, string clientId, string clientName, string method) =>
      $"{middlemanUrl}/client-portal/{clientId}/{clientName}/{method}";
  }
}