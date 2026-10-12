using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using W3C.Domain.Repositories;

namespace W3ChampionsStatisticService.Admin.ReplayExport;

/// <summary>
/// The indexes of the replay export searches.
///
/// They are not part of the startup index step (MongoIndexInitializationService): the server starts only
/// when that step is done, and a new index on the large Matchup collection can take minutes to build.
/// <see cref="ReplayExportIndexService"/> builds them in the background instead.
///
/// The indexes that a database already has are not known in advance, so each index is safe on its own:
/// <list type="bullet">
/// <item>An index with the same keys exists (with any name): nothing to do.</item>
/// <item>Not enough free disk for the build: no build, a warning in the log. The searches stay correct,
/// only slower, and their time limits protect the database.</item>
/// <item>An error: logged, and the next index is tried.</item>
/// </list>
/// </summary>
public class ReplayExportIndexes(MongoClient mongoClient, ILogger<ReplayExportIndexes> logger) : MongoDbRepositoryBase(mongoClient)
{
    public record Spec(string Collection, string Name, BsonDocument Keys);

    public static readonly IReadOnlyList<Spec> Specs =
    [
        // Every search filters by game mode and sorts by _id. Patch and dates are _id ranges.
        new("Matchup", "ReplayExport_GameMode_Id", new BsonDocument { { "GameMode", 1 }, { "_id", -1 } }),
        // Searches with seasons: one index range per season, merged in _id order.
        new("Matchup", "ReplayExport_GameMode_Season_Id", new BsonDocument { { "GameMode", 1 }, { "Season", 1 }, { "_id", -1 } }),
        // Searches with a map: a new map is rare in old seasons, so without this index "Oldest" and the
        // count read every older game. The player filter uses MatchRepository's text index instead.
        new("Matchup", "ReplayExport_GameMode_MapName_Id", new BsonDocument { { "GameMode", 1 }, { "MapName", 1 }, { "_id", -1 } }),
        // The past downloads of one user, newest first.
        new("ReplayExports", "ReplayExport_RequestedBy_Id", new BsonDocument { { "RequestedBy", 1 }, { "_id", -1 } }),
    ];

    /// <summary>Free disk that always stays free, also after a build.</summary>
    public const long DiskReserveBytes = 2L * 1024 * 1024 * 1024;

    /// <summary>
    /// A generous size of one index entry. A build needs room for the index and for its temporary sort
    /// files, so <see cref="RequiredFreeBytes"/> counts three times this per document.
    /// </summary>
    public const long BytesPerEntry = 64;

    // Mongo error codes: an index with the same keys and another name or other options, or with the same
    // name and other keys.
    private const int IndexOptionsConflict = 85;
    private const int IndexKeySpecsConflict = 86;

    public static long RequiredFreeBytes(long documents) => DiskReserveBytes + 3 * Math.Max(documents, 0) * BytesPerEntry;

    /// <summary>True when the keys are the same fields in the same order and direction (1 and 1.0 are the same).</summary>
    public static bool SameKeys(BsonDocument a, BsonDocument b)
    {
        if (a.ElementCount != b.ElementCount) return false;
        for (var i = 0; i < a.ElementCount; i++)
        {
            var (x, y) = (a.GetElement(i), b.GetElement(i));
            if (x.Name != y.Name) return false;
            var same = x.Value.IsNumeric && y.Value.IsNumeric ? x.Value.ToDouble() == y.Value.ToDouble() : x.Value.Equals(y.Value);
            if (!same) return false;
        }
        return true;
    }

    /// <summary>Makes sure that every index of <see cref="Specs"/> exists, when it is safe to build it.</summary>
    public async Task EnsureAsync(CancellationToken cancellationToken)
    {
        foreach (var spec in Specs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await Ensure(spec, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (MongoCommandException ex) when (ex.Code is IndexOptionsConflict or IndexKeySpecsConflict)
            {
                logger.LogWarning(ex, "Replay export index {Index} on {Collection} conflicts with an existing index; not built", spec.Name, spec.Collection);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Replay export index {Index} on {Collection} could not be built", spec.Name, spec.Collection);
            }
        }
    }

    private async Task Ensure(Spec spec, CancellationToken cancellationToken)
    {
        var database = CreateClient();
        var collection = database.GetCollection<BsonDocument>(spec.Collection);

        var existing = await (await collection.Indexes.ListAsync(cancellationToken)).ToListAsync(cancellationToken);
        var same = existing.FirstOrDefault(index => index.TryGetValue("key", out var key) && key.IsBsonDocument && SameKeys(key.AsBsonDocument, spec.Keys));
        if (same != null)
        {
            logger.LogInformation("Replay export index {Index} on {Collection} exists as {ExistingName}", spec.Name, spec.Collection, same.GetValue("name", "?"));
            return;
        }

        var documents = await collection.EstimatedDocumentCountAsync(cancellationToken: cancellationToken);
        var free = await FreeDiskBytes(database, cancellationToken);
        var required = RequiredFreeBytes(documents);
        if (free == null)
        {
            logger.LogWarning(
                "Replay export index {Index} on {Collection} not built: free disk unknown. Build it by hand when there is room: createIndex({Keys}, {{ name: \"{IndexName}\" }})",
                spec.Name, spec.Collection, spec.Keys.ToJson(), spec.Name);
            return;
        }
        if (free < required)
        {
            logger.LogWarning(
                "Replay export index {Index} on {Collection} not built: free disk {FreeBytes} bytes, needed {RequiredBytes} bytes for {Documents} documents. Build it by hand when there is room: createIndex({Keys}, {{ name: \"{IndexName}\" }})",
                spec.Name, spec.Collection, free, required, documents, spec.Keys.ToJson(), spec.Name);
            return;
        }

        logger.LogInformation("Building replay export index {Index} on {Collection} ({Documents} documents)", spec.Name, spec.Collection, documents);
        var stopwatch = Stopwatch.StartNew();
        await collection.Indexes.CreateOneAsync(
            new CreateIndexModel<BsonDocument>(spec.Keys, new CreateIndexOptions { Name = spec.Name }),
            cancellationToken: cancellationToken);
        logger.LogInformation("Built replay export index {Index} on {Collection} in {ElapsedMs} ms", spec.Name, spec.Collection, stopwatch.ElapsedMilliseconds);
    }

    /// <summary>
    /// Free bytes on the disk of the database (dbStats), or null when the server does not tell. A database
    /// that does not exist yet reports a disk of 0 bytes: that is unknown too.
    /// </summary>
    protected virtual async Task<long?> FreeDiskBytes(IMongoDatabase database, CancellationToken cancellationToken)
    {
        var stats = await database.RunCommandAsync<BsonDocument>(new BsonDocument { { "dbStats", 1 }, { "scale", 1 } }, cancellationToken: cancellationToken);
        if (!stats.TryGetValue("fsTotalSize", out var total) || !stats.TryGetValue("fsUsedSize", out var used)) return null;
        if (!total.IsNumeric || !used.IsNumeric || total.ToDouble() <= 0) return null;
        return (long)(total.ToDouble() - used.ToDouble());
    }
}

/// <summary>
/// Builds the <see cref="ReplayExportIndexes"/> after the start, in the background, so the server does not
/// wait for the builds. Errors are logged: an exception from a BackgroundService would stop the host.
/// </summary>
public class ReplayExportIndexService(ReplayExportIndexes indexes, ILogger<ReplayExportIndexService> logger) : BackgroundService
{
    /// <summary>Wait after the start, so the builds do not compete with the start of the service.</summary>
    public static readonly TimeSpan StartDelay = TimeSpan.FromSeconds(30);

    /// <summary>Set to "off" to skip the builds, for example when the database disk is short of space.</summary>
    public const string SwitchVariable = "REPLAY_EXPORT_INDEXES";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (string.Equals(Environment.GetEnvironmentVariable(SwitchVariable), "off", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogInformation("Replay export indexes are not built: {Variable} is off", SwitchVariable);
            return;
        }

        try
        {
            await Task.Delay(StartDelay, stoppingToken);
            await indexes.EnsureAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // The service stops. A build that runs on the server continues there.
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Replay export index service failed");
        }
    }
}
