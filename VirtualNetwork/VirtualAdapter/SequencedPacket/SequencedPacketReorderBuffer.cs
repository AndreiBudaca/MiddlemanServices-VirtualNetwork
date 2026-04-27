using System.Collections.Generic;

namespace VirtualNetwork.VirtualAdapter
{
  internal sealed class SequencedPacketReorderBuffer
  {
    private readonly object syncRoot = new();
    private readonly Dictionary<ulong, byte[]> bufferedPackets = new();
    private ulong nextSequenceNumber = 1;

    public IReadOnlyList<byte[]> Add(ulong sequenceNumber, byte[] packet)
    {
      lock (syncRoot)
      {
        if (sequenceNumber < nextSequenceNumber)
        {
          return Array.Empty<byte[]>();
        }

        bufferedPackets.TryAdd(sequenceNumber, packet);

        var readyPackets = new List<byte[]>();

        while (bufferedPackets.TryGetValue(nextSequenceNumber, out var readyPacket))
        {
          bufferedPackets.Remove(nextSequenceNumber);
          readyPackets.Add(readyPacket);
          nextSequenceNumber++;
        }

        if (bufferedPackets.Count > 1_000_000)
        {
          throw new InvalidOperationException("Sequenced packet buffer grew too large while waiting for missing packets.");
        }

        return readyPackets;
      }
    }
  }
}