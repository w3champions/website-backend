using System.Threading.Tasks;
using MongoDB.Driver;
using W3C.Domain.Repositories;
using W3C.Domain.Tracing;
using W3ChampionsStatisticService.Ports;

namespace W3ChampionsStatisticService.PlayerProfiles.ProgressionStats;

[Trace]
public class PlayerProgressionRepository(MongoClient mongoClient)
    : MongoDbRepositoryBase(mongoClient), IPlayerProgressionRepository
{
    public Task<PlayerProgression> LoadProgression(string id)
    {
        return LoadFirst<PlayerProgression>(id);
    }

    public Task UpsertProgression(PlayerProgression progression)
    {
        return Upsert(progression);
    }
}
