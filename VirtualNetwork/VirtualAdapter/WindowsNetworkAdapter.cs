using System.Diagnostics;
using System.Net;
using MiddleManClient.MethodProcessing.MethodDiscovery.Attributes;
using MiddleManClient.ServerContracts;
using VirtualNetwork.Neworking;

namespace VirtualNetwork.VirtualAdapter
{
  public class WindowsNetworkAdapter(Router router) : IVirtualNetworkAdapter
  {
    private const string AdapterName = "Middleman Network";
    private const uint SessionCapacity = 0x00400000;

    private readonly Router router = router;
    private readonly CancellationTokenSource cancellationTokenSource = new();
    private WintunNative.WintunSession? wintunSession;

    public async Task Receive(ServerContext context, IAsyncEnumerable<byte[]> dataStream)
    {
      var session = wintunSession ?? throw new InvalidOperationException("Wintun session is not initialized. Start the adapter first.");

      await foreach (var packet in dataStream.WithCancellation(cancellationTokenSource.Token))
      {
        if (packet.Length < 20)
        {
          Console.WriteLine("Skipping received packet because payload is too small to be an IPv4 packet.");
          continue;
        }

        if (!TryParseIpv4Destination(packet, out _, out _))
        {
          Console.WriteLine("Skipping received packet because it is not a valid IPv4 packet.");
          continue;
        }

        session.SendPacket(packet);
      }
    }

    public async Task Start()
    {
      var ownIp = await router.GetOwnIpAddress();
      wintunSession = WintunNative.OpenOrCreateSession(AdapterName, SessionCapacity);

      Console.WriteLine($"Virtual network adapter started with IP address: {ownIp}");
      ConfigureAdapterInterface(ownIp);

      Console.CancelKeyPress += (_, eventArgs) =>
      {
        eventArgs.Cancel = true;
        cancellationTokenSource.Cancel();
      };

      await RunReadLoop(cancellationTokenSource.Token);
      wintunSession.Dispose();
      wintunSession = null;
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
          if (!TryParseIpv4Destination(packet, out var destinationIp, out var destinationPort))
          {
            Console.WriteLine("Dropping packet because destination IPv4 could not be parsed.");
            continue;
          }

          if (!router.IsInVirtualSubnet(destinationIp))
          {
            continue;
          }

          try
          {
            await using var packetStream = new MemoryStream(packet, writable: false);
            await router.Send(packetStream, destinationIp.ToString(), destinationPort > 0 ? destinationPort : 1);
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
      var gateway = router.GetGatewayAddress();
      var arguments = $"interface ipv4 set address name=\"{AdapterName}\" source=static address={ownIp} mask={mask} gateway={gateway}";

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
        Console.WriteLine("Unable to configure adapter IP: netsh process could not be started.");
        return;
      }

      process.WaitForExit();
      if (process.ExitCode != 0)
      {
        var errorOutput = process.StandardError.ReadToEnd();
        Console.WriteLine($"Adapter IP configuration warning (exit {process.ExitCode}): {errorOutput}");
      }
    }

    private static bool TryParseIpv4Destination(byte[] packet, out IPAddress destinationIp, out int destinationPort)
    {
      destinationIp = IPAddress.None;
      destinationPort = 0;

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

      var protocol = packet[9];
      if ((protocol == 6 || protocol == 17) && packet.Length >= ihlBytes + 4)
      {
        destinationPort = (packet[ihlBytes + 2] << 8) | packet[ihlBytes + 3];
      }

      return true;
    }
  }
}