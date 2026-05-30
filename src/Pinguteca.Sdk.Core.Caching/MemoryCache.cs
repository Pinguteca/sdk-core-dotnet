using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Pinguteca.Sdk.Core.Caching;

/// <summary>
/// In-process LRU + TTL + SWR <see cref="ICache"/>. Suitable for
/// single-replica deployments and tests. Multi-replica deployments
/// should use a shared cache implementation (Redis adapter, etc.)
/// because in-memory write-triggered invalidation only clears the
/// writing replica.
/// </summary>
public sealed class MemoryCache : ICache
{
    /// <summary>
    /// Default capacity when none is supplied. Realistic SDK
    /// consumers typically size in the hundreds of distinct
    /// request/method/tenant combinations.
    /// </summary>
    public const int DefaultCapacity = 1024;

    private readonly int _capacity;
    private readonly Func<DateTimeOffset> _now;
    private readonly Lock _lock = new();
    private readonly Dictionary<string, LinkedListNode<Slot>> _items;
    private readonly LinkedList<Slot> _order = new();

    /// <summary>
    /// Constructs an in-memory cache with the supplied capacity.
    /// Values &lt;= 0 fall back to <see cref="DefaultCapacity"/>.
    /// </summary>
    public MemoryCache(int capacity = DefaultCapacity)
        : this(capacity, () => DateTimeOffset.UtcNow)
    {
    }

    internal MemoryCache(int capacity, Func<DateTimeOffset> now)
    {
        if (capacity <= 0) capacity = DefaultCapacity;
        _capacity = capacity;
        _now = now;
        _items = new Dictionary<string, LinkedListNode<Slot>>(capacity);
    }

    /// <inheritdoc />
    public ValueTask<CacheLookup> GetAsync(string key, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            if (!_items.TryGetValue(key, out var node))
            {
                return ValueTask.FromResult(new CacheLookup(null, false));
            }
            var now = _now();
            var hardDeadline = node.Value.Entry.Created + node.Value.Entry.Ttl + node.Value.Entry.Swr;
            if (now > hardDeadline)
            {
                _order.Remove(node);
                _items.Remove(key);
                return ValueTask.FromResult(new CacheLookup(null, false));
            }
            _order.Remove(node);
            _order.AddFirst(node);
            return ValueTask.FromResult(new CacheLookup(node.Value.Entry, true));
        }
    }

    /// <inheritdoc />
    public ValueTask SetAsync(string key, Entry entry, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            if (_items.TryGetValue(key, out var existing))
            {
                existing.Value = new Slot(key, entry);
                _order.Remove(existing);
                _order.AddFirst(existing);
                return ValueTask.CompletedTask;
            }
            var node = new LinkedListNode<Slot>(new Slot(key, entry));
            _order.AddFirst(node);
            _items[key] = node;
            while (_order.Count > _capacity)
            {
                var oldest = _order.Last;
                if (oldest is null) break;
                _order.RemoveLast();
                _items.Remove(oldest.Value.Key);
            }
            return ValueTask.CompletedTask;
        }
    }

    /// <inheritdoc />
    public ValueTask DeleteAsync(string key, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            if (_items.TryGetValue(key, out var node))
            {
                _order.Remove(node);
                _items.Remove(key);
            }
        }
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask DeleteMatchingAsync(string prefix, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(prefix))
        {
            throw new ArgumentException("Prefix must be non-empty.", nameof(prefix));
        }
        lock (_lock)
        {
            // Materialise the matching keys first; mutating during
            // iteration over Dictionary.Keys throws.
            var toRemove = new List<string>();
            foreach (var key in _items.Keys)
            {
                if (key.Contains(prefix, StringComparison.Ordinal))
                {
                    toRemove.Add(key);
                }
            }
            foreach (var key in toRemove)
            {
                if (_items.TryGetValue(key, out var node))
                {
                    _order.Remove(node);
                    _items.Remove(key);
                }
            }
        }
        return ValueTask.CompletedTask;
    }

    private readonly record struct Slot(string Key, Entry Entry);
}
