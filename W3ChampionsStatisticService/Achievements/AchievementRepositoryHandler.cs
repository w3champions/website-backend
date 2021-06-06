using System;
using System.Collections.Generic;
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
        private readonly IPlayerRepository _playerRepository;
        private readonly IPlayerStatsRepository _playerStatsRepository;

        public AchievementRepositoryHandler(
            IAchievementRepository achievementRepository,
            IPlayerRepository playerRepository,
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
                // check if the player exists....
                var playerProfile = await _playerRepository.LoadPlayerProfile(playerId);
                if (playerProfile != null){
                    // need to create the achievements
                    var newPlayerAchievements = CreateNewPlayerAchievements(playerId);
                    // need to update the newly created achievements
                    // need to save the newly created achievements
                    // once saved, pass achievments out to be used -- can use playerAchievementsFound
                }
            }
            return playerAchievements;
        }

        private List<Achievement> GenerateNewAchievementList() {
            var achievementList = new List<Achievement>();
            achievementList.Add(new MapWith25WinsAchievement());
            achievementList.Add(new Win10GamesWithATPartnerAchievement());
            return achievementList;
        }

        private PlayerAchievements CreateNewPlayerAchievements(string playerId) {
            var newPlayerAchievements = new PlayerAchievements();
            newPlayerAchievements.PlayerId = playerId;
            newPlayerAchievements.playerAchievements = GenerateNewAchievementList();
            return newPlayerAchievements;
        }
    }
}