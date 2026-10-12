using System;
using System.Collections.Generic;
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
