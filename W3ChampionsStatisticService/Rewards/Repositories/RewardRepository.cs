using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MongoDB.Driver;
using W3C.Domain.Repositories;
using W3C.Domain.Rewards.Entities;
using W3C.Domain.Common.Repositories;
using W3C.Domain.Rewards.Repositories;
using W3C.Domain.Rewards.ValueObjects;
using W3C.Domain.Tracing;

namespace W3ChampionsStatisticService.Rewards.Repositories;

[Trace]
public class RewardRepository(MongoClient mongoClient) : MongoDbRepositoryBase(mongoClient), IRewardRepository
{
    public Task<Reward> GetById(string rewardId)
    {
        return LoadFirst<Reward>(rewardId);
    }

    public Task<List<Reward>> GetAll()
    {
        return LoadAll<Reward>();
    }

    public Task<List<Reward>> GetActive()
    {
        return LoadAll<Reward>(r => r.IsActive == true);
    }

    public async Task<Reward> Create(Reward reward)
    {
        await Insert(reward);
        return reward;
    }

    public async Task<Reward> Update(Reward reward)
    {
        await Upsert(reward);
        return reward;
    }

    public Task Delete(string rewardId)
    {
        return Delete<Reward>(rewardId);
    }

    public async Task<Reward> GetByModuleAndParameters(string moduleId, Dictionary<string, object> parameters)
    {
        var rewards = await LoadAll<Reward>(r => r.ModuleId == moduleId && r.IsActive == true);

        // Find matching parameters (simplified comparison)
        return rewards.FirstOrDefault(r =>
            r.Parameters.Count == parameters.Count &&
            r.Parameters.All(p => parameters.ContainsKey(p.Key)));
    }
}
