using System;
using System.Threading.Tasks;
using W3ChampionsStatisticService.Achievements.Models;

namespace W3ChampionsStatisticService.Ports {
    public interface IAchievementRepository {
        Task<bool> UpsertPlayerAchievements(PlayerAchievements playerAchievements);
        Task<PlayerAchievements> GetPlayerAchievements(string playerId);
        Task DeletePlayerAchievements(string playerId);
    }
}