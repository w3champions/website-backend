using System;
using System.Collections.Generic;
using System.Linq;

namespace W3ChampionsStatisticService.NetworkDiagnostics.Runs;

/// <summary>Pure fleet decisions (spec §5.2). Randomness and time are injected.</summary>
public static class AudiencePlanner
{
    public static List<string> PickInitial(RunAudience audience, IReadOnlyList<string> online, IRandom random) => audience.Kind switch
    {
        AudienceKind.All => online.ToList(),
        AudienceKind.SamplePercent => online.Where(_ => random.NextDouble() < audience.Percent!.Value / 100.0).ToList(),
        AudienceKind.SampleCount => PickRandom(online, audience.Count!.Value, random),
        _ => [],
    };

    /// <summary>Partial Fisher-Yates on a copy: n distinct candidates, uniformly.</summary>
    public static List<string> PickRandom(IReadOnlyList<string> candidates, int n, IRandom random)
    {
        var pool = candidates.ToList();
        var take = Math.Min(Math.Max(n, 0), pool.Count);
        for (var i = 0; i < take; i++)
        {
            var j = i + random.Next(pool.Count - i);
            (pool[i], pool[j]) = (pool[j], pool[i]);
        }
        return pool.Take(take).ToList();
    }

    public static DateTime RandomSlot(DateTime now, DateTime windowEnd, IRandom random) =>
        windowEnd <= now ? now : now + TimeSpan.FromTicks((long)((windowEnd - now).Ticks * random.NextDouble()));

    public static bool ShouldAssignLateJoiner(NetworkDiagnosticRun run, DateTime now, IRandom random)
    {
        if (!run.IsFleet || run.CancelledAt != null || run.WindowEnd is not { } end || now >= end) return false;
        if (run.WindowStart is { } start && now < start) return false;
        return run.Audience.Kind switch
        {
            AudienceKind.All => true,
            AudienceKind.SamplePercent => random.NextDouble() < run.Audience.Percent!.Value / 100.0,
            AudienceKind.SampleCount => run.Counters.Delivered < run.Audience.Count!.Value,
            _ => false,
        };
    }

    public static int TopUpNeed(int count, int delivered, int pendingUndeliveredConnected) =>
        Math.Max(0, count - delivered - pendingUndeliveredConnected);
}
