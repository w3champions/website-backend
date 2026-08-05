using System.Threading.Tasks;

namespace W3ChampionsStatisticService.Ports;

public interface IVersionRepository
{
    Task<HandlerVersion> GetLastVersion<T>();

    /// <summary>Commits the watermark and clears any failure tracking, since this is forward progress.</summary>
    Task SaveLastVersion<T>(string lastVersion, int season = 0);

    /// <summary>
    /// Records that <paramref name="eventId"/> failed and returns how often it has failed in a row.
    /// The count has to be persisted because the read model handlers are transient: a fresh instance
    /// is resolved for every retry cycle, so an in-memory counter would reset before it ever trips.
    /// A different event id than the last recorded one restarts the count at 1.
    /// </summary>
    Task<int> RecordEventFailure<T>(string eventId);
}

public class HandlerVersion(string version, int season, bool isStopped)
{
    public string Version { get; set; } = version;
    public int Season { get; set; } = season;
    public bool IsStopped { get; set; } = isStopped;
}
