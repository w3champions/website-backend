using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using MongoDB.Bson;
using MongoDB.Driver;
using W3C.Contracts.GameObjects;
using W3C.Domain.CommonValueObjects;
using W3ChampionsStatisticService.Matches;

namespace W3ChampionsStatisticService.Admin.ReplayExport;

/// <summary>
/// Turns a <see cref="ReplayExportFilter"/> into a Matchup query.
///
/// Time filters (patch and dates) use <c>_id</c>, not StartTime/EndTime: a Matchup's _id is the ObjectId of
/// its MatchFinishedEvent, so its timestamp is the end of the match, and the _id index makes a patch-sized
/// window cheap. StartTime/EndTime have no index and are stored as [ticks, offset] arrays.
/// </summary>
public static class ReplayExportQuery
{
    private static readonly FilterDefinitionBuilder<Matchup> Match = Builders<Matchup>.Filter;
    private static readonly FilterDefinitionBuilder<Team> TeamFilter = Builders<Team>.Filter;
    private static readonly FilterDefinitionBuilder<PlayerOverviewMatches> Player = Builders<PlayerOverviewMatches>.Filter;

    /// <summary>
    /// One option per patch version. A version can have more than one window, for example when a patch is
    /// rolled back and the old version gets a new Patch entry.
    /// </summary>
    public static List<ReplayExportPatchOption> PatchOptions(IEnumerable<Patch> patches)
    {
        var ordered = patches
            .Where(p => !string.IsNullOrWhiteSpace(p.Version))
            .OrderBy(p => p.StartDate)
            .ToList();

        var options = new Dictionary<string, ReplayExportPatchOption>();
        string previousVersion = null;
        for (var i = 0; i < ordered.Count; i++)
        {
            var version = ordered[i].Version;
            var window = new ReplayExportPatchWindow
            {
                From = AsUtc(ordered[i].StartDate),
                To = i + 1 < ordered.Count ? AsUtc(ordered[i + 1].StartDate) : null,
            };

            if (!options.TryGetValue(version, out var option))
            {
                option = new ReplayExportPatchOption { Version = version };
                options[version] = option;
            }

            // Two entries in a row for the same version are one window.
            if (version == previousVersion)
            {
                option.Windows[^1].To = window.To;
            }
            else
            {
                option.Windows.Add(window);
            }
            previousVersion = version;
        }

        return options.Values
            .OrderByDescending(o => o.Windows.Max(w => w.From))
            .ToList();
    }

    public static string PatchAt(IEnumerable<ReplayExportPatchOption> patches, DateTimeOffset time)
    {
        var utc = time.UtcDateTime;
        return patches
            .FirstOrDefault(p => p.Windows.Any(w => utc >= w.From && (w.To == null || utc < w.To)))
            ?.Version;
    }

    /// <summary>
    /// The time windows the filter allows: the patch windows, cut to From/To. An empty list means that no
    /// game can match (for example an unknown patch, or dates outside the patch).
    /// </summary>
    private static List<(DateTime? From, DateTime? To)> TimeWindows(
        ReplayExportFilter filter,
        IEnumerable<ReplayExportPatchOption> patches)
    {
        List<(DateTime? From, DateTime? To)> windows = string.IsNullOrWhiteSpace(filter.Patch)
            ? [(null, null)]
            : patches
                .FirstOrDefault(p => p.Version == filter.Patch)
                ?.Windows.Select(w => ((DateTime?)w.From, w.To)).ToList() ?? [];

        var from = filter.From?.UtcDateTime;
        var to = filter.To?.UtcDateTime;

        return windows
            .Select(w => (From: Later(w.From, from), To: Earlier(w.To, to)))
            .Where(w => w.From == null || w.To == null || w.From < w.To)
            .ToList();
    }

    /// <param name="playerTextSearch">
    /// True when Matchup has the text index on Team1Players–Team4Players (see
    /// <see cref="ReplayExportRepository.HasPlayerTextIndex"/>). The player filter then uses it to find the
    /// player's games. Without it, the filter reads every game that the other filters allow.
    /// </param>
    public static FilterDefinition<Matchup> Build(
        ReplayExportFilter filter,
        IEnumerable<ReplayExportPatchOption> patches,
        IReadOnlyCollection<ObjectId> excludedIds = null,
        bool playerTextSearch = false)
    {
        // Only FLO games have a replay.
        var filters = new List<FilterDefinition<Matchup>>
        {
            Match.Eq(m => m.GameMode, filter.GameMode),
            Match.Gt(m => m.FloMatchId, 0),
        };

        var windows = TimeWindows(filter, patches);
        if (windows.Count == 0)
        {
            return Match.In(m => m.Id, Array.Empty<ObjectId>());
        }
        if (windows.Any(w => w.From != null || w.To != null))
        {
            filters.Add(Match.Or(windows.Select(IdWindow)));
        }

        if (filter.Seasons is { Count: > 0 } seasons)
        {
            filters.Add(Match.In(m => m.Season, seasons));
        }

        if (excludedIds is { Count: > 0 })
        {
            filters.Add(Match.Nin(m => m.Id, excludedIds));
        }

        if (filter.GateWay is { } gateWay)
        {
            filters.Add(Match.Eq(m => m.GateWay, gateWay));
        }

        if (!string.IsNullOrWhiteSpace(filter.MapName))
        {
            filters.Add(Match.Eq(m => m.MapName, filter.MapName));
        }

        if (!string.IsNullOrWhiteSpace(filter.BattleTag))
        {
            var tag = filter.BattleTag.Trim();
            // The text search finds the games whose player lists contain the tag (any case); the regex keeps
            // only the games with exactly this player, not "xName#1234" or "Name#12345".
            if (playerTextSearch) filters.Add(Match.Text(PlayerPhrase(tag)));
            var exactTag = new BsonRegularExpression($"^{Regex.Escape(tag)}$", "i");
            filters.Add(HasPlayer(Player.Regex(p => p.BattleTag, exactTag)));
        }

        if (filter.MinMmr.HasValue || filter.MaxMmr.HasValue)
        {
            var inRange = Player.Ne(p => p.OldMmr, null);
            if (filter.MinMmr is { } minMmr) inRange &= Player.Gte(p => p.OldMmr, minMmr);
            if (filter.MaxMmr is { } maxMmr) inRange &= Player.Lte(p => p.OldMmr, maxMmr);

            filters.Add(filter.MmrMode == ReplayExportMmrMode.AnyPlayer
                ? HasPlayer(inRange)
                : Match.Not(HasPlayer(Player.Not(inRange))));
        }

        filters.AddRange(RaceFilters(filter.Race, filter.OpponentRace));

        if (filter.MinDurationSeconds is { } minDuration)
        {
            filters.Add(Match.Gte(m => m.Duration, TimeSpan.FromSeconds(minDuration)));
        }
        if (filter.MaxDurationSeconds is { } maxDuration)
        {
            filters.Add(Match.Lte(m => m.Duration, TimeSpan.FromSeconds(maxDuration)));
        }

        return Match.And(filters);
    }

    /// <summary>
    /// One race: some player played it. Two different races: one player played each. The same race twice
    /// (a mirror): every player played it.
    /// </summary>
    private static IEnumerable<FilterDefinition<Matchup>> RaceFilters(Race? race, Race? opponentRace)
    {
        var races = new[] { race, opponentRace }.Where(r => r.HasValue).Select(r => r.Value).ToList();
        if (races.Count == 2 && races[0] == races[1])
        {
            yield return HasPlayer(PlayedRace(races[0]));
            yield return Match.Not(HasPlayer(Player.Not(PlayedRace(races[0]))));
            yield break;
        }

        foreach (var r in races)
        {
            yield return HasPlayer(PlayedRace(r));
        }
    }

    /// <summary>Random players keep Race = RnD and get the race they played in RndRace.</summary>
    private static FilterDefinition<PlayerOverviewMatches> PlayedRace(Race race) => race == Race.RnD
        ? Player.Eq(p => p.Race, Race.RnD)
        : Player.Or(Player.Eq(p => p.Race, race), Player.Eq(p => p.RndRace, race));

    /// <summary>A $text phrase: the whole tag, in this order. A BattleTag has no quotes or backslashes.</summary>
    public static string PlayerPhrase(string battleTag) => $"\"{battleTag.Replace("\"", "").Replace("\\", "")}\"";

    private static FilterDefinition<Matchup> HasPlayer(FilterDefinition<PlayerOverviewMatches> player) =>
        Match.ElemMatch(m => m.Teams, TeamFilter.ElemMatch(t => t.Players, player));

    private static FilterDefinition<Matchup> IdWindow((DateTime? From, DateTime? To) window)
    {
        var filters = new List<FilterDefinition<Matchup>>();
        if (window.From is { } from) filters.Add(Match.Gte(m => m.Id, FirstObjectIdAt(from)));
        if (window.To is { } to) filters.Add(Match.Lt(m => m.Id, FirstObjectIdAt(to)));
        return Match.And(filters);
    }

    /// <summary>The smallest ObjectId with the timestamp of <paramref name="time"/> (to the second).</summary>
    public static ObjectId FirstObjectIdAt(DateTime time)
    {
        var seconds = Math.Clamp(new DateTimeOffset(AsUtc(time)).ToUnixTimeSeconds(), 0, uint.MaxValue);
        var bytes = new byte[12];
        bytes[0] = (byte)(seconds >> 24);
        bytes[1] = (byte)(seconds >> 16);
        bytes[2] = (byte)(seconds >> 8);
        bytes[3] = (byte)seconds;
        return new ObjectId(bytes);
    }

    private static DateTime AsUtc(DateTime time) => time.Kind switch
    {
        DateTimeKind.Utc => time,
        DateTimeKind.Local => time.ToUniversalTime(),
        _ => DateTime.SpecifyKind(time, DateTimeKind.Utc),
    };

    private static DateTime? Later(DateTime? a, DateTime? b) => a == null ? b : b == null ? a : (a > b ? a : b);

    private static DateTime? Earlier(DateTime? a, DateTime? b) => a == null ? b : b == null ? a : (a < b ? a : b);
}
