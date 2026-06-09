using System.Threading.Tasks;
using W3C.Contracts.Matchmaking;

namespace W3ChampionsStatisticService.Ladder;

public interface IApexLeaderboardRepository
{
    Task UpsertOne(ApexLeaderboard leaderboard);
    Task<ApexLeaderboard> LoadApexLeaderboard(int season, GameMode gameMode);
}
