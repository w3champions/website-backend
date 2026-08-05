using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using MongoDB.Bson;
using MongoDB.Driver;
using Serilog;

namespace W3ChampionsStatisticService.Admin.Jobs;

/// <summary>
/// One reading of how hard the database and this service are currently working.
/// <para>
/// Counter fields are cumulative since start and only mean anything as the difference
/// between two samples; gauge fields stand on their own. Interpreting them is
/// <see cref="AdminJobContext"/>'s job, so that a probe stays stateless and can be
/// shared by concurrently running jobs.
/// </para>
/// </summary>
public record PressureSample(
    DateTimeOffset TakenAt,
    TimeSpan ProcessCpuTime,
    int ProcessorCount,
    bool DatabaseAvailable,
    long DirtyTriggerReached,
    long ApplicationThreadEvictions,
    double DirtyCacheFraction,
    double WriteTicketUtilisation,
    long QueuedWriters);

public interface IPressureProbe
{
    Task<PressureSample> Sample(CancellationToken cancellationToken);
}

/// <summary>
/// Samples two independent back-pressure signals: how loaded the database is, and how
/// much CPU this service is burning.
/// <para>
/// <b>Database.</b> This deployment is a single mongod, so there is no replication lag
/// to watch. The equivalent signal is WiredTiger eviction pressure - replica lag is
/// really a proxy for "writes are piling up faster than they can be durably absorbed",
/// and on a standalone server that condition shows up as a growing dirty cache and,
/// past a point, as mongod conscripting query threads into doing eviction work.
/// </para>
/// <para>
/// <b>CPU.</b> Jobs run inside the same process as the API, so a job that pegs the CPU
/// degrades every request the site serves even if the database is perfectly happy. This
/// measures the whole process rather than the job, which is the point: what matters is
/// whether there is headroom left for normal traffic.
/// </para>
/// </summary>
public class PressureProbe(MongoClient mongoClient) : IPressureProbe
{
    /// <summary>
    /// Sections we never read. serverStatus is cheap but not free, and metrics and locks
    /// are the bulky ones.
    /// </summary>
    private static readonly BsonDocument ServerStatusCommand = new()
    {
        { "serverStatus", 1 },
        { "metrics", 0 },
        { "locks", 0 },
        { "tcmalloc", 0 },
        { "opLatencies", 0 },
    };

    private bool _loggedUnavailable;

    public async Task<PressureSample> Sample(CancellationToken cancellationToken)
    {
        var process = Process.GetCurrentProcess();

        // ProcessorCount honours the container's CPU limit, so this stays a fraction of
        // what we are actually allowed to use rather than of the host's hardware.
        var local = new PressureSample(
            TakenAt: DateTimeOffset.UtcNow,
            ProcessCpuTime: process.TotalProcessorTime,
            ProcessorCount: Math.Max(Environment.ProcessorCount, 1),
            DatabaseAvailable: false,
            DirtyTriggerReached: 0,
            ApplicationThreadEvictions: 0,
            DirtyCacheFraction: 0,
            WriteTicketUtilisation: 0,
            QueuedWriters: 0);

        BsonDocument status;
        try
        {
            status = await mongoClient
                .GetDatabase("admin")
                .RunCommandAsync<BsonDocument>(ServerStatusCommand, cancellationToken: cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (!_loggedUnavailable)
            {
                // Most likely the user lacks clusterMonitor. Say so once; jobs fall back
                // to a fixed conservative pace and keep the CPU brake.
                _loggedUnavailable = true;
                Log.Warning(ex, "Cannot read MongoDB pressure (serverStatus); admin jobs will pace themselves blindly");
            }

            return local;
        }

        var cache = Get(status, "wiredTiger", "cache");
        if (cache == null)
        {
            return local;
        }

        var maxBytes = GetNumber(cache, "maximum bytes configured") ?? 0;
        var dirtyBytes = GetNumber(cache, "tracked dirty bytes in the cache") ?? 0;

        var writeQueue = Get(status, "queues", "execution", "write");
        var totalTickets = GetNumber(writeQueue, "totalTickets") ?? 0;
        var ticketsOut = GetNumber(writeQueue, "out") ?? 0;

        return local with
        {
            DatabaseAvailable = true,
            DirtyTriggerReached = (long)(GetNumber(cache, "number of times dirty trigger was reached") ?? 0),
            ApplicationThreadEvictions = (long)(GetNumber(cache, "application threads page write from cache to disk count") ?? 0),
            DirtyCacheFraction = maxBytes > 0 ? dirtyBytes / maxBytes : 0,
            WriteTicketUtilisation = totalTickets > 0 ? ticketsOut / totalTickets : 0,
            QueuedWriters = (long)(GetNumber(Get(status, "globalLock", "currentQueue"), "writers") ?? 0),
        };
    }

    private static BsonDocument Get(BsonDocument document, params string[] path)
    {
        foreach (var step in path)
        {
            if (document == null || !document.TryGetValue(step, out var value) || !value.IsBsonDocument)
            {
                return null;
            }

            document = value.AsBsonDocument;
        }

        return document;
    }

    private static double? GetNumber(BsonDocument document, string field) =>
        document != null && document.TryGetValue(field, out var value) && value.IsNumeric
            ? value.ToDouble()
            : null;
}
