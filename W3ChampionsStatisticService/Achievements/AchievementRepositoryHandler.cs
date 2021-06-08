using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using W3ChampionsStatisticService.Achievements.Models;
using W3ChampionsStatisticService.CommonValueObjects;
using W3ChampionsStatisticService.Ladder;
using W3ChampionsStatisticService.PlayerStats.RaceOnMapVersusRaceStats;
using W3ChampionsStatisticService.PlayerStats.HeroStats;
using W3ChampionsStatisticService.PlayerProfiles;
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
            //TODO: get this to work when match is finished....
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
                    var newPlayerAchievements = CreateNewPlayerAchievements(playerProfile);
                    // once saved, pass achievments out to be used -- can use playerAchievementsFound
                    // TODO
                }
            }
            return playerAchievements;
        }

        private List<int> ConvertSeasonsToSimpleList(List<Season> seasons) {
            var seasonArray = new List<int>();
            foreach (Season s in seasons){seasonArray.Add(s.Id);}
            return seasonArray;
        }

        private List<Achievement> GenerateNewAchievementList() {
            var achievementList = new List<Achievement>();
            achievementList.Add(new MapWith25WinsAchievement());
            achievementList.Add(new Win10GamesWithATPartnerAchievement());
            return achievementList;
        }

        private PlayerAchievements UpdateCurrentPlayerAchievements(PlayerAchievements playerAchievements, PlayerOverallStats playerOverallStats, bool isFirstUpdate){
            // TODO: create way for achievements to be updated
            // working here...
            var seasons = ConvertSeasonsToSimpleList(playerOverallStats.ParticipatedInSeasons);
            return playerAchievements;
        }

        private PlayerAchievements CreateNewPlayerAchievements(PlayerOverallStats playerOverallStats) {
            var newPlayerAchievements = new PlayerAchievements();
            newPlayerAchievements.PlayerId = playerOverallStats.BattleTag;
            newPlayerAchievements.playerAchievements = GenerateNewAchievementList();
            newPlayerAchievements = UpdateCurrentPlayerAchievements(newPlayerAchievements, playerOverallStats, true);
            return newPlayerAchievements;
        }
    }
}