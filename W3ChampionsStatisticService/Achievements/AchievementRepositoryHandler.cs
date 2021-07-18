using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MongoDB.Bson;
using W3ChampionsStatisticService.Achievements.Models;
using W3ChampionsStatisticService.CommonValueObjects;
using W3ChampionsStatisticService.Ladder;
using W3ChampionsStatisticService.PlayerStats.RaceOnMapVersusRaceStats;
using W3ChampionsStatisticService.PlayerProfiles;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.Matches;
using W3ChampionsStatisticService.ReadModelBase;
using W3ChampionsStatisticService.PadEvents;

namespace W3ChampionsStatisticService.Achievements {

    //TODO: refactor this to work with the individual achievements
    // their update logic will be part of their class
     public class AchievementRepositoryHandler : IReadModelHandler  {

        private IAchievementRepository _achievementRepository;
        private readonly IMatchRepository _matchRepository;
        private readonly IPlayerRepository _playerRepository;
        private readonly IPlayerStatsRepository _playerStatsRepository;

        // in order to add more achievements add the ID here, and update
        // UpdateCurrentPlayerAchievementList and UpdateCurrentPlayerAchievements
        private long[] ActiveAchievementIds = {0, 1};

        public AchievementRepositoryHandler(
            IAchievementRepository achievementRepository,
            IMatchRepository matchRepository,
            IPlayerRepository playerRepository,
            IPlayerStatsRepository playerStatsRepository) {
            _achievementRepository = achievementRepository;
            _matchRepository = matchRepository;    
            _playerRepository = playerRepository;
            _playerStatsRepository = playerStatsRepository;
        }

        public async Task Update(MatchFinishedEvent nextEvent){
            try {
                if(nextEvent.WasFakeEvent){return;}
                var matchup = await GetMatchupFromMatch(nextEvent.match);
                var teams = matchup.Teams;
                foreach(Team team in teams){
                    foreach(PlayerOverviewMatches player in team.Players){
                        var needsUpdateWithCurrentMatchup = true;
                        var battleTag = player.BattleTag;
                        var playerProfile = await _playerRepository.LoadPlayerProfile(battleTag);
                        var playerAchievements = await _achievementRepository.GetPlayerAchievements(battleTag);

                        if (playerAchievements == null){
                            playerAchievements = await CreateNewPlayerAchievements(playerProfile);
                            needsUpdateWithCurrentMatchup = false;
                        }

                        if (needsUpdateWithCurrentMatchup){
                            var matchups = new List<Matchup>{matchup};
                            for(int i = 0; i < playerAchievements.PlayerAchievementList.Count; i++){
                                playerAchievements.PlayerAchievementList[i].Update(playerProfile, matchups);
                            }
                        } 
                        
                        if (playerAchievements.PlayerAchievementList.Count < ActiveAchievementIds.Length) {
                            playerAchievements = await AddAdditionalAchievements(playerAchievements, playerProfile);
                        }

                        await _achievementRepository.UpsertPlayerAchievements(playerAchievements);
                    }
                }
            }catch(Exception e){
                Console.WriteLine($"Exception occured when attempting to update player achievements: {e}");
            }
        }

        private async Task<PlayerAchievements> AddAdditionalAchievements(PlayerAchievements playerAchievements, PlayerOverallStats playerProfile){
            var achievementsToAdd = UpdateCurrentPlayerAchievementList(playerAchievements.PlayerAchievementList);
            var matches = await GetAllPlayerMatches(playerProfile);
            for(int i = 0; i < achievementsToAdd.Count; i++){
                achievementsToAdd[i].Update(playerProfile, matches);
                playerAchievements.PlayerAchievementList.Add(achievementsToAdd[i]);
            }
            return playerAchievements;
        } 

        private async Task<Matchup> GetMatchupFromMatch(Match match){
            var id = ObjectId.Parse(match.id);
            var matchupDetail = await _matchRepository.LoadDetails(id);
            return matchupDetail.Match;
        }

        public async Task<PlayerAchievements> GetPlayerAchievementsFromUI(string battleTag){
            var needsSave = false;
            var playerAchievements = await _achievementRepository.GetPlayerAchievements(battleTag);
            if (playerAchievements == null){
                var playerProfile = await _playerRepository.LoadPlayerProfile(battleTag);
                if (playerProfile != null){
                    playerAchievements = await CreateNewPlayerAchievements(playerProfile);
                    needsSave = true;
                } else {
                    return null;
                } 
            } else {
                if (playerAchievements.PlayerAchievementList.Count < ActiveAchievementIds.Length){
                    var achievementsToAdd = UpdateCurrentPlayerAchievementList(playerAchievements.PlayerAchievementList);
                    var playerProfile = await _playerRepository.LoadPlayerProfile(battleTag);
                    playerAchievements = await AddAdditionalAchievements(playerAchievements, playerProfile);
                    needsSave = true;
                }
            }
            if(needsSave){await _achievementRepository.UpsertPlayerAchievements(playerAchievements);}
            return playerAchievements;
        }

        private List<int> ConvertSeasonsToSimpleList(List<Season> seasons){
            var seasonList = new List<int>();
            foreach (Season s in seasons){seasonList.Add(s.Id);}
            seasonList.Reverse();
            return seasonList;
        }

        private List<Achievement> UpdateCurrentPlayerAchievementList(List<Achievement> currentAchievementsList){
            if (currentAchievementsList == null) {currentAchievementsList = new List<Achievement>();}
                var currentListIds = new List<long>();
                foreach(Achievement achievement in currentAchievementsList){
                    currentListIds.Add(achievement.Id);
                }
                foreach(long activeAchievementsId in ActiveAchievementIds){
                    if(!currentListIds.Contains(activeAchievementsId)){
                        switch (activeAchievementsId) {
                            case 0:
                            currentAchievementsList.Add(new MapWith25WinsAchievement());
                            break;
                            case 1:
                            currentAchievementsList.Add(new Win10GamesWithATPartnerAchievement());
                            break;
                        }

                    }
            }
            return currentAchievementsList;
        }

        private async Task<List<Matchup>> GetAllPlayerMatches(PlayerOverallStats playerOverallStats){
            var playerMatches = new List<Matchup>();
            var battleTag = playerOverallStats.BattleTag;
            var playerRaceOnMapVersusRaceRatios = new List<PlayerRaceOnMapVersusRaceRatio>();
            var seasons = ConvertSeasonsToSimpleList(playerOverallStats.ParticipatedInSeasons);

            foreach(int s in seasons){
                var playerRaceOnMapVersusRaceRatio = await _playerStatsRepository.LoadMapAndRaceStat(battleTag, s);
                playerRaceOnMapVersusRaceRatios.Add(playerRaceOnMapVersusRaceRatio);
                var seasonalMatches = await _matchRepository.LoadFor(battleTag, null, GateWay.Undefined, GameMode.Undefined, 100, 0, s);

                foreach(Matchup matchup in seasonalMatches){
                    playerMatches.Add(matchup);
                }
            }
            return playerMatches;
        }

        private async Task<PlayerAchievements> CreateNewPlayerAchievements(PlayerOverallStats playerOverallStats){
            var newPlayerAchievements = new PlayerAchievements();
            var playerMatches = await GetAllPlayerMatches(playerOverallStats);
            newPlayerAchievements.PlayerAchievementList = UpdateCurrentPlayerAchievementList(null);
            for(int i = 0; i < newPlayerAchievements.PlayerAchievementList.Count; i++){
                var achievement = newPlayerAchievements.PlayerAchievementList[i];
                achievement.Update(playerOverallStats, playerMatches);
                newPlayerAchievements.PlayerAchievementList[i] = achievement;
            }
            return newPlayerAchievements;
        }
    }
}