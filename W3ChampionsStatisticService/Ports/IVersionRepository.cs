using System.Threading.Tasks;

namespace W3ChampionsStatisticService.Ports;

public interface IVersionRepository
{
    Task<HandlerVersion> GetLastVersion<T>();
    Task SaveLastVersion<T>(string lastVersion, int season = 0);
}

public class HandlerVersion(string version, int season, bool isStopped)
{
    public string Version { get; set; } = version;
    public int Season { get; set; } = season;
    public bool IsStopped { get; set; } = isStopped;
}
