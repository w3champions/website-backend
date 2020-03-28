using System.Threading.Tasks;
using W3ChampionsStatisticService.Ports;

namespace W3ChampionsStatisticService.MongoDb
{
    public class VersionRepository : IVersionRepository
    {
        public Task<string> GetLastVersion<T>()
        {
            return Task.FromResult("");
        }

        public Task SaveLastVersion<T>(string lastVersion)
        {
            return Task.CompletedTask;
        }
    }
}