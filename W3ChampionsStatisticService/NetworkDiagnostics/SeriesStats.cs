using System;
using System.Collections.Generic;
using System.Linq;
using MongoDB.Bson.Serialization.Attributes;

namespace W3ChampionsStatisticService.NetworkDiagnostics;

/// <summary>One probe result: offset from window start (ms) and RTT (µs, null = lost).</summary>
public readonly record struct ProbeSample(uint OffsetMs, uint? RttUs);

/// <summary>C6 StatsDto. All *Ms are float ms; null when no sample succeeded. LossPct null when nothing was sent.</summary>
[BsonIgnoreExtraElements]
public record StatsDto
{
    public int Sent { get; init; }
    public int Received { get; init; }
    public double? LossPct { get; init; }
    public double? LastMs { get; init; }
    public double? MinMs { get; init; }
    public double? AvgMs { get; init; }
    public double? MaxMs { get; init; }
    public double? StddevMs { get; init; }
    public double? P50Ms { get; init; }
    public double? P90Ms { get; init; }
    public double? P95Ms { get; init; }
    public double? P99Ms { get; init; }
    public double? JitterMs { get; init; }
}

public static class SeriesStats
{
    public static StatsDto Compute(IReadOnlyList<ProbeSample> samples)
    {
        var ordered = samples.OrderBy(s => s.OffsetMs).ToList();
        var rtts = ordered.Where(s => s.RttUs.HasValue).Select(s => s.RttUs!.Value / 1000.0).ToList();
        var sent = ordered.Count;
        var received = rtts.Count;
        double? lossPct = sent == 0 ? null : Math.Round((sent - received) * 100.0 / sent, 2);
        if (received == 0)
        {
            return new StatsDto { Sent = sent, Received = 0, LossPct = lossPct };
        }

        var sorted = rtts.OrderBy(x => x).ToList();
        var avg = rtts.Average();
        var stddev = Math.Sqrt(rtts.Sum(x => (x - avg) * (x - avg)) / received);
        double? jitter = received < 2
            ? null
            : Enumerable.Range(1, received - 1).Average(i => Math.Abs(rtts[i] - rtts[i - 1]));

        return new StatsDto
        {
            Sent = sent,
            Received = received,
            LossPct = lossPct,
            LastMs = Round(rtts[^1]),
            MinMs = Round(sorted[0]),
            AvgMs = Round(avg),
            MaxMs = Round(sorted[^1]),
            StddevMs = Round(stddev),
            P50Ms = Round(NearestRank(sorted, 50)),
            P90Ms = Round(NearestRank(sorted, 90)),
            P95Ms = Round(NearestRank(sorted, 95)),
            P99Ms = Round(NearestRank(sorted, 99)),
            JitterMs = Round(jitter),
        };
    }

    /// <summary>Nearest-rank percentile: the value at rank ceil(p/100 * n) of an ascending list.</summary>
    public static double? NearestRank(IReadOnlyList<double> sortedAscending, int percent)
    {
        if (sortedAscending.Count == 0) return null;
        var rank = (percent * sortedAscending.Count + 99) / 100; // integer ceil, avoids 0.9*10 float error
        return sortedAscending[Math.Clamp(rank, 1, sortedAscending.Count) - 1];
    }

    private static double? Round(double? value) => value.HasValue ? Math.Round(value.Value, 3) : null;
}
