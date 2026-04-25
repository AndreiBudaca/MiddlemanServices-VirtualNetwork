using System.Net;
using VirtualNetwork.Config;
using VirtualNetwork.Neworking.Models;

namespace VirtualNetwork.Neworking.AddressManagement
{
  public class HostConfiguration
  {
    private readonly Dictionary<ClientDetails, IPAddress> clientIpMap = new(new ClientDetailsComparer());
    private readonly Dictionary<IPAddress, ClientDetails> ipClientMap = new(new IPAddressComparer());
    private readonly IPAddress networkAddress;
    private readonly IPAddress mask;
    private readonly IPAddress gatewayAddress;
    private IPAddress lastAssignedIp;

    public HostConfiguration(NetworkConfig config)
    {
      networkAddress = IPAddress.Parse(config.Address);
      mask = IPAddress.Parse(config.AddressMask);
      gatewayAddress = CalculateGatewayAddress(networkAddress, config.AddressMask);
      lastAssignedIp = gatewayAddress;

      var gatewayClientDetails = new ClientDetails { Id = config.GatewayId, Name = config.GatewayName };
      clientIpMap.Add(gatewayClientDetails, gatewayAddress);
      ipClientMap.Add(gatewayAddress, gatewayClientDetails);
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

    public IEnumerable<ClientDetails> GetAllClients()
    {
      return clientIpMap.Keys;
    }

    public void CacheClientIp(ClientDetails client, IPAddress ip)
    {
      clientIpMap[client] = ip;
      ipClientMap[ip] = client;
    }

    public IPAddress GetGatewayAddress() => gatewayAddress;

    public IPAddress GetNetworkAddress() => networkAddress;

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

    public bool IsInVirtualSubnet(IPAddress ipAddress)
    {
      var ipBytes = ipAddress.GetAddressBytes();
      var networkBytes = networkAddress.GetAddressBytes();
      var maskBytes = mask.GetAddressBytes();

      if (ipBytes.Length != networkBytes.Length || ipBytes.Length != maskBytes.Length)
      {
        return false;
      }

      for (int i = 0; i < ipBytes.Length; i++)
      {
        if ((ipBytes[i] & maskBytes[i]) != (networkBytes[i] & maskBytes[i]))
        {
          return false;
        }
      }

      return true;
    }

    public bool IsBroadcastAddress(IPAddress ipAddress)
    {
      var ipBytes = ipAddress.GetAddressBytes();
      var networkBytes = networkAddress.GetAddressBytes();
      var maskBytes = mask.GetAddressBytes();

      if (ipBytes.Length != networkBytes.Length || ipBytes.Length != maskBytes.Length)
      {
        return false;
      }

      // Ensure the IP is in the same subnet first.
      for (int i = 0; i < ipBytes.Length; i++)
      {
        if ((ipBytes[i] & maskBytes[i]) != (networkBytes[i] & maskBytes[i]))
        {
          return false;
        }
      }

      // Broadcast = network OR inverted mask.
      for (int i = 0; i < ipBytes.Length; i++)
      {
        byte broadcastByte = (byte)(networkBytes[i] | (byte)~maskBytes[i]);
        if (ipBytes[i] != broadcastByte)
        {
          return false;
        }
      }

      return true;
    }

    private static IPAddress CalculateGatewayAddress(IPAddress networkAddress, string addressMask)
    {
      var maskBytes = IPAddress.Parse(addressMask).GetAddressBytes();
      var networkBytes = networkAddress.GetAddressBytes();

      if (maskBytes.Length != networkBytes.Length)
      {
        throw new ArgumentException("Mask and network address must have the same address family.");
      }

      // Use first host address in subnet as gateway candidate: network + 1.
      var gatewayBytes = new byte[networkBytes.Length];
      Array.Copy(networkBytes, gatewayBytes, networkBytes.Length);

      for (int i = gatewayBytes.Length - 1; i >= 0; i--)
      {
        if (gatewayBytes[i] < 255)
        {
          gatewayBytes[i]++;
          break;
        }

        gatewayBytes[i] = 0;
      }

      return new IPAddress(gatewayBytes);
    }
  }

  public class ClientDetailsComparer : IEqualityComparer<ClientDetails>
  {
    public bool Equals(ClientDetails? x, ClientDetails? y)
    {
      if (ReferenceEquals(x, y)) return true;
      if (x is null || y is null) return false;
      return x.Id.Equals(y.Id) && x.Name.Equals(y.Name);
    }

    public int GetHashCode(ClientDetails obj)
    {
      return HashCode.Combine(obj.Id, obj.Name);
    }
  }

  public class IPAddressComparer : IEqualityComparer<IPAddress>
  {
    public bool Equals(IPAddress? x, IPAddress? y)
    {
      if (ReferenceEquals(x, y)) return true;
      if (x is null || y is null) return false;
      return x.ToString().Equals(y.ToString());
    }

    public int GetHashCode(IPAddress obj)
    {
      return obj.GetHashCode();
    }
  }
}