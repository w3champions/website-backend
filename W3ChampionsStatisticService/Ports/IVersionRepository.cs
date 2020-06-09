using System.Threading.Tasks;
using W3ChampionsStatisticService.ReadModelBase;

namespace W3ChampionsStatisticService.Ports
{
    public interface IVersionRepository
    {
        Task<HandlerVersion> GetLastVersion<T>(bool tempVersion);
        Task SaveLastVersion<T>(string lastVersion, bool tempVersion, int season = 0);
        Task SaveSyncState<T>(SyncState syncState, bool tempVersion);
    }

    public class HandlerVersion
    {
        public HandlerVersion(string version, int season, bool isStopped, SyncState syncState)
        {
            Version = version;
            Season = season;
            IsStopped = isStopped;
            SyncState = syncState;
        }

        public string Version { get; set; }
        public int Season { get; set; }
        public bool IsStopped { get; set; }
        public SyncState SyncState { get; set; }
    }
}