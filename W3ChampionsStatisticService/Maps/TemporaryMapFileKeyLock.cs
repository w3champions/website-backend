using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace W3ChampionsStatisticService.Maps;

/// <summary>
/// One async mutex per fileKey (R-I1/S-H1): two uploads of the same fileKey — the same bytes under the same name, or two
/// restores of one record — never interleave their store, digest check, record write, re-probe and compensation, so
/// neither can delete bytes the other has just stored. The expiry sweep takes the same lock around its own probe and
/// delete of a fileKey (S6-M1), so a reclaim can never land on bytes an upload has just stored either. Keys are compared
/// ordinally, like update-service's byte-exact file paths. Entries are reference-counted and removed when their last
/// holder or waiter leaves, so the dictionary never outgrows the uploads in flight.
/// <para>
/// In-memory and single-instance by design, like <see cref="Sessions.MintRateLimiter"/>: ONE instance per process,
/// registered once in <see cref="MapServiceExtensions.AddMapServices"/> and injected into the upload service and the
/// sweep; two website-backend instances would not see each other's locks. Concurrency idiom as in MintRateLimiter: a
/// private Dictionary guarded by one lock object.
/// </para>
/// </summary>
public sealed class TemporaryMapFileKeyLock
{
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly object _lock = new();

    /// <summary>Test seam: called with the key when an acquire finds it already held or awaited, just before waiting.</summary>
    internal Action<string> OnContended { get; init; }

    /// <summary>Test seam: the number of keys currently held or awaited.</summary>
    internal int Count
    {
        get
        {
            lock (_lock)
            {
                return _entries.Count;
            }
        }
    }

    /// <summary>
    /// Waits for <paramref name="key"/> and returns the handle that releases it. A cancelled wait throws
    /// <see cref="OperationCanceledException"/> and holds nothing.
    /// </summary>
    public async Task<IDisposable> AcquireAsync(string key, CancellationToken cancellationToken)
    {
        Entry entry;
        bool contended;
        lock (_lock)
        {
            if (!_entries.TryGetValue(key, out entry))
            {
                entry = new Entry();
                _entries.Add(key, entry);
            }

            entry.References++;
            contended = entry.References > 1;
        }

        try
        {
            if (contended)
            {
                OnContended?.Invoke(key);
            }

            await entry.Semaphore.WaitAsync(cancellationToken);
        }
        catch
        {
            Leave(key, entry);
            throw;
        }

        return new Holder(this, key, entry);
    }

    private void Leave(string key, Entry entry)
    {
        lock (_lock)
        {
            if (--entry.References == 0)
            {
                _entries.Remove(key);
            }
        }
    }

    private sealed class Entry
    {
        public SemaphoreSlim Semaphore { get; } = new(1, 1);

        public int References { get; set; }
    }

    private sealed class Holder(TemporaryMapFileKeyLock owner, string key, Entry entry) : IDisposable
    {
        private readonly TemporaryMapFileKeyLock _owner = owner;
        private readonly string _key = key;
        private readonly Entry _entry = entry;

        public void Dispose()
        {
            _entry.Semaphore.Release();
            _owner.Leave(_key, _entry);
        }
    }
}
