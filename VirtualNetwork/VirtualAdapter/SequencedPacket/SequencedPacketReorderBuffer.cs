using System.Collections.Generic;

namespace VirtualNetwork.VirtualAdapter
{
  internal sealed class SequencedPacketReorderBuffer
  {
    private static readonly TimeSpan DefaultGapTimeout = TimeSpan.FromMilliseconds(100);
    private readonly object syncRoot = new();
    private readonly Dictionary<ulong, byte[]> bufferedPackets = new();
    private readonly TimeSpan gapTimeout;
    private ulong nextSequenceNumber = 1;
    private DateTimeOffset? gapStartedAt;

    public SequencedPacketReorderBuffer(TimeSpan? gapTimeout = null)
    {
      this.gapTimeout = gapTimeout ?? DefaultGapTimeout;
    }

    public IReadOnlyList<byte[]> Add(ulong sequenceNumber, byte[] packet, DateTimeOffset receivedAt)
    {
      lock (syncRoot)
      {
        var readyPackets = new List<byte[]>();

        if (sequenceNumber < nextSequenceNumber)
        {
          return readyPackets;
        }

        bufferedPackets.TryAdd(sequenceNumber, packet);
        if (sequenceNumber > nextSequenceNumber && gapStartedAt is null)
        {
          gapStartedAt = receivedAt;
        }

        CollectReadyPacketsLocked(receivedAt, readyPackets);
        return readyPackets;
      }
    }

    public IReadOnlyList<byte[]> FlushExpired(DateTimeOffset now)
    {
      lock (syncRoot)
      {
        var readyPackets = new List<byte[]>();
        CollectReadyPacketsLocked(now, readyPackets);
        return readyPackets;
      }
    }

    private void CollectReadyPacketsLocked(DateTimeOffset now, List<byte[]> readyPackets)
    {
      DrainContiguousPackets(readyPackets);

      if (bufferedPackets.Count == 0)
      {
        gapStartedAt = null;
        return;
      }

      if (gapStartedAt is null || now - gapStartedAt.Value < gapTimeout)
      {
        return;
      }

      if (!TryGetLowestBufferedSequenceNumber(out var lowestBufferedSequenceNumber))
      {
        gapStartedAt = null;
        return;
      }

      nextSequenceNumber = lowestBufferedSequenceNumber;
      gapStartedAt = null;
      DrainContiguousPackets(readyPackets);

      if (bufferedPackets.Count > 0)
      {
        gapStartedAt = now;
      }
    }

    private void DrainContiguousPackets(List<byte[]> readyPackets)
    {
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
    }

    private bool TryGetLowestBufferedSequenceNumber(out ulong sequenceNumber)
    {
      sequenceNumber = 0;

      if (bufferedPackets.Count == 0)
      {
        return false;
      }

      sequenceNumber = ulong.MaxValue;
      foreach (var bufferedSequenceNumber in bufferedPackets.Keys)
      {
        if (bufferedSequenceNumber < sequenceNumber)
        {
          sequenceNumber = bufferedSequenceNumber;
        }
      }

      return sequenceNumber != ulong.MaxValue;
    }
  }
}