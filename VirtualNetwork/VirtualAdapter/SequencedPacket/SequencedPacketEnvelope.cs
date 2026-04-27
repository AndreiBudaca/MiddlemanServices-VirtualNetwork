using System.Buffers.Binary;

namespace VirtualNetwork.VirtualAdapter
{
  internal static class SequencedPacketEnvelope
  {
    private const int HeaderSize = sizeof(ulong) + sizeof(int);

    public static byte[] Wrap(ulong sequenceNumber, byte[] packet)
    {
      var payload = new byte[HeaderSize + packet.Length];
      BinaryPrimitives.WriteUInt64BigEndian(payload.AsSpan(0, sizeof(ulong)), sequenceNumber);
      BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(sizeof(ulong), sizeof(int)), packet.Length);
      packet.CopyTo(payload.AsSpan(HeaderSize));
      return payload;
    }

    public static bool TryUnwrap(byte[] packet, out ulong sequenceNumber, out byte[] payload)
    {
      sequenceNumber = 0;
      payload = Array.Empty<byte>();

      if (packet.Length < HeaderSize)
      {
        return false;
      }

      sequenceNumber = BinaryPrimitives.ReadUInt64BigEndian(packet.AsSpan(0, sizeof(ulong)));
      var payloadLength = BinaryPrimitives.ReadInt32BigEndian(packet.AsSpan(sizeof(ulong), sizeof(int)));
      if (payloadLength < 0)
      {
        return false;
      }

      if (packet.Length != HeaderSize + payloadLength)
      {
        return false;
      }

      payload = new byte[payloadLength];
      packet.AsSpan(HeaderSize, payloadLength).CopyTo(payload);
      return true;
    }
  }
}