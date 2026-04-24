using System.Net;
using VirtualNetwork.Neworking.Models;

namespace VirtualNetwork.Neworking.AddressManagement
{
  public class HostConfiguration
  {
    private readonly Dictionary<ClientDetails, IPAddress> clientIpMap = [];
    private readonly Dictionary<IPAddress, ClientDetails> ipClientMap = [];
    private readonly IPAddress networkAddress;
    private readonly IPAddress gatewayAddress;
    private IPAddress lastAssignedIp;

    public HostConfiguration(string networkAddress, string addressMask)
    {
      this.networkAddress = IPAddress.Parse(networkAddress);
      gatewayAddress = CalculateGatewayAddress(this.networkAddress, addressMask);
      lastAssignedIp = gatewayAddress;
    }

    public IPAddress AssignIpAddress(ClientDetails client)
    {
      if (clientIpMap.TryGetValue(client, out IPAddress? value)) return value;

      var newIp = GetNextAvailableIp();
      clientIpMap[client] = newIp;
      ipClientMap[newIp] = client;

      return newIp;
    }

    public ClientDetails? GetClientByIp(IPAddress ip)
    {
      return ipClientMap.TryGetValue(ip, out ClientDetails? value) ? value : null;
    }

    public IPAddress GetGatewayAddress() => gatewayAddress;

    private IPAddress GetNextAvailableIp()
    {
      var nextIpBytes = lastAssignedIp.GetAddressBytes();
      for (int i = 3; i >= 0; i--)
      {
        if (nextIpBytes[i] < 255)
        {
          nextIpBytes[i]++;
          break;
        }
        else
        {
          nextIpBytes[i] = 0;
        }
      }

      var nextIp = new IPAddress(nextIpBytes);
      if (nextIp.Equals(networkAddress) || nextIp.Equals(gatewayAddress) || ipClientMap.ContainsKey(nextIp))
      {
        return GetNextAvailableIp();
      }

      lastAssignedIp = nextIp;
      return nextIp;
    }

    private static IPAddress CalculateGatewayAddress(IPAddress networkAddress, string addressMask)
    {
      var maskBytes = IPAddress.Parse(addressMask).GetAddressBytes();
      var networkBytes = networkAddress.GetAddressBytes();

      var gatewayBytes = new byte[4];
      for (int i = 0; i < 4; i++)
      {
        gatewayBytes[i] = (byte)(networkBytes[i] | (maskBytes[i] & 0xFF));
      }

      return new IPAddress(gatewayBytes);
    }
  }
}