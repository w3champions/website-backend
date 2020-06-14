using System.Threading.Tasks;

namespace W3ChampionsStatisticService.Ports
{
    public interface IVersionRepository
    {
        Task<HandlerVersion> GetLastVersion<T>();
        Task SaveLastVersion<T>(string lastVersion, int season = 0);
    }

    public class HandlerVersion
    {
        public HandlerVersion(string version, int season, bool isStopped)
        {
            Version = version;
            Season = season;
            IsStopped = isStopped;
        }

        public string Version { get; set; }
        public int Season { get; set; }
        public bool IsStopped { get; set; }
    }
}