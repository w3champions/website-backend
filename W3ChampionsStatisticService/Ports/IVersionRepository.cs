using System.Threading.Tasks;
using W3ChampionsStatisticService.ReadModelBase;

namespace W3ChampionsStatisticService.Ports
{
    public interface IVersionRepository
    {
        Task<HandlerVersion> GetLastVersion<T>(bool tempVersion = false);
        Task SaveLastVersion<T>(string lastVersion, int season = 0, bool tempVersion = false);
        Task SaveSyncState<T>(SyncState syncState, bool tempVersion = false);
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