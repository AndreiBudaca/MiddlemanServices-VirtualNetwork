using System.Diagnostics;
using System.Net;
using VirtualNetwork.Neworking;

namespace VirtualNetwork.VirtualAdapter
{
  public class LinuxNetworkAdapter(Router router) : IVirtualNetworkAdapter
  {
    private static readonly int[] PreferredMtuValues = [30000, 9000, 1500];

    private readonly Router router = router;
    private readonly CancellationTokenSource cancellationTokenSource = new();
    private LinuxTunDevice? tunDevice;
    private bool routeConfigured;

    private const string AdapterNamePattern = "mmnet%d";

    public Task Receive(byte[] packet)
    {
      return ReceiveInternal(packet);
    }

    private async Task ReceiveInternal(byte[] packet)
    {
      var device = tunDevice ?? throw new InvalidOperationException("Linux TUN device is not initialized. Start the adapter first.");

      if (packet.Length < 20)
      {
        Console.WriteLine("Skipping received packet because payload is too small to be an IPv4 packet.");
        return;
      }

      if (!TryParseIpv4Destination(packet, out _))
      {
        Console.WriteLine("Skipping received packet because it is not a valid IPv4 packet.");
        return;
      }

      device.WritePacket(packet);
    }

    public async Task Start()
    {
      var ownIp = await router.GetOwnIpAddress();
      tunDevice = LinuxTunDevice.OpenOrCreate(AdapterNamePattern);

      Console.WriteLine($"Linux virtual network adapter started with IP address: {ownIp} on interface {tunDevice.InterfaceName}");
      ConfigureAdapterInterface(tunDevice.InterfaceName, ownIp);
      ConfigureVirtualSubnetRoute(tunDevice.InterfaceName);

      Console.CancelKeyPress += (_, eventArgs) =>
      {
        eventArgs.Cancel = true;
        cancellationTokenSource.Cancel();
      };

      try
      {
        await RunReadLoop(cancellationTokenSource.Token);
      }
      finally
      {
        RemoveVirtualSubnetRoute(tunDevice.InterfaceName);
        ExecuteIp($"link set dev {tunDevice.InterfaceName} down", "adapter interface down", treatFailureAsWarning: true);
        tunDevice.Dispose();
        tunDevice = null;
      }
    }

    private async Task RunReadLoop(CancellationToken cancellationToken)
    {
      var device = tunDevice ?? throw new InvalidOperationException("Linux TUN device is not initialized.");

      while (!cancellationToken.IsCancellationRequested)
      {
        if (!device.WaitForPacket(cancellationToken))
        {
          continue;
        }

        while (device.TryReadPacket(out var packet))
        {
          if (!TryParseIpv4Destination(packet, out var destinationIp))
          {
            Console.WriteLine("Dropping packet because destination IPv4 could not be parsed.");
            continue;
          }

          if (!router.IsInVirtualSubnet(destinationIp))
          {
            Console.WriteLine($"Dropping packet for non-virtual destination {destinationIp}.");
            continue;
          }

          try
          {
            await router.Send(packet, destinationIp);
          }
          catch (Exception ex)
          {
            Console.WriteLine($"Failed to forward packet to {destinationIp}: {ex.Message}");
          }
        }
      }
    }

    private void ConfigureAdapterInterface(string interfaceName, IPAddress ownIp)
    {
      var prefixLength = GetPrefixLength(IPAddress.Parse(router.GetAddressMask()));
      ExecuteIp($"link set dev {interfaceName} up", "adapter link up");
      ConfigureAdapterMtu(interfaceName);
      ExecuteIp($"addr replace {ownIp}/{prefixLength} dev {interfaceName}", "adapter IPv4 address configuration");
    }

    private void ConfigureAdapterMtu(string interfaceName)
    {
      foreach (var mtu in PreferredMtuValues)
      {
        var applied = ExecuteIp($"link set dev {interfaceName} mtu {mtu}", $"adapter MTU configuration ({mtu})", treatFailureAsWarning: true);
        if (!applied)
        {
          continue;
        }

        if (TryGetConfiguredMtu(interfaceName, out var configuredMtu) && configuredMtu == mtu)
        {
          Console.WriteLine($"MTU configured to {configuredMtu} on {interfaceName}.");
          return;
        }

        Console.WriteLine($"MTU verification failed for {interfaceName} at value {mtu}. Trying next fallback value.");
      }

      Console.WriteLine($"Failed to configure and verify MTU on {interfaceName}; kernel may enforce a lower MTU than requested.");
    }

    private void ConfigureVirtualSubnetRoute(string interfaceName)
    {
      var routePrefix = GetVirtualRoutePrefix();
      var gateway = router.GetGatewayAddress();

      ExecuteIp($"route del {routePrefix} via {gateway} dev {interfaceName}", "virtual subnet route cleanup before add", treatFailureAsWarning: true);

      var added = ExecuteIp($"route add {routePrefix} via {gateway} dev {interfaceName} metric 10", "virtual subnet route add");
      routeConfigured = added;
    }

    private void RemoveVirtualSubnetRoute(string interfaceName)
    {
      if (!routeConfigured)
      {
        return;
      }

      var routePrefix = GetVirtualRoutePrefix();
      var gateway = router.GetGatewayAddress();

      ExecuteIp($"route del {routePrefix} via {gateway} dev {interfaceName}", "virtual subnet route remove", treatFailureAsWarning: true);
      routeConfigured = false;
    }

    private string GetVirtualRoutePrefix()
    {
      var networkAddress = router.GetNetworkAddress();
      var mask = IPAddress.Parse(router.GetAddressMask());
      var prefixLength = GetPrefixLength(mask);
      return $"{networkAddress}/{prefixLength}";
    }

    private static int GetPrefixLength(IPAddress subnetMask)
    {
      var bits = 0;

      foreach (var octet in subnetMask.GetAddressBytes())
      {
        var value = octet;
        while (value != 0)
        {
          bits += value & 1;
          value >>= 1;
        }
      }

      return bits;
    }

    private static bool ExecuteIp(string arguments, string operationName, bool treatFailureAsWarning = false)
    {
      var process = Process.Start(new ProcessStartInfo
      {
        FileName = "ip",
        Arguments = arguments,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
      });

      if (process is null)
      {
        Console.WriteLine($"Unable to run ip for {operationName}: process could not be started.");
        return false;
      }

      process.WaitForExit();
      if (process.ExitCode == 0)
      {
        Console.WriteLine($"Successfully completed {operationName}.");
        return true;
      }

      var errorOutput = process.StandardError.ReadToEnd();
      var severity = treatFailureAsWarning ? "warning" : "error";
      Console.WriteLine($"ip {severity} during {operationName} (exit {process.ExitCode}): {errorOutput}");
      return false;
    }

    private static bool TryGetConfiguredMtu(string interfaceName, out int mtu)
    {
      mtu = 0;

      var process = Process.Start(new ProcessStartInfo
      {
        FileName = "ip",
        Arguments = $"link show dev {interfaceName}",
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
      });

      if (process is null)
      {
        Console.WriteLine("Unable to read MTU from ip: process could not be started.");
        return false;
      }

      process.WaitForExit();
      if (process.ExitCode != 0)
      {
        var errorOutput = process.StandardError.ReadToEnd();
        Console.WriteLine($"Unable to read MTU from ip (exit {process.ExitCode}): {errorOutput}");
        return false;
      }

      var output = process.StandardOutput.ReadToEnd();
      var mtuToken = " mtu ";
      var mtuStart = output.IndexOf(mtuToken, StringComparison.Ordinal);
      if (mtuStart < 0)
      {
        return false;
      }

      mtuStart += mtuToken.Length;
      var mtuEnd = output.IndexOf(' ', mtuStart);
      if (mtuEnd < 0)
      {
        mtuEnd = output.Length;
      }

      var mtuText = output[mtuStart..mtuEnd].Trim();
      return int.TryParse(mtuText, out mtu);
    }

    private static bool TryParseIpv4Destination(byte[] packet, out IPAddress destinationIp)
    {
      destinationIp = IPAddress.None;

      if (packet.Length < 20)
      {
        return false;
      }

      var version = (packet[0] & 0xF0) >> 4;
      if (version != 4)
      {
        return false;
      }

      var ihlBytes = (packet[0] & 0x0F) * 4;
      if (ihlBytes < 20 || packet.Length < ihlBytes)
      {
        return false;
      }

      destinationIp = new IPAddress(packet.AsSpan(16, 4));
      return true;
    }

    public Task Receive1(byte[] packet)
    {
      return Receive(packet);
    }

    public Task Receive2(byte[] packet)
    {
      return Receive(packet);
    }

    public Task Receive3(byte[] packet)
    {
      return Receive(packet);
    }

    public Task Receive4(byte[] packet)
    {
      return Receive(packet);
    }

    public Task Receive5(byte[] packet)
    {
      return Receive(packet);
    }

    public Task Receive6(byte[] packet)
    {
      return Receive(packet);
    }

    public Task Receive7(byte[] packet)
    {
      return Receive(packet);
    }

    public Task Receive8(byte[] packet)
    {
      return Receive(packet);
    }

    public Task Receive9(byte[] packet)
    {
      return Receive(packet);
    }

    public Task Receive10(byte[] packet)
    {
      return Receive(packet);
    }

    public Task Receive11(byte[] packet)
    {
      return Receive(packet);
    }

    public Task Receive12(byte[] packet)
    {
      return Receive(packet);
    }
  }
}