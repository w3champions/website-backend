using System;
using System.Collections.Generic;
using System.Linq;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using W3C.Contracts.GameObjects;
using W3C.Contracts.Matchmaking;
using W3C.Domain.CommonValueObjects;
using W3C.Domain.Repositories;

namespace W3ChampionsStatisticService.PlayerProfiles.ProgressionStats;

// Permanent, monotonic per-entity/mode/race lifetime win-milestone track. One document per
// (entity, gateway, gameMode, race?) — season-less, so wins accumulate across all seasons.
// totalWins counts wins only; activityWeeks counts every game (won or lost) in a rolling
// ~90-day window, pre-computed on write so the on-read target calculator needs no history query.
public class ProgressionMilestone : IIdentifiable
{
    public const int RecentWindowDays = 90;

    public List<PlayerId> PlayerIds { get; set; }
    public GateWay GateWay { get; set; }
    public GameMode GameMode { get; set; }
    public Race? Race { get; set; }

    public long TotalWins { get; set; }
    public List<ActivityWeek> ActivityWeeks { get; set; } = new();

    [BsonRepresentation(BsonType.Array)]
    public DateTimeOffset LastPlayedAt { get; set; }

    public string Id => BuildId(PlayerIds, GateWay, GameMode, Race);

    public static string BuildId(List<PlayerId> playerIds, GateWay gateWay, GameMode gameMode, Race? race)
    {
        var key = string.Join("_", playerIds.OrderBy(t => t.BattleTag).Select(b => $"{b.BattleTag}@{(int)gateWay}"));
        var id = $"{key}_{gameMode}";
        if (race != null)
        {
            id += $"_{race}";
        }
        return id;
    }

    public static ProgressionMilestone Create(List<PlayerId> playerIds, GateWay gateWay, GameMode gameMode, Race? race)
    {
        return new ProgressionMilestone
        {
            PlayerIds = playerIds,
            GateWay = gateWay,
            GameMode = gameMode,
            Race = race,
        };
    }

    public void RecordWin() => TotalWins++;

    public void RecordActivity(DateTimeOffset playedAt)
    {
        var weekStart = StartOfIsoWeek(playedAt);
        var bucket = ActivityWeeks.FirstOrDefault(w => w.WeekStartUtc == weekStart);
        if (bucket == null)
        {
            bucket = new ActivityWeek { WeekStartUtc = weekStart };
            ActivityWeeks.Add(bucket);
        }
        bucket.Games++;
        if (playedAt > LastPlayedAt)
        {
            LastPlayedAt = playedAt;
        }
    }

    public void PruneActivityBefore(DateTimeOffset cutoff)
    {
        var cutoffWeek = StartOfIsoWeek(cutoff);
        ActivityWeeks.RemoveAll(w => w.WeekStartUtc < cutoffWeek);
    }

    public MilestoneActivity ActivityIn(DateTimeOffset now)
    {
        var windowStart = StartOfIsoWeek(now.AddDays(-RecentWindowDays));
        var inWindow = ActivityWeeks.Where(w => w.WeekStartUtc >= windowStart).ToList();
        return new MilestoneActivity(inWindow.Sum(w => w.Games), inWindow.Count(w => w.Games > 0));
    }

    // Monday 00:00 UTC of the ISO week containing the instant.
    public static DateTimeOffset StartOfIsoWeek(DateTimeOffset instant)
    {
        var day = instant.UtcDateTime.Date;
        var diff = ((int)day.DayOfWeek + 6) % 7; // Monday = 0
        return new DateTimeOffset(day.AddDays(-diff), TimeSpan.Zero);
    }
}

public class ActivityWeek
{
    [BsonRepresentation(BsonType.Array)]
    public DateTimeOffset WeekStartUtc { get; set; }
    public int Games { get; set; }
}

public readonly record struct MilestoneActivity(int RecentGames, int ActiveWeeks);
