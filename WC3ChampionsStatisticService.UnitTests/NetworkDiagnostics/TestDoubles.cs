using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using W3ChampionsStatisticService.NetworkDiagnostics;

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

    public void Dispose() => Disposed = true;
}
