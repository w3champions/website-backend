using System.Collections.Generic;
using System.Threading.Tasks;
using W3C.Domain.Rewards.Entities;

namespace W3C.Domain.Rewards.Repositories;

public interface IRewardRepository
{
    Task<Reward> GetById(string rewardId);
    Task<List<Reward>> GetAll();
    Task<List<Reward>> GetActive();
    Task<Reward> Create(Reward reward);
    Task<Reward> Update(Reward reward);
    Task Delete(string rewardId);
    Task<Reward> GetByModuleAndParameters(string moduleId, Dictionary<string, object> parameters);
}
