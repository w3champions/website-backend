using System.Threading.Tasks;
using MongoDB.Driver;
using W3C.Contracts.Matchmaking;
using W3C.Domain.Repositories;
using W3C.Domain.Tracing;

namespace W3ChampionsStatisticService.Ladder;

[Trace]
public class ApexLeaderboardRepository(MongoClient mongoClient)
    : MongoDbRepositoryBase(mongoClient), IApexLeaderboardRepository
{
    public Task UpsertOne(ApexLeaderboard leaderboard)
    {
        return Upsert(leaderboard);
    }

    public Task<ApexLeaderboard> LoadApexLeaderboard(int season, GameMode gameMode)
    {
        var id = $"{season}_{(int)gameMode}";
        return LoadFirst<ApexLeaderboard>(id);
    }
}
