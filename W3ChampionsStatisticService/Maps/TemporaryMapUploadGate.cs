using System;
using System.Collections.Generic;
using System.Threading;

namespace W3ChampionsStatisticService.Maps;

/// <summary>
/// Bounds the temporary-map uploads in flight, in one place: at most one per battleTag (D7) and at most
/// <see cref="TemporaryMapLimits.MaxConcurrentUploads"/> per process (Task 2 security H1). The controller acquires a slot
/// after the auth filter and BEFORE the first request-body byte, so a spool of up to 256 MiB is never started for an
/// upload that will be refused, and releases it in a finally once the service has returned — compensation included.
/// The per-battleTag slot is taken first, then the process-wide one; a global refusal gives the per-battleTag slot back.
/// Both refusals answer <c>429 { code: "QUOTA_EXCEEDED", retryAfterSeconds: 30 }</c>, the pinned A.3 body.
/// <para>
/// A slot is held for however long the upload takes. Once the bytes are stored the service runs to its outcome without
/// the request token, so under an upstream outage one slot can be held for the worst case of that path (~10 minutes:
/// client timeouts on the record write and the re-probe, then four compensation attempts) — accepted by ruling:
/// correctness over latency while uploads fail anyway; new uploads then see 429 and retry.
/// </para>
/// <para>
/// In-memory and single-instance by design, like <see cref="Sessions.MintRateLimiter"/> and
/// <see cref="TemporaryMapFileKeyLock"/>: two website-backend instances would not share the cap. Same concurrency idiom:
/// private state guarded by one lock object. BattleTags are compared ordinally, as the limiter keys them.
/// </para>
/// </summary>
public sealed class TemporaryMapUploadGate
{
    private readonly HashSet<string> _battleTagsInFlight = new(StringComparer.Ordinal);
    private readonly object _lock = new();
    private int _inFlight;

    /// <summary>Test seam: uploads currently holding a slot.</summary>
    internal int InFlight
    {
        get
        {
            lock (_lock)
            {
                return _inFlight;
            }
        }
    }

    /// <summary>
    /// Takes a slot for one upload by <paramref name="battleTag"/>. On true, <paramref name="slot"/> releases it when
    /// disposed (idempotent); on false nothing is held and <paramref name="slot"/> is null. A blank battleTag is a
    /// caller bug: the controller fails closed before asking.
    /// </summary>
    public bool TryAcquire(string battleTag, out IDisposable slot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(battleTag);
        lock (_lock)
        {
            // 1. The per-battleTag slot: a second upload while this player's first is still in flight is refused.
            if (!_battleTagsInFlight.Add(battleTag))
            {
                slot = null;
                return false;
            }

            // 2. The process-wide slot; a refusal hands the per-battleTag slot straight back.
            if (_inFlight >= TemporaryMapLimits.MaxConcurrentUploads)
            {
                _battleTagsInFlight.Remove(battleTag);
                slot = null;
                return false;
            }

            _inFlight++;
        }

        slot = new Slot(this, battleTag);
        return true;
    }

    private void Release(string battleTag)
    {
        lock (_lock)
        {
            _battleTagsInFlight.Remove(battleTag);
            _inFlight--;
        }
    }

    private sealed class Slot(TemporaryMapUploadGate gate, string battleTag) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                gate.Release(battleTag);
            }
        }
    }
}
