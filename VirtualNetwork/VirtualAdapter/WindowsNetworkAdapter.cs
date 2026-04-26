using System.Diagnostics;
using System.Net;
using VirtualNetwork.Neworking;

namespace VirtualNetwork.VirtualAdapter
{
  public class WindowsNetworkAdapter(Router router) : IVirtualNetworkAdapter
  {
    private const string AdapterName = "Middleman Network";
    private const uint SessionCapacity = 0x00400000;
    private static readonly int[] PreferredMtuValues = [30000, 9000, 1500];

    private readonly Router router = router;
    private readonly CancellationTokenSource cancellationTokenSource = new();
    private WintunNative.WintunSession? wintunSession;
    private bool routeConfigured;

    public async Task Receive(byte[] packet)
    {
      var session = wintunSession ?? throw new InvalidOperationException("Wintun session is not initialized. Start the adapter first.");

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

      session.SendPacket(packet);
    }

    public async Task Start()
    {
      var ownIp = await router.GetOwnIpAddress();
      wintunSession = WintunNative.OpenOrCreateSession(AdapterName, SessionCapacity);

      Console.WriteLine($"Virtual network adapter started with IP address: {ownIp}");
      ConfigureAdapterInterface(ownIp);
      ConfigureVirtualSubnetRoute();

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
        RemoveVirtualSubnetRoute();
        wintunSession?.Dispose();
        wintunSession = null;
      }
    }

    private async Task RunReadLoop(CancellationToken cancellationToken)
    {
      var session = wintunSession ?? throw new InvalidOperationException("Wintun session is not initialized.");

      while (!cancellationToken.IsCancellationRequested)
      {
        if (!session.WaitForPacket(cancellationToken))
        {
          continue;
        }

        while (session.TryReceivePacket(out var packet))
        {
          if (!TryParseIpv4Destination(packet, out var destinationIp))
          {
            //Console.WriteLine("Dropping packet because destination IPv4 could not be parsed.");
            continue;
          }

          if (!router.IsInVirtualSubnet(destinationIp))
          {
            //Console.WriteLine($"Dropping packet for non-virtual destination {destinationIp}.");
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

    private void ConfigureAdapterInterface(IPAddress ownIp)
    {
      var mask = router.GetAddressMask();
      var arguments = $"interface ipv4 set address name=\"{AdapterName}\" source=static address={ownIp} mask={mask}";
      ExecuteNetsh(arguments, "adapter IPv4 address configuration");
      ConfigureAdapterMtu();
    }

    private void ConfigureAdapterMtu()
    {
      foreach (var mtu in PreferredMtuValues)
      {
        var applied = ExecuteNetsh($"interface ipv4 set subinterface \"{AdapterName}\" mtu={mtu} store=active", $"adapter MTU configuration ({mtu})", treatFailureAsWarning: true);
        if (!applied)
        {
          continue;
        }

        if (TryGetConfiguredMtu(AdapterName, out var configuredMtu) && configuredMtu == mtu)
        {
          Console.WriteLine($"MTU configured to {configuredMtu} on {AdapterName}.");
          return;
        }

        Console.WriteLine($"MTU verification failed for {AdapterName} at value {mtu}. Trying next fallback value.");
      }

      Console.WriteLine($"Failed to configure and verify MTU on {AdapterName}; Windows may enforce a lower MTU than requested.");
    }

    private void ConfigureVirtualSubnetRoute()
    {
      var routePrefix = GetVirtualRoutePrefix();
      var gateway = router.GetGatewayAddress();

      // Ensure route lifecycle is runtime-only and deterministic for this process.
      ExecuteNetsh($"interface ipv4 delete route prefix={routePrefix} interface=\"{AdapterName}\" nexthop={gateway}", "virtual subnet route cleanup before add", treatFailureAsWarning: true);

      var added = ExecuteNetsh($"interface ipv4 add route prefix={routePrefix} interface=\"{AdapterName}\" nexthop={gateway} metric=10 store=active", "virtual subnet route add");
      routeConfigured = added;
    }

    private void RemoveVirtualSubnetRoute()
    {
      if (!routeConfigured)
      {
        return;
      }

      var routePrefix = GetVirtualRoutePrefix();
      var gateway = router.GetGatewayAddress();

      ExecuteNetsh($"interface ipv4 delete route prefix={routePrefix} interface=\"{AdapterName}\" nexthop={gateway}", "virtual subnet route remove", treatFailureAsWarning: true);
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

    private static bool ExecuteNetsh(string arguments, string operationName, bool treatFailureAsWarning = false)
    {
      var process = Process.Start(new ProcessStartInfo
      {
        FileName = "netsh",
        Arguments = arguments,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
      });

      if (process is null)
      {
        Console.WriteLine($"Unable to run netsh for {operationName}: process could not be started.");
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
      Console.WriteLine($"Netsh {severity} during {operationName} (exit {process.ExitCode}): {errorOutput}");
      return false;
    }

    private static bool TryGetConfiguredMtu(string adapterName, out int mtu)
    {
      mtu = 0;

      var process = Process.Start(new ProcessStartInfo
      {
        FileName = "netsh",
        Arguments = "interface ipv4 show subinterfaces",
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
      });

      if (process is null)
      {
        Console.WriteLine("Unable to read MTU from netsh: process could not be started.");
        return false;
      }

      process.WaitForExit();
      if (process.ExitCode != 0)
      {
        var errorOutput = process.StandardError.ReadToEnd();
        Console.WriteLine($"Unable to read MTU from netsh (exit {process.ExitCode}): {errorOutput}");
        return false;
      }

      var output = process.StandardOutput.ReadToEnd();
      using var reader = new StringReader(output);
      while (reader.ReadLine() is { } line)
      {
        if (!line.Contains(adapterName, StringComparison.OrdinalIgnoreCase))
        {
          continue;
        }

        var columns = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (columns.Length < 4)
        {
          continue;
        }

        if (!int.TryParse(columns[0], out mtu))
        {
          mtu = 0;
          continue;
        }

        return true;
      }

      return false;
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