using System;
using System.Collections.Generic;
using System.Linq;
using W3C.Contracts.GameObjects;
using W3C.Contracts.Matchmaking;
using W3C.Domain.GameModes;

namespace W3ChampionsStatisticService.PlayerProfiles.MmrRankingStats;

/// <summary>
/// A player's rating history for one game mode across every season they played.
///
/// This is a response shape rather than a stored one. The per-season timelines
/// it is built from carry an Rd that must never leave the service, so the two
/// are kept as separate types instead of annotating the stored one and hoping
/// nobody removes the annotation.
/// </summary>
public class PlayerLifetimeTimeline
{
    public GameMode GameMode { get; set; }
    public List<LifetimeRaceSeries> Series { get; set; } = [];
    public List<LifetimeSeason> Seasons { get; set; } = [];

    /// <summary>
    /// Assembles the per-season, per-race timelines into one series per race.
    /// </summary>
    public static PlayerLifetimeTimeline Build(GameMode gameMode, IEnumerable<SeasonTimeline> seasonTimelines)
    {
        var withData = seasonTimelines.Where(x => x.Timeline?.MmrRpAtDates is { Count: > 0 });

        // Outside the race-split modes a player has one rating that follows them
        // across race changes, and the race a match was filed under is incidental.
        // Grouping by it there would cut one continuous history into unrelated
        // lines, each with its own bogus peak.
        var raceSplit = GameModesHelper.IsRaceSplitGameMode(gameMode);

        // Europe and America were separate ladders up to season 5, so a player who
        // ranked on both has two unrelated ratings for the same season and race.
        // Interleaving them by date draws one line zigzagging between two histories.
        // Split only when there is genuinely more than one, so the ordinary
        // single-gateway player still gets a single series.
        var gateWays = withData.Select(x => x.GateWay).Distinct().ToList();
        var splitByGateWay = gateWays.Count > 1;

        var grouped = withData.GroupBy(x => (
            Race: raceSplit ? x.Race : Race.Total,
            GateWay: splitByGateWay ? x.GateWay : gateWays.FirstOrDefault()));

        var lifetime = new PlayerLifetimeTimeline { GameMode = gameMode };

        foreach (var group in grouped)
        {
            var entries = group
                .SelectMany(x => x.Timeline.MmrRpAtDates.Select(entry => (Season: x.Season, Entry: entry)))
                .OrderBy(x => x.Entry.Date)
                .ToList();

            lifetime.Series.Add(new LifetimeRaceSeries
            {
                Race = group.Key.Race,
                GateWay = group.Key.GateWay,
                Points = entries.Select(x => new LifetimePoint
                {
                    Date = x.Entry.Date,
                    Mmr = x.Entry.Mmr,
                    Rp = x.Entry.Rp,
                    Games = x.Entry.GamesOrDefault,
                }).ToList(),
                Peak = FindPeak(entries),
            });
        }

        lifetime.Series = [.. lifetime.Series.OrderBy(s => s.Race).ThenBy(s => s.GateWay)];
        lifetime.Seasons = BuildSeasons(seasonTimelines);
        return lifetime;
    }

    /// <summary>
    /// The highest rating the player held once the system was confident in it.
    ///
    /// Placement entries are excluded via <see cref="MmrRpAtDate.WasCalibrating"/>,
    /// which reads the stored Rd. Entries written before Rd was recorded have none and
    /// so read as settled - they can therefore contribute a placement-era rating to the
    /// peak. The backfill rebuilds every day from the events and restores Rd, so this
    /// only affects days a completed run never reached; the tab is not shown until it
    /// has run.
    ///
    /// Null when the player has no settled entries at all.
    /// </summary>
    private static LifetimePeak FindPeak(List<(int Season, MmrRpAtDate Entry)> entries)
    {
        var settled = entries.Where(x => !x.Entry.WasCalibrating).ToList();
        if (settled.Count == 0) return null;

        var best = settled.MaxBy(x => x.Entry.PeakMmr);
        return new LifetimePeak
        {
            Mmr = best.Entry.PeakMmr,
            Date = best.Entry.Date,
            Season = best.Season,
        };
    }

    /// <summary>
    /// When each season ran, taken from the data itself. Season carries only an
    /// id, so there is nowhere else to get this from.
    /// </summary>
    private static List<LifetimeSeason> BuildSeasons(IEnumerable<SeasonTimeline> seasonTimelines)
    {
        return seasonTimelines
            .Where(x => x.Timeline?.MmrRpAtDates is { Count: > 0 })
            .GroupBy(x => x.Season)
            .Select(g => new LifetimeSeason
            {
                Season = g.Key,
                Start = g.Min(x => x.Timeline.MmrRpAtDates.First().Date),
                End = g.Max(x => x.Timeline.MmrRpAtDates.Last().Date),
            })
            .OrderBy(s => s.Season)
            .ToList();
    }
}

/// <summary>One stored timeline together with the coordinates it was loaded for.</summary>
public record SeasonTimeline(int Season, Race Race, GateWay GateWay, PlayerMmrRpTimeline Timeline);

public class LifetimeRaceSeries
{
    public Race Race { get; set; }

    /// <summary>
    /// Which ladder this line belongs to. Up to and including season 5 Europe and
    /// America were separate ratings, so a player active on both has two unrelated
    /// histories and gets one series each. Everyone else gets a single series.
    /// </summary>
    public GateWay GateWay { get; set; }
    public List<LifetimePoint> Points { get; set; } = [];

    /// <summary>Null when the player has no settled entries in this series.</summary>
    public LifetimePeak Peak { get; set; }
}

public class LifetimePoint
{
    public DateTimeOffset Date { get; set; }
    public int Mmr { get; set; }
    public double? Rp { get; set; }

    /// <summary>Games played that day.</summary>
    public int Games { get; set; }
}

public class LifetimePeak
{
    public int Mmr { get; set; }
    public DateTimeOffset Date { get; set; }
    public int Season { get; set; }
}

public class LifetimeSeason
{
    public int Season { get; set; }
    public DateTimeOffset Start { get; set; }
    public DateTimeOffset End { get; set; }
}
