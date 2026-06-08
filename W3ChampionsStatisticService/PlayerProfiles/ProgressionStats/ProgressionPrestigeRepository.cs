using System.Threading.Tasks;
using MongoDB.Driver;
using W3C.Domain.Repositories;
using W3C.Domain.Tracing;
using W3ChampionsStatisticService.Ports;

namespace W3ChampionsStatisticService.PlayerProfiles.ProgressionStats;

[Trace]
public class ProgressionPrestigeRepository(MongoClient mongoClient)
    : MongoDbRepositoryBase(mongoClient), IProgressionPrestigeRepository
{
    public Task<ProgressionPrestige> LoadPrestige(string battleTag)
    {
        return LoadFirst<ProgressionPrestige>(battleTag);
    }

    public Task UpsertPrestige(ProgressionPrestige prestige)
    {
        return Upsert(prestige);
    }
}
