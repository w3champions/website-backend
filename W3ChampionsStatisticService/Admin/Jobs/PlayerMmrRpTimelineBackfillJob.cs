using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MongoDB.Bson;
using MongoDB.Driver;
using Serilog;
using W3C.Domain.MatchmakingService;
using W3ChampionsStatisticService.Matches;
using W3ChampionsStatisticService.PlayerProfiles.MmrRankingStats;

namespace W3ChampionsStatisticService.Admin.Jobs;

/// <summary>
/// Rebuilds <see cref="PlayerMmrRpTimeline"/> from the match events that produced it,
/// so historical entries carry the Rd, Games and DailyMaxMmr the live handler now
/// records. Until it runs, the lifetime endpoint withholds peaks for every player whose
/// history predates those fields, because it cannot tell a genuine peak from one set
/// while the player was still calibrating.
/// <para>
/// Rebuilding also corrects the entries themselves. The old handler kept only a day's
/// last game and discarded the rest, so days where a player did not finish on their
/// high point will move. That is intended: the alternative leaves each timeline half
/// computed one way and half the other.
/// </para>
/// </summary>
public class PlayerMmrRpTimelineBackfillJob(MongoClient mongoClient) : IAdminJob
{
    public string Key => "timeline-backfill";

    public string Name => "MMR timeline backfill";

    public string Description =>
        "Recomputes the MMR/RP timeline from match history so lifetime peaks can be shown. " +
        "Rewrites historical entries, which shifts some existing charts. Resumable; runs for hours. " +
        "Covers everything up to yesterday (UTC) - do not run this on the same UTC day the " +
        "timeline handler was deployed, or that day's part-old entries will be marked as rebuilt.";

    public bool RequiresConfirmation => true;

    /// <summary>
    /// Matches are inserted when they finish, but not instantaneously and not in a
    /// guaranteed order, so a day's <c>_id</c> window is widened at both ends and the
    /// exact day is then selected on <c>endTime</c>.
    /// </summary>
    /// <summary>
    /// Slack around a day's <c>_id</c> range, covering the gap between a match ending
    /// and its event being written. That is normally seconds; two hours is generous.
    ///
    /// It does NOT cover a match whose event carries an end time far older than its
    /// insert - which matchmaking's healMatches produces, since it finishes a match
    /// using the result log's timestamp and can run up to ~2.5 days later. Those
    /// matches are missed by both the day they belong to and the day they were written
    /// on. Accepted deliberately: healMatches has been inoperative since 2025-06-14
    /// (see matchmaking-service#869), so the affected set is small and historical, and
    /// closing the gap properly means an index on endTime that nothing else needs.
    /// Revisit if healMatches is repaired rather than removed.
    /// </summary>
    private static readonly TimeSpan IdRangePadding = TimeSpan.FromHours(2);

    private const string CheckpointField = "nextDay";

    private IMongoDatabase Database => mongoClient.GetDatabase("W3Champions-Statistic-Service");
    private IMongoCollection<MatchFinishedEvent> Events => Database.GetCollection<MatchFinishedEvent>(nameof(MatchFinishedEvent));
    private IMongoCollection<PlayerMmrRpTimeline> Timelines => Database.GetCollection<PlayerMmrRpTimeline>(nameof(PlayerMmrRpTimeline));

    public async Task RunAsync(IAdminJobContext context, CancellationToken cancellationToken)
    {
        var firstDay = await FindFirstDay(cancellationToken);
        if (firstDay == null)
        {
            await context.Report(0, 0, "No match events to rebuild from");
            return;
        }

        // Yesterday relative to whenever this runs, not to any deploy: today is still
        // being written by the live handler, and rewriting a day underneath it would
        // drop games that land mid-run.
        //
        // The corollary is operational: run this on a later UTC day than the one the
        // timeline handler was deployed on. Run it on the same day and the entries the
        // OLD handler wrote earlier that day are never rebuilt, yet the document is
        // still promoted to the current schema version, claiming they were.
        var lastDay = DateOnly.FromDateTime(DateTime.UtcNow.Date).AddDays(-1);
        var day = ReadCheckpoint(context) ?? firstDay.Value;

        var totalDays = Math.Max(lastDay.DayNumber - firstDay.Value.DayNumber + 1, 1);
        Log.Information("Timeline backfill covering {FirstDay} to {LastDay}, resuming at {Day}", firstDay, lastDay, day);

        while (day <= lastDay)
        {
            var timelines = await RebuildDay(day, cancellationToken);

            context.AddItems(timelines);
            await context.Report(
                current: day.DayNumber - firstDay.Value.DayNumber + 1,
                total: totalDays,
                message: $"Rebuilt {day:yyyy-MM-dd}",
                // The checkpoint is the next day to do, so a resume never redoes a day
                // it already finished - and redoing one would be harmless anyway, since
                // a day is rebuilt from scratch rather than merged into.
                checkpoint: new BsonDocument(CheckpointField, day.AddDays(1).DayNumber));

            await context.Pace(cancellationToken);
            day = day.AddDays(1);
        }

    }

    /// <summary>How many times to redo a day whose documents moved underneath it.</summary>
    private const int MaxRebuildAttempts = 5;

    /// <summary>
    /// Rebuilds every timeline that has a match on this day, and returns how many were
    /// written.
    /// <para>
    /// The live handler writes these same documents while this job runs, and both
    /// replace them whole - so every write is conditional on the revision it was read
    /// at. On a clash the whole day is redone rather than the individual document
    /// retried: rebuilding a day is idempotent (the day is cleared and re-derived from
    /// the events), so redoing it is both simpler and exactly equivalent.
    /// </para>
    /// </summary>
    private async Task<int> RebuildDay(DateOnly day, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            var written = await TryRebuildDay(day, cancellationToken);
            if (written.HasValue)
            {
                return written.Value;
            }

            if (attempt >= MaxRebuildAttempts)
            {
                throw new InvalidOperationException(
                    $"Could not rebuild {day:yyyy-MM-dd} after {MaxRebuildAttempts} attempts; its timelines kept being written concurrently.");
            }

            Log.Information("Timelines for {Day} changed while rebuilding, redoing the day ({Attempt}/{MaxAttempts})",
                day, attempt, MaxRebuildAttempts);
        }
    }

    /// <summary>
    /// One rebuild pass. Returns the number of documents written, or null if any of
    /// them had moved since it was read.
    /// </summary>
    private async Task<int?> TryRebuildDay(DateOnly day, CancellationToken cancellationToken)
    {
        var entries = await CollectDay(day, cancellationToken);
        if (entries.Count == 0)
        {
            return 0;
        }

        var existing = await LoadTimelines(entries.Keys, cancellationToken);
        var writes = new List<WriteModel<PlayerMmrRpTimeline>>(entries.Count);
        var inserts = 0;

        foreach (var (id, rebuilt) in entries)
        {
            var current = existing.GetValueOrDefault(id);
            var timeline = current ?? rebuilt.NewTimeline();

            // Drop whatever the day previously held before inserting the rebuilt entry,
            // so a rerun converges instead of double-counting games.
            timeline.MmrRpAtDates.RemoveAll(e => e.HasSameYearMonthDayAs(rebuilt.Entry));
            timeline.UpdateTimeline(rebuilt.Entry);

            if (current == null)
            {
                // Not an upsert: a document appearing between the read and the write is
                // a conflict, not something to overwrite. The duplicate key surfaces it.
                timeline.Revision = 1;
                writes.Add(new InsertOneModel<PlayerMmrRpTimeline>(timeline));
                inserts++;
                continue;
            }

            var expectedRevision = current.Revision;
            timeline.Revision = expectedRevision + 1;
            writes.Add(new ReplaceOneModel<PlayerMmrRpTimeline>(
                Builders<PlayerMmrRpTimeline>.Filter.And(
                    Builders<PlayerMmrRpTimeline>.Filter.Eq(t => t.Id, id),
                    Builders<PlayerMmrRpTimeline>.Filter.Eq(t => t.Revision, expectedRevision)),
                timeline));
        }

        try
        {
            var result = await Timelines.BulkWriteAsync(writes, new BulkWriteOptions { IsOrdered = false }, cancellationToken);

            // A replace that matched nothing means that document's revision moved.
            return result.MatchedCount + result.InsertedCount == writes.Count ? writes.Count : null;
        }
        catch (MongoBulkWriteException<PlayerMmrRpTimeline> e)
            when (e.WriteErrors.All(w => w.Category == ServerErrorCategory.DuplicateKey))
        {
            // Somebody created a timeline we had read as absent.
            return null;
        }
    }

    /// <summary>
    /// Reads one day of match events and folds them into one entry per timeline.
    /// <para>
    /// Ranged on <c>_id</c> rather than <c>endTime</c>: an ObjectId leads with its
    /// creation timestamp, so the always-present <c>_id</c> index serves this without
    /// needing an index on <c>endTime</c>. Every match is read once, in order, and
    /// never looked up by player - which is why this needs no index on
    /// <c>Teams.Players.BattleTag</c> either.
    /// </para>
    /// </summary>
    private async Task<Dictionary<string, RebuiltDay>> CollectDay(DateOnly day, CancellationToken cancellationToken)
    {
        var dayStart = new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var dayEnd = dayStart.AddDays(1);

        var filter = Builders<MatchFinishedEvent>.Filter.And(
            Builders<MatchFinishedEvent>.Filter.Gte(e => e.Id, ObjectIdAt(dayStart - IdRangePadding)),
            Builders<MatchFinishedEvent>.Filter.Lt(e => e.Id, ObjectIdAt(dayEnd + IdRangePadding)));

        var entries = new Dictionary<string, RebuiltDay>();

        using var cursor = await Events.FindAsync(filter, new FindOptions<MatchFinishedEvent>
        {
            // The result payload dwarfs what we need - heroes, pings, server info - and
            // this reads the whole of match history.
            Projection = Builders<MatchFinishedEvent>.Projection
                .Include(e => e.Id)
                .Include("match.players")
                .Include("match.endTime")
                .Include("match.state")
                .Include("match.season")
                .Include("match.gameMode")
                .Include("match.gateway"),
            BatchSize = 500,
        }, cancellationToken);

        while (await cursor.MoveNextAsync(cancellationToken))
        {
            foreach (var finished in cursor.Current)
            {
                var match = finished?.match;
                if (match?.players == null || match.endTime == 0)
                {
                    continue;
                }

                // Cancelled matches are not this job's business, exactly as they are not
                // MatchFinishedReadModelHandler's - it discards them before the timeline
                // handler runs, calling them "known and potentially frequent".
                //
                // Only CANCELED is rejected, deliberately. `state` was an untyped int
                // with no filter at all until #405 (2025-05-26), so events stored before
                // then WERE folded into timelines whatever their state, and an absent
                // field deserializes as INIT - indistinguishable from a real INIT. Since
                // RebuildDay clears a day before rewriting it, rejecting those would
                // delete history the handler of the day legitimately wrote. Old data is
                // accepted as-is; only the case the current handler would reject is.
                if (match.state == EMatchState.CANCELED)
                {
                    continue;
                }

                var endTime = DateTimeOffset.FromUnixTimeMilliseconds(match.endTime);
                if (endTime < dayStart || endTime >= dayEnd)
                {
                    // Swept in by the padding, belongs to a neighbouring day.
                    continue;
                }

                foreach (var player in match.players)
                {
                    // Same exclusions the live handler applies, so a rebuilt timeline
                    // matches what the handler would have produced.
                    //
                    // The null and empty-battleTag checks mirror the pipeline's
                    // StripComputerPlayers, which runs before the live handler. Without
                    // the null check this dereferences and fails the job, and because a
                    // resume retries the same day it would fail on that day forever.
                    if (player == null || string.IsNullOrEmpty(player.battleTag))
                    {
                        continue;
                    }
                    if (player.IsAt || player.updatedMmr == null)
                    {
                        continue;
                    }

                    var timeline = new PlayerMmrRpTimeline(player.battleTag, player.race, match.gateway, match.season, match.gameMode);
                    var entry = new MmrRpAtDate(
                        mmr: (int)player.updatedMmr.rating,
                        rp: player.ranking?.rp,
                        date: endTime,
                        rd: player.updatedMmr.rd >= PlayersObfuscator.RankDeviationObfuscationThreshold
                            ? player.updatedMmr.rd
                            : null);

                    if (entries.TryGetValue(timeline.Id, out var existing))
                    {
                        // Accumulates the games count and the intra-day peak - the whole
                        // reason the day is rebuilt rather than taking its last game.
                        existing.Entry.MergeSameDay(entry);
                    }
                    else
                    {
                        entries[timeline.Id] = new RebuiltDay(timeline, entry);
                    }
                }
            }
        }

        return entries;
    }

    private async Task<Dictionary<string, PlayerMmrRpTimeline>> LoadTimelines(
        IEnumerable<string> ids,
        CancellationToken cancellationToken)
    {
        var loaded = await Timelines
            .Find(Builders<PlayerMmrRpTimeline>.Filter.In(t => t.Id, ids))
            .ToListAsync(cancellationToken);

        return loaded.ToDictionary(t => t.Id);
    }

    /// <summary>
    /// The day of the oldest match event, found from the <c>_id</c> index rather than by
    /// scanning for the smallest endTime.
    /// </summary>
    private async Task<DateOnly?> FindFirstDay(CancellationToken cancellationToken)
    {
        var oldest = await Events
            .Find(Builders<MatchFinishedEvent>.Filter.Empty)
            .Sort(Builders<MatchFinishedEvent>.Sort.Ascending(e => e.Id))
            .Limit(1)
            .Project(e => e.Id)
            .FirstOrDefaultAsync(cancellationToken);

        return oldest == ObjectId.Empty
            ? null
            // A day earlier than the first insert, since a match can finish just before
            // midnight and be inserted just after.
            : DateOnly.FromDateTime(oldest.CreationTime.ToUniversalTime()).AddDays(-1);
    }

    private static DateOnly? ReadCheckpoint(IAdminJobContext context) =>
        context.Checkpoint != null && context.Checkpoint.TryGetValue(CheckpointField, out var value) && value.IsNumeric
            ? DateOnly.FromDayNumber(value.ToInt32())
            : null;

    /// <summary>
    /// The smallest ObjectId with this timestamp: four big-endian seconds followed by
    /// zeroes. Built by hand rather than with GenerateNewId, whose random remainder
    /// would put the boundary at an arbitrary point within its second.
    /// </summary>
    private static ObjectId ObjectIdAt(DateTimeOffset moment)
    {
        var bytes = new byte[12];
        var seconds = (uint)moment.ToUnixTimeSeconds();

        bytes[0] = (byte)(seconds >> 24);
        bytes[1] = (byte)(seconds >> 16);
        bytes[2] = (byte)(seconds >> 8);
        bytes[3] = (byte)seconds;

        return new ObjectId(bytes);
    }

    /// <summary>A day's worth of games for one timeline, folded into a single entry.</summary>
    private sealed record RebuiltDay(PlayerMmrRpTimeline Template, MmrRpAtDate Entry)
    {
        /// <summary>
        /// The template carries the identity the entry was keyed by, so a timeline that
        /// does not exist yet can be created without re-parsing its id.
        /// </summary>
        public PlayerMmrRpTimeline NewTimeline() => Template;
    }
}
