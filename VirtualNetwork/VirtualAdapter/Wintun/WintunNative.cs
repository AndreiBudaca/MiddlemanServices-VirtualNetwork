using System.ComponentModel;
using System.Runtime.InteropServices;

namespace VirtualNetwork.VirtualAdapter
{
  internal static class WintunNative
  {
    private const uint ErrorNoMoreItems = 259;
    private const uint WaitObject0 = 0;
    private const uint WaitTimeout = 258;

    [DllImport("wintun", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr WintunCreateAdapter(string name, string tunnelType, IntPtr requestedGuid);

    [DllImport("wintun", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr WintunOpenAdapter(string name);

    [DllImport("wintun", SetLastError = true)]
    private static extern void WintunCloseAdapter(IntPtr adapter);

    [DllImport("wintun", SetLastError = true)]
    private static extern IntPtr WintunStartSession(IntPtr adapter, uint capacity);

    [DllImport("wintun", SetLastError = true)]
    private static extern void WintunEndSession(IntPtr session);

    [DllImport("wintun", SetLastError = true)]
    private static extern IntPtr WintunGetReadWaitEvent(IntPtr session);

    [DllImport("wintun", SetLastError = true)]
    private static extern IntPtr WintunReceivePacket(IntPtr session, out uint packetSize);

    [DllImport("wintun", SetLastError = true)]
    private static extern void WintunReleaseReceivePacket(IntPtr session, IntPtr packet);

    [DllImport("wintun", SetLastError = true)]
    private static extern IntPtr WintunAllocateSendPacket(IntPtr session, uint packetSize);

    [DllImport("wintun", SetLastError = true)]
    private static extern void WintunSendPacket(IntPtr session, IntPtr packet);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

    public static WintunSession OpenOrCreateSession(string adapterName, uint capacity)
    {
      var adapter = WintunOpenAdapter(adapterName);
      if (adapter == IntPtr.Zero)
      {
        adapter = WintunCreateAdapter(adapterName, "VirtualNetwork", IntPtr.Zero);
      }

      if (adapter == IntPtr.Zero)
      {
        throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to open or create Wintun adapter.");
      }

      var session = WintunStartSession(adapter, capacity);
      if (session == IntPtr.Zero)
      {
        var error = Marshal.GetLastWin32Error();
        WintunCloseAdapter(adapter);
        throw new Win32Exception(error, "Failed to start Wintun session.");
      }

      var readWaitHandle = WintunGetReadWaitEvent(session);
      if (readWaitHandle == IntPtr.Zero)
      {
        WintunEndSession(session);
        WintunCloseAdapter(adapter);
        throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to get Wintun read event handle.");
      }

      return new WintunSession(adapter, session, readWaitHandle);
    }

    internal sealed class WintunSession(IntPtr adapterHandle, IntPtr sessionHandle, IntPtr readWaitHandle) : IDisposable
    {
      private IntPtr adapterHandle = adapterHandle;
      private IntPtr sessionHandle = sessionHandle;
      private readonly IntPtr readWaitHandle = readWaitHandle;

      public bool WaitForPacket(CancellationToken cancellationToken)
      {
        while (!cancellationToken.IsCancellationRequested)
        {
          var waitResult = WaitForSingleObject(readWaitHandle, 250);
          if (waitResult == WaitObject0)
          {
            return true;
          }

          if (waitResult == WaitTimeout)
          {
            continue;
          }

          throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed while waiting for Wintun packet event.");
        }

        return false;
      }

      public bool TryReceivePacket(out byte[] packet)
      {
        packet = Array.Empty<byte>();
        var packetPointer = WintunReceivePacket(sessionHandle, out var packetSize);
        if (packetPointer == IntPtr.Zero)
        {
          var error = Marshal.GetLastWin32Error();
          if (error == ErrorNoMoreItems)
          {
            return false;
          }

          throw new Win32Exception(error, "Failed to receive Wintun packet.");
        }

        packet = new byte[packetSize];
        Marshal.Copy(packetPointer, packet, 0, (int)packetSize);
        WintunReleaseReceivePacket(sessionHandle, packetPointer);
        return true;
      }

      public void SendPacket(byte[] packet)
      {
        if (packet.Length == 0)
        {
          return;
        }

        var packetPointer = WintunAllocateSendPacket(sessionHandle, (uint)packet.Length);
        if (packetPointer == IntPtr.Zero)
        {
          throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to allocate Wintun send packet.");
        }

        Marshal.Copy(packet, 0, packetPointer, packet.Length);
        WintunSendPacket(sessionHandle, packetPointer);
      }

      public void Dispose()
      {
        if (sessionHandle != IntPtr.Zero)
        {
          WintunEndSession(sessionHandle);
          sessionHandle = IntPtr.Zero;
        }

        if (adapterHandle != IntPtr.Zero)
        {
          WintunCloseAdapter(adapterHandle);
          adapterHandle = IntPtr.Zero;
        }

        GC.SuppressFinalize(this);
      }
    }
  }
}
