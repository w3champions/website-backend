using System.Threading.Tasks;
using MongoDB.Driver;
using W3ChampionsStatisticService.ReadModelBase;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.Achievements.Models;

namespace W3ChampionsStatisticService.Achievements {
    public class AchievementRepository : MongoDbRepositoryBase, IAchievementRepository {
        public async Task<bool> UpsertPlayerAchievements(PlayerAchievements playerAchievements) {
            var playerAchievementsFound = await LoadFirst<PlayerAchievements>(p => p.PlayerId == playerAchievements.PlayerId);
            if (playerAchievementsFound != null) return false;
            await Insert(playerAchievements);
            return true;
        }
        public Task<PlayerAchievements> GetPlayerAchievements(string playerId) {
            return LoadFirst<PlayerAchievements>(p => p.PlayerId == playerId);
        }
        public Task DeletePlayerAchievements(string playerId) {
            return Delete<PlayerAchievements>(p => p.PlayerId == playerId);
        }

        public AchievementRepository( MongoClient mongoClient) : base(mongoClient) {
        }
    }
}