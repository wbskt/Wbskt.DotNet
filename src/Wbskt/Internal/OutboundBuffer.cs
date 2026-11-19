
namespace Wbskt.Internal;

/// <summary>
/// Bounded, in-memory FIFO of messages waiting for a connection. When full, the oldest
/// message is dropped so the most recent readings survive a long outage.
/// </summary>
internal sealed class OutboundBuffer(int capacity)
{
    private readonly LinkedList<OutboundFrame> _items = new();
    private readonly object _gate = new();

    public int Capacity { get; } = capacity;

    public int Count
    {
        get { lock (_gate) { return _items.Count; } }
    }

    private long _dropped;

    public long DroppedCount
    {
        get { lock (_gate) { return _dropped; } }
    }

    public void Enqueue(OutboundFrame message)
    {
        lock (_gate)
        {
            if (Capacity <= 0)
            {
                _dropped++;
                return;
            }

            while (_items.Count >= Capacity)
            {
                _items.RemoveFirst();
                _dropped++;
            }

            _items.AddLast(message);
        }
    }

    public bool TryPeek(out OutboundFrame message)
    {
        lock (_gate)
        {
            if (_items.First is { } first)
            {
                message = first.Value;
                return true;
            }

            message = null!;
            return false;
        }
    }

    /// <summary>
    /// Removes <paramref name="message"/> if it is still at the head. It may already be gone
    /// when the buffer overflowed while it was being sent.
    /// </summary>
    public void RemoveIfHead(OutboundFrame message)
    {
        lock (_gate)
        {
            if (_items.First is { } first && ReferenceEquals(first.Value, message))
            {
                _items.RemoveFirst();
            }
        }
    }
}
