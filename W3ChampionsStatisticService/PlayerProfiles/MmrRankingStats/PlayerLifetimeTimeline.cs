using System;
using System.Collections.Generic;
using System.Linq;
using W3C.Contracts.GameObjects;
using W3C.Contracts.Matchmaking;

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
        var byRace = seasonTimelines
            .Where(x => x.Timeline?.MmrRpAtDates is { Count: > 0 })
            .GroupBy(x => x.Race);

        var lifetime = new PlayerLifetimeTimeline { GameMode = gameMode };

        foreach (var raceGroup in byRace)
        {
            // A player can appear on both gateways in the seasons that had them,
            // so order by date across the whole set rather than by season.
            var entries = raceGroup
                .SelectMany(x => x.Timeline.MmrRpAtDates.Select(entry => (Season: x.Season, SchemaVersion: x.Timeline.SchemaVersion, Entry: entry)))
                .OrderBy(x => x.Entry.Date)
                .ToList();

            lifetime.Series.Add(new LifetimeRaceSeries
            {
                Race = raceGroup.Key,
                Points = entries.Select(x => new LifetimePoint
                {
                    Date = x.Entry.Date,
                    Mmr = x.Entry.Mmr,
                    Rp = x.Entry.Rp,
                    Games = x.Entry.GamesOrDefault(x.SchemaVersion),
                }).ToList(),
                Peak = FindPeak(entries),
            });
        }

        lifetime.Series = [.. lifetime.Series.OrderBy(s => s.Race)];
        lifetime.Seasons = BuildSeasons(seasonTimelines);
        return lifetime;
    }

    /// <summary>
    /// The highest rating the player held once the system was confident in it.
    ///
    /// Null when any of the underlying documents predates the fields this needs.
    /// Reporting an ungated peak for those would be worse than reporting none:
    /// ratings start at 1500, so a player whose true rating is below that would
    /// show 1500 as their lifetime best forever.
    /// </summary>
    private static LifetimePeak FindPeak(List<(int Season, int SchemaVersion, MmrRpAtDate Entry)> entries)
    {
        if (entries.Any(x => x.SchemaVersion < PlayerMmrRpTimeline.CurrentSchemaVersion))
        {
            return null;
        }

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
public record SeasonTimeline(int Season, Race Race, PlayerMmrRpTimeline Timeline);

public class LifetimeRaceSeries
{
    public Race Race { get; set; }
    public List<LifetimePoint> Points { get; set; } = [];

    /// <summary>Null when the history is too old to tell placement games apart.</summary>
    public LifetimePeak Peak { get; set; }
}

public class LifetimePoint
{
    public DateTimeOffset Date { get; set; }
    public int Mmr { get; set; }
    public double? Rp { get; set; }

    /// <summary>Null on entries recorded before games were counted.</summary>
    public int? Games { get; set; }
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
