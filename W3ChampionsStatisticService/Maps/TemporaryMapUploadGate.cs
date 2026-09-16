using System;
using System.Collections.Generic;
using System.Threading;

namespace W3ChampionsStatisticService.Maps;

/// <summary>
/// Bounds the temporary-map uploads in flight, in one place: at most one per battleTag and at most
/// <see cref="TemporaryMapLimits.MaxConcurrentUploads"/> per process, so the spooled temp disk is bounded. The controller acquires a slot
/// after the auth filter and BEFORE the first request-body byte, so a spool of up to 256 MiB is never started for an
/// upload that will be refused, and releases it in a finally once the service has returned — compensation included.
/// The per-battleTag bound is checked first, then the process-wide one; a refusal leaves nothing held and names the
/// bound, so the refusal log can say which without naming the other uploaders. Both refusals answer
/// <c>429 { code: "QUOTA_EXCEEDED", retryAfterSeconds: 30 }</c>, the pinned A.3 body.
/// <para>
/// A slot is held for however long the upload takes. Once the bytes are stored the service runs to its outcome without
/// the request token, so under an upstream outage one slot can be held for the worst case of that path (~10 minutes:
/// client timeouts on the record write and the re-probe, then four compensation attempts) — a deliberate trade:
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
    /// <summary>One entry per upload holding a slot; its count is the process-wide occupancy.</summary>
    private readonly HashSet<string> _battleTagsInFlight = new(StringComparer.Ordinal);
    private readonly object _lock = new();

    /// <summary>Uploads currently holding a slot: logged with a refusal, asserted by the tests.</summary>
    internal int InFlight
    {
        get
        {
            lock (_lock)
            {
                return _battleTagsInFlight.Count;
            }
        }
    }

    /// <summary>
    /// Takes a slot for one upload by <paramref name="battleTag"/>. On true, <paramref name="slot"/> releases it when
    /// disposed (idempotent) and <paramref name="refusedBy"/> is <see cref="TemporaryMapUploadGateBound.None"/>; on false
    /// nothing is held, <paramref name="slot"/> is null and <paramref name="refusedBy"/> names the bound that refused.
    /// A blank battleTag is a caller bug: the controller fails closed before asking.
    /// </summary>
    public bool TryAcquire(string battleTag, out IDisposable slot, out TemporaryMapUploadGateBound refusedBy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(battleTag);
        lock (_lock)
        {
            // 1. The per-battleTag slot: a second upload while this player's first is still in flight is refused.
            if (_battleTagsInFlight.Contains(battleTag))
            {
                slot = null;
                refusedBy = TemporaryMapUploadGateBound.PerBattleTag;
                return false;
            }

            // 2. The process-wide slot: taking this player's would exceed the cap.
            if (_battleTagsInFlight.Count >= TemporaryMapLimits.MaxConcurrentUploads)
            {
                slot = null;
                refusedBy = TemporaryMapUploadGateBound.Global;
                return false;
            }

            _battleTagsInFlight.Add(battleTag);
        }

        slot = new Slot(this, battleTag);
        refusedBy = TemporaryMapUploadGateBound.None;
        return true;
    }

    private void Release(string battleTag)
    {
        lock (_lock)
        {
            _battleTagsInFlight.Remove(battleTag);
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

/// <summary>Which bound of <see cref="TemporaryMapUploadGate"/> refused an upload; <see cref="None"/> when it was admitted.</summary>
public enum TemporaryMapUploadGateBound
{
    None,

    /// <summary>An upload of the same battleTag is in flight.</summary>
    PerBattleTag,

    /// <summary>All <see cref="TemporaryMapLimits.MaxConcurrentUploads"/> slots are taken.</summary>
    Global,
}
