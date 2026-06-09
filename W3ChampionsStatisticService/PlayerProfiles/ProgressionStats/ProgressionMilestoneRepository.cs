using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MongoDB.Driver;
using W3C.Domain.Repositories;
using W3C.Domain.Tracing;
using W3ChampionsStatisticService.Ports;

namespace W3ChampionsStatisticService.PlayerProfiles.ProgressionStats;

[Trace]
public class ProgressionMilestoneRepository(MongoClient mongoClient)
    : MongoDbRepositoryBase(mongoClient), IProgressionMilestoneRepository, IRequiresIndexes
{
    public string CollectionName => nameof(ProgressionMilestone);

    public Task<ProgressionMilestone> LoadMilestone(string id)
    {
        return LoadFirst<ProgressionMilestone>(id);
    }

    public Task UpsertMilestone(ProgressionMilestone milestone)
    {
        return Upsert(milestone);
    }

    public Task<List<ProgressionMilestone>> LoadMilestonesForPlayer(string battleTag)
    {
        return LoadAll<ProgressionMilestone>(m => m.PlayerIds.Any(p => p.BattleTag == battleTag));
    }

    public async Task EnsureIndexesAsync()
    {
        var collection = CreateCollection<ProgressionMilestone>();

        var indexes = new List<CreateIndexModel<ProgressionMilestone>>
        {
            // Multikey index backing the owner read (LoadMilestonesForPlayer): the
            // m.PlayerIds.Any(p => p.BattleTag == battleTag) filter would otherwise
            // COLLSCAN the collection, which grows one document per entity/mode/race.
            new(Builders<ProgressionMilestone>.IndexKeys.Ascending("PlayerIds.BattleTag")),
        };

        await collection.Indexes.CreateManyAsync(indexes);
    }
}
