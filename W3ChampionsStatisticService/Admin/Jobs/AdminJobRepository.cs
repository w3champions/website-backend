using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MongoDB.Bson;
using MongoDB.Driver;
using W3C.Domain.Repositories;
using W3C.Domain.Tracing;

namespace W3ChampionsStatisticService.Admin.Jobs;

[Trace]
public class AdminJobRepository(MongoClient mongoClient) : MongoDbRepositoryBase(mongoClient), IAdminJobRepository
{
    private IMongoCollection<AdminJob> Jobs => CreateCollection<AdminJob>();

    /// <summary>
    /// Statuses a job may be started from. <see cref="AdminJobStatus.Completed"/> is
    /// excluded so a finished job is not re-run by a stray click; <c>force</c> adds it.
    /// </summary>
    private static readonly AdminJobStatus[] StartableStatuses =
    [
        AdminJobStatus.Idle,
        AdminJobStatus.Failed,
        AdminJobStatus.Cancelled,
        AdminJobStatus.Interrupted,
    ];

    public Task<List<AdminJob>> LoadAll() => LoadAll<AdminJob>();

    public Task<AdminJob> Load(string key) => LoadFirst<AdminJob>(key);

    public async Task<AdminJob> TryClaim(string key, string battleTag, bool force, bool reset)
    {
        // Make sure a document exists before the conditional update below. This can't
        // be folded into that update as an upsert: when the document exists but its
        // status excludes it from the filter, Mongo would attempt an insert and fail
        // on the duplicate _id rather than reporting "not startable".
        await Jobs.UpdateOneAsync(
            j => j.Id == key,
            Builders<AdminJob>.Update
                .SetOnInsert(j => j.Status, AdminJobStatus.Idle)
                .SetOnInsert(j => j.RunCount, 0),
            new UpdateOptions { IsUpsert = true });

        AdminJobStatus[] startable = force
            ? [.. StartableStatuses, AdminJobStatus.Completed]
            : StartableStatuses;

        // Single atomic transition. With one service instance this can't currently
        // race, but it is also what stops two admins clicking Run at the same moment.
        var before = await Jobs.FindOneAndUpdateAsync(
            Builders<AdminJob>.Filter.And(
                Builders<AdminJob>.Filter.Eq(j => j.Id, key),
                Builders<AdminJob>.Filter.In(j => j.Status, startable)),
            Builders<AdminJob>.Update
                .Set(j => j.Status, AdminJobStatus.Running)
                .Set(j => j.TriggeredBy, battleTag)
                .Set(j => j.FinishedAt, null)
                .Set(j => j.DurationMs, null)
                .Set(j => j.Error, null)
                .Inc(j => j.RunCount, 1),
            new FindOneAndUpdateOptions<AdminJob> { ReturnDocument = ReturnDocument.Before });

        if (before == null)
        {
            return null;
        }

        // A run either continues where the last one stopped or starts over. Continuing
        // keeps the checkpoint, the item count and the accumulated runtime, so a job
        // that survived a deploy - or was paused, which is a cancel plus a run - still
        // reports its true totals. Completed is always a fresh run: reaching it via
        // `force` means the admin asked for the whole thing again.
        var resuming = !reset
            && before.Checkpoint != null
            && before.Status is AdminJobStatus.Failed or AdminJobStatus.Cancelled or AdminJobStatus.Interrupted;

        var startedAt = DateTimeOffset.UtcNow;
        var update = Builders<AdminJob>.Update.Set(j => j.StartedAt, startedAt);

        if (resuming)
        {
            // Bank the previous leg and restart the clock, so the gap while the job sat
            // stopped is not counted as runtime.
            var previousLeg = before.StartedAt is { } previousStart
                ? (long)((before.FinishedAt ?? startedAt) - previousStart).TotalMilliseconds
                : 0;
            update = update.Set(j => j.ActiveMs, before.ActiveMs + Math.Max(previousLeg, 0));
        }
        else
        {
            update = update
                .Set(j => j.ActiveMs, 0L)
                .Set(j => j.ItemsProcessed, 0L)
                .Set(j => j.Progress, new AdminJobProgress())
                .Set(j => j.Checkpoint, null);
        }

        await Jobs.UpdateOneAsync(j => j.Id == key, update);

        return await Load(key);
    }

    public Task ReportProgress(string key, AdminJobProgress progress, BsonDocument checkpoint, long itemsProcessed)
    {
        var update = Builders<AdminJob>.Update
            .Set(j => j.Progress, progress)
            .Set(j => j.ItemsProcessed, itemsProcessed);

        // A null checkpoint means "unchanged", not "clear it" - only a reset clears.
        if (checkpoint != null)
        {
            update = update.Set(j => j.Checkpoint, checkpoint);
        }

        return Jobs.UpdateOneAsync(j => j.Id == key, update);
    }

    public async Task Finish(string key, AdminJobStatus status, string error)
    {
        var finishedAt = DateTimeOffset.UtcNow;
        var job = await Load(key);
        var durationMs = job?.StartedAt is { } startedAt
            ? job.ActiveMs + (long)(finishedAt - startedAt).TotalMilliseconds
            : (long?)null;

        await Jobs.UpdateOneAsync(
            j => j.Id == key,
            Builders<AdminJob>.Update
                .Set(j => j.Status, status)
                .Set(j => j.FinishedAt, finishedAt)
                .Set(j => j.DurationMs, durationMs)
                .Set(j => j.Error, error));
    }

    public async Task<List<string>> MarkRunningAsInterrupted()
    {
        var running = Builders<AdminJob>.Filter.Eq(j => j.Status, AdminJobStatus.Running);

        var keys = await Jobs.Find(running).Project(j => j.Id).ToListAsync();
        if (keys.Count == 0)
        {
            return keys;
        }

        await Jobs.UpdateManyAsync(
            running,
            Builders<AdminJob>.Update
                .Set(j => j.Status, AdminJobStatus.Interrupted)
                .Set(j => j.FinishedAt, DateTimeOffset.UtcNow)
                .Set(j => j.Error, "Interrupted - the service stopped while this job was running."));

        return keys;
    }
}
