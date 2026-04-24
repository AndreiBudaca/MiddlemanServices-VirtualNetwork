using System.Runtime.InteropServices;

namespace VirtualNetwork.VirtualAdapter
{
  internal sealed class LinuxTunDevice : IDisposable
  {
    private const int O_RDWR = 0x0002;
    private const int O_NONBLOCK = 0x0800;
    private const short IFF_TUN = 0x0001;
    private const short IFF_NO_PI = 0x1000;
    private const ulong TUNSETIFF = 0x400454CA;
    private const short POLLIN = 0x0001;

    private const int IFNAMSIZ = 16;
    private const int MaxPacketSize = 65535;
    private const int EAGAIN = 11;
    private const int EINTR = 4;

    private int fd;

    public string InterfaceName { get; }

    private LinuxTunDevice(int fd, string interfaceName)
    {
      this.fd = fd;
      InterfaceName = interfaceName;
    }

    public static LinuxTunDevice OpenOrCreate(string requestedNamePattern)
    {
      var fd = open("/dev/net/tun", O_RDWR | O_NONBLOCK);
      if (fd < 0)
      {
        throw CreateIOException("Failed to open /dev/net/tun");
      }

      try
      {
        var ifreq = new byte[40];
        var nameBytes = System.Text.Encoding.ASCII.GetBytes(requestedNamePattern);
        var copyLength = Math.Min(nameBytes.Length, IFNAMSIZ - 1);
        Array.Copy(nameBytes, ifreq, copyLength);

        var flagsOffset = IFNAMSIZ;
        ifreq[flagsOffset] = (byte)((IFF_TUN | IFF_NO_PI) & 0xFF);
        ifreq[flagsOffset + 1] = (byte)(((IFF_TUN | IFF_NO_PI) >> 8) & 0xFF);

        var handle = Marshal.AllocHGlobal(ifreq.Length);
        try
        {
          Marshal.Copy(ifreq, 0, handle, ifreq.Length);
          var result = ioctl(fd, TUNSETIFF, handle);
          if (result < 0)
          {
            throw CreateIOException("Failed to configure TUN interface (TUNSETIFF)");
          }

          Marshal.Copy(handle, ifreq, 0, ifreq.Length);
        }
        finally
        {
          Marshal.FreeHGlobal(handle);
        }

        var actualName = ReadNullTerminatedAscii(ifreq.AsSpan(0, IFNAMSIZ));
        if (string.IsNullOrWhiteSpace(actualName))
        {
          throw new IOException("Kernel returned an empty TUN interface name.");
        }

        return new LinuxTunDevice(fd, actualName);
      }
      catch
      {
        close(fd);
        throw;
      }
    }

    public bool WaitForPacket(CancellationToken cancellationToken)
    {
      var pollFds = new[]
      {
        new PollFd
        {
          fd = fd,
          events = POLLIN,
          revents = 0
        }
      };

      while (!cancellationToken.IsCancellationRequested)
      {
        var result = poll(pollFds, (uint)pollFds.Length, 500);
        if (result > 0)
        {
          return true;
        }

        if (result == 0)
        {
          continue;
        }

        var errno = Marshal.GetLastPInvokeError();
        if (errno == EINTR)
        {
          continue;
        }

        throw CreateIOException("poll() failed while waiting for a packet", errno);
      }

      return false;
    }

    public bool TryReadPacket(out byte[] packet)
    {
      var buffer = new byte[MaxPacketSize];
      var bytesRead = read(fd, buffer, (nuint)buffer.Length);
      if (bytesRead > 0)
      {
        packet = new byte[bytesRead];
        Buffer.BlockCopy(buffer, 0, packet, 0, bytesRead);
        return true;
      }

      if (bytesRead == 0)
      {
        packet = Array.Empty<byte>();
        return false;
      }

      var errno = Marshal.GetLastPInvokeError();
      if (errno == EAGAIN)
      {
        packet = Array.Empty<byte>();
        return false;
      }

      throw CreateIOException("read() failed for TUN device", errno);
    }

    public void WritePacket(byte[] packet)
    {
      var bytesWritten = write(fd, packet, (nuint)packet.Length);
      if (bytesWritten == packet.Length)
      {
        return;
      }

      if (bytesWritten < 0)
      {
        throw CreateIOException("write() failed for TUN device");
      }

      throw new IOException($"Short write to TUN device. Expected {packet.Length} bytes, wrote {bytesWritten}.");
    }

    public void Dispose()
    {
      if (fd < 0)
      {
        return;
      }

      close(fd);
      fd = -1;
      GC.SuppressFinalize(this);
    }

    private static IOException CreateIOException(string message, int? knownErrno = null)
    {
      var errno = knownErrno ?? Marshal.GetLastPInvokeError();
      return new IOException($"{message}: errno {errno}");
    }

    private static string ReadNullTerminatedAscii(ReadOnlySpan<byte> span)
    {
      var zeroIndex = span.IndexOf((byte)0);
      var length = zeroIndex >= 0 ? zeroIndex : span.Length;
      return System.Text.Encoding.ASCII.GetString(span[..length]);
    }

    [DllImport("libc", SetLastError = true)]
    private static extern int open(string pathname, int flags);

    [DllImport("libc", SetLastError = true)]
    private static extern int close(int fd);

    [DllImport("libc", SetLastError = true)]
    private static extern int ioctl(int fd, ulong request, IntPtr argp);

    [DllImport("libc", SetLastError = true)]
    private static extern int poll([In, Out] PollFd[] fds, uint nfds, int timeout);

    [DllImport("libc", SetLastError = true)]
    private static extern int read(int fd, byte[] buffer, nuint count);

    [DllImport("libc", SetLastError = true)]
    private static extern int write(int fd, byte[] buffer, nuint count);

    [StructLayout(LayoutKind.Sequential)]
    private struct PollFd
    {
      public int fd;
      public short events;
      public short revents;
    }
  }
}
