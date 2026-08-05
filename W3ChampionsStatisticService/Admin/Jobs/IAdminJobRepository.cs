using System.Collections.Generic;
using System.Threading.Tasks;
using MongoDB.Bson;

namespace W3ChampionsStatisticService.Admin.Jobs;

public interface IAdminJobRepository
{
    Task<List<AdminJob>> LoadAll();

    Task<AdminJob> Load(string key);

    /// <summary>
    /// Atomically moves a job into <see cref="AdminJobStatus.Running"/>, returning the
    /// claimed document, or null if its current status does not allow starting.
    /// </summary>
    /// <param name="force">Also allow starting a job that has already completed.</param>
    /// <param name="reset">Discard the checkpoint and counters and start over.</param>
    Task<AdminJob> TryClaim(string key, string battleTag, bool force, bool reset);

    Task ReportProgress(string key, AdminJobProgress progress, BsonDocument checkpoint, long itemsProcessed);

    /// <param name="error">Null unless <paramref name="status"/> is <see cref="AdminJobStatus.Failed"/>.</param>
    Task Finish(string key, AdminJobStatus status, string error);

    /// <summary>
    /// Marks every job left <see cref="AdminJobStatus.Running"/> as
    /// <see cref="AdminJobStatus.Interrupted"/>. Returns the keys affected.
    /// </summary>
    Task<List<string>> MarkRunningAsInterrupted();
}
