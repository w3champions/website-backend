using System.Threading.Tasks;

namespace W3ChampionsStatisticService.Ports
{
    public interface IVersionRepository
    {
        Task<string> GetLastVersion<T>();
        Task SaveLastVersion<T>(string lastVersion);
    }
}