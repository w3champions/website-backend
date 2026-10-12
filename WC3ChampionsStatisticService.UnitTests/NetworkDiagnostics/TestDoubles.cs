using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using W3ChampionsStatisticService.NetworkDiagnostics;
using W3ChampionsStatisticService.NetworkDiagnostics.Persistence;
using W3ChampionsStatisticService.NetworkDiagnostics.Results;
using W3ChampionsStatisticService.NetworkDiagnostics.Runs;

namespace WC3ChampionsStatisticService.Tests.NetworkDiagnostics;

/// <summary>A clock tests move by hand.</summary>
public sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;
    public override DateTimeOffset GetUtcNow() => Now;
}

/// <summary>A filter as the server receives it.</summary>
public static class FilterRender
{
    public static BsonDocument Render<T>(FilterDefinition<T> filter) =>
        filter.Render(new RenderArgs<T>(BsonSerializer.SerializerRegistry.GetSerializer<T>(), BsonSerializer.SerializerRegistry));
}

/// <summary>A whole observation list in one of the line-based forms, as a client receives the response body.</summary>
public static class ObservationText
{
    public static string ToCsv(IEnumerable<ObservationDto> rows) => Whole(ObservationFormats.Csv, rows);

    public static string ToNdjson(IEnumerable<ObservationDto> rows) => Whole(ObservationFormats.Ndjson, rows);

    private static string Whole(ObservationFormat format, IEnumerable<ObservationDto> rows) => format.Preamble + format.Render(rows);
}

/// <summary>Returns queued doubles in order, then repeats the last one. Next(max) = floor(NextDouble()*max).</summary>
public sealed class QueueRandom(params double[] values) : IRandom
{
    private readonly Queue<double> _values = new(values);
    private double _last = values.Length > 0 ? values[^1] : 0.0;

    public double NextDouble()
    {
        if (_values.Count > 0) _last = _values.Dequeue();
        return _last;
    }

    public int Next(int maxExclusive) => Math.Min(maxExclusive - 1, (int)(NextDouble() * maxExclusive));
}

/// <summary>
/// A Mongo cursor over prepared batches. A batch is produced when it is moved to, so one can throw the way a getMore
/// fails mid-stream.
/// </summary>
public sealed class FakeCursor<T>(params Func<IEnumerable<T>>[] batches) : IAsyncCursor<T>
{
    private int _next;

    public IEnumerable<T> Current { get; private set; }
    public int BatchesRead => _next;
    public bool Disposed { get; private set; }
    public CancellationToken LastToken { get; private set; }

    public static FakeCursor<T> Of(params T[] rows) => new(() => rows);

    public bool MoveNext(CancellationToken cancellationToken = default)
    {
        LastToken = cancellationToken;
        cancellationToken.ThrowIfCancellationRequested();
        if (_next >= batches.Length) return false;
        Current = batches[_next++]().ToList();
        return true;
    }

    public Task<bool> MoveNextAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return Task.FromResult(MoveNext(cancellationToken));
        }
        catch (Exception e)
        {
            return Task.FromException<bool>(e);
        }
    }

    /// <summary>Completes when the cursor is disposed. A server releases it after the response has ended, so a client that just read the response awaits this instead of looking at <see cref="Disposed"/>.</summary>
    public Task WhenDisposed => _disposed.Task;

    private readonly TaskCompletionSource _disposed = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Dispose()
    {
        Disposed = true;
        _disposed.TrySetResult();
    }
}

/// <summary>
/// The assignment collection in memory, with the queries the dispatcher's push step and the expiry sweeps run
/// (same filters, order and limit as the Mongo repository). Everything else is not supported.
/// </summary>
public sealed class InMemoryAssignments : INetworkDiagnosticsAssignmentRepository
{
    public List<NetworkDiagnosticAssignment> Rows { get; } = [];

    private static bool PendingIn(NetworkDiagnosticAssignment a, DateTime now) =>
        a.Status == AssignmentStatus.Pending && a.DeliverAfter <= now && a.DeliverBy > now;

    private int Update(Func<NetworkDiagnosticAssignment, bool> filter, Func<NetworkDiagnosticAssignment, NetworkDiagnosticAssignment> change)
    {
        var modified = 0;
        for (var i = 0; i < Rows.Count; i++)
        {
            if (!filter(Rows[i])) continue;
            Rows[i] = change(Rows[i]);
            modified++;
        }
        return modified;
    }

    public Task<List<NetworkDiagnosticAssignment>> DueUndeliveredAsync(IReadOnlyCollection<string> battleTags, DateTime now, int limit) =>
        Task.FromResult(Rows.Where(a => battleTags.Contains(a.BattleTag) && a.DeliveredAt == null && PendingIn(a, now))
            .OrderBy(a => a.DeliverAfter).Take(limit).ToList());

    public Task<List<NetworkDiagnosticAssignment>> DueForAsync(string battleTag, DateTime now) =>
        Task.FromResult(Rows.Where(a => a.BattleTag == battleTag && PendingIn(a, now)).ToList());

    public Task<bool> TryMarkDeliveredAsync(string id, DateTime now) =>
        Task.FromResult(Update(a => a.Id == id && a.DeliveredAt == null, a => a with { DeliveredAt = now }) == 1);

    public Task<int> ExpireAllPendingAsync(string runId) =>
        Task.FromResult(Update(a => a.RunId == runId && a.Status == AssignmentStatus.Pending, a => a with { Status = AssignmentStatus.Expired }));

    public Task<int> ExpireUndeliveredAsync(string runId) =>
        Task.FromResult(Update(a => a.RunId == runId && a.Status == AssignmentStatus.Pending && a.DeliveredAt == null, a => a with { Status = AssignmentStatus.Expired }));

    public Task<Dictionary<string, int>> ExpireOverdueAsync(DateTime now)
    {
        var overdue = Rows.Where(a => a.Status == AssignmentStatus.Pending && a.DeliverBy <= now).GroupBy(a => a.RunId).ToDictionary(g => g.Key, g => g.Count());
        Update(a => a.Status == AssignmentStatus.Pending && a.DeliverBy <= now, a => a with { Status = AssignmentStatus.Expired });
        return Task.FromResult(overdue);
    }

    public Task<NetworkDiagnosticAssignment> GetAsync(string id) => Task.FromResult(Rows.FirstOrDefault(a => a.Id == id));

    public Task InsertManyAsync(IReadOnlyCollection<NetworkDiagnosticAssignment> assignments) => throw new NotSupportedException();
    public Task<bool> TryInsertAsync(NetworkDiagnosticAssignment assignment) => throw new NotSupportedException();
    public Task<bool> ExistsAsync(string runId, string battleTag) => throw new NotSupportedException();
    public Task<List<NetworkDiagnosticAssignment>> ForRunAsync(string runId) => throw new NotSupportedException();
    public Task<List<string>> AssignedBattleTagsAsync(string runId) => throw new NotSupportedException();
    public Task<int> CountPendingUndeliveredAsync(string runId, IReadOnlyCollection<string> battleTags, DateTime now) => throw new NotSupportedException();
    public Task<NetworkDiagnosticAssignment> TrySubmitAsync(string id, string battleTag, DateTime now) => throw new NotSupportedException();
}
