using System;
using System.Threading.Tasks;
using W3ChampionsStatisticService.Achievements.Models;
using W3ChampionsStatisticService.CommonValueObjects;
using W3ChampionsStatisticService.PlayerStats.RaceOnMapVersusRaceStats;
using W3ChampionsStatisticService.PlayerStats.HeroStats;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.ReadModelBase;
using W3ChampionsStatisticService.PadEvents;

namespace W3ChampionsStatisticService.Achievements {
    public class AchievementRepositoryHandler : IReadModelHandler  {

        private readonly IAchievementRepository _achievementRepository;
        private readonly IPlayerStatsRepository _playerRepository;
        private readonly IPlayerStatsRepository _playerStatsRepository;

        public AchievementRepositoryHandler(
            IAchievementRepository achievementRepository,
            IPlayerStatsRepository playerRepository,
            IPlayerStatsRepository playerStatsRepository) {
           _achievementRepository = achievementRepository;    
            _playerRepository = playerRepository;
            _playerStatsRepository = playerStatsRepository;
        }

        public async Task Update(MatchFinishedEvent nextEvent) {
            if (nextEvent == null || nextEvent.match == null || nextEvent.result == null) {
                return;
            }
        } 

        public async Task<PlayerAchievements> GetPlayerAchievements(string playerId){
            var playerAchievements = await _achievementRepository.GetPlayerAchievements(playerId);
            if (playerAchievements == null){
                // need to create the achievements
                CreateNewPlayerAchievements(playerId);
                // need to update the newly created achievements
                // need to save the newly created achievements
                // once saved, pass achievments out to be used -- can use playerAchievementsFound
            }
            return playerAchievements;
        }

        private void CreateNewPlayerAchievements(string playerId) {

        }
    }
}