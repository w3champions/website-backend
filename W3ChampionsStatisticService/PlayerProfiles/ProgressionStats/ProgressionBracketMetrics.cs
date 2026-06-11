using System;
using System.Collections.Generic;
using System.Linq;
using Prometheus;
using W3C.Contracts.Matchmaking;

namespace W3ChampionsStatisticService.PlayerProfiles.ProgressionStats;

// Prometheus gauges for the progression ladder's bracket populations. Refreshed wholesale by
// ProgressionBracketMetricsService; Publish replaces the full series set each pass so brackets
// that empty out (or a season rollover) drop their stale series instead of freezing.
public static class ProgressionBracketMetrics
{
    private static readonly Gauge BracketCount = Metrics.CreateGauge(
        "progression_bracket_count",
        "Ladder entries per progression bracket (current season), by game mode, league and division.",
        new GaugeConfiguration { LabelNames = new[] { "gameMode", "league", "division" } });

    private static readonly Gauge RankedTotal = Metrics.CreateGauge(
        "progression_ranked_total",
        "Ladder entries with a placed progression rank (current season), by game mode.",
        new GaugeConfiguration { LabelNames = new[] { "gameMode" } });

    public static void Publish(IReadOnlyCollection<ProgressionBracketCount> counts)
    {
        var freshBrackets = counts
            .Select(c => new[] { GameModeLabel(c.GameMode), LeagueLabel(c.League), DivisionLabel(c.Division) })
            .ToHashSet(StringArrayComparer.Instance);
        foreach (var stale in BracketCount.GetAllLabelValues().Where(l => !freshBrackets.Contains(l)).ToList())
        {
            BracketCount.RemoveLabelled(stale);
        }
        foreach (var c in counts)
        {
            BracketCount.WithLabels(GameModeLabel(c.GameMode), LeagueLabel(c.League), DivisionLabel(c.Division)).Set(c.Count);
        }

        var totals = counts
            .GroupBy(c => GameModeLabel(c.GameMode))
            .ToDictionary(g => g.Key, g => g.Sum(c => c.Count));
        foreach (var stale in RankedTotal.GetAllLabelValues().Where(l => !totals.ContainsKey(l[0])).ToList())
        {
            RankedTotal.RemoveLabelled(stale);
        }
        foreach (var (mode, total) in totals)
        {
            RankedTotal.WithLabels(mode).Set(total);
        }
    }

    private static string GameModeLabel(GameMode gameMode) => gameMode.ToString();

    private static string LeagueLabel(int league) =>
        Enum.IsDefined(typeof(ProgressionLeague), league) ? ((ProgressionLeague)league).ToString() : league.ToString();

    private static string DivisionLabel(int? division) => division?.ToString() ?? "";

    private sealed class StringArrayComparer : IEqualityComparer<string[]>
    {
        public static readonly StringArrayComparer Instance = new();
        public bool Equals(string[] x, string[] y) => x != null && y != null && x.SequenceEqual(y);
        public int GetHashCode(string[] obj) => string.Join("", obj).GetHashCode();
    }
}
