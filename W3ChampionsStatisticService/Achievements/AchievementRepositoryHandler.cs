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
     public class AchievementRepositoryHandler : IReadModelHandler  {

        private IAchievementRepository _achievementRepository;
        private readonly IMatchRepository _matchRepository;
        private readonly IPlayerRepository _playerRepository;
        private readonly IPlayerStatsRepository _playerStatsRepository;

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
                            var playerMatchDetails = new List<MatchupDetail>();
                            playerAchievements.PlayerAchievementList = await RunAchievementUpdates(playerProfile, playerAchievements.PlayerAchievementList, matchups);
                        } 
                        
                        if (playerAchievements.PlayerAchievementList.Count < AchievementEvaluator.AllActiveAchievements.Count) {
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
            var achievementsToAdd = GetMissingAchievements(playerAchievements.PlayerAchievementList);
            var matches = await GetAllPlayerMatches(playerProfile);
            achievementsToAdd = await RunAchievementUpdates(playerProfile, achievementsToAdd, matches);
            foreach(Achievement achievement in achievementsToAdd){
                playerAchievements.PlayerAchievementList.Add(achievement);
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
                if (playerAchievements.PlayerAchievementList.Count < AchievementEvaluator.AllActiveAchievements.Count){
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

        private List<Achievement> GetMissingAchievements(List<Achievement> currentAchievementsList){
            var currentAchievementIds = new List<long>();
            foreach(Achievement achievement in currentAchievementsList){
                currentAchievementIds.Add(achievement.Id);
            }
            var newAchievementsList = new List<Achievement>();
            foreach(Achievement achievement in AchievementEvaluator.AllActiveAchievements){
                if (!currentAchievementIds.Contains(achievement.Id)) {
                    newAchievementsList.Add(achievement);
                }
            }
            return newAchievementsList;
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
        private async Task<MatchupDetail> GetMatchupDetail(Matchup matchup) {
            var matchupId = matchup.Id;
            var matchupDetails = await _matchRepository.LoadDetails(matchupId);
            return matchupDetails;
        }

        private async Task<List<Achievement>> RunAchievementUpdates(
            PlayerOverallStats playerOverallStats,
            List<Achievement> playerAchievements,
            List<Matchup> playerMatches) {
                var playerMatchDetails = new List<MatchupDetail>();
                foreach(Achievement achievement in playerAchievements){
                    switch (achievement.Type) {
                    case "detail":
                    if (playerMatchDetails.Count == 0){
                        foreach(Matchup playerMatch in playerMatches) {
                            playerMatchDetails.Add(await GetMatchupDetail(playerMatch));
                        }
                    }
                    achievement.UpdateFromMatchupDetails(playerOverallStats, playerMatchDetails);
                    break;
                    default:
                    achievement.UpdateFromMatchups(playerOverallStats, playerMatches);
                    break;
                    }
                }
                return playerAchievements;
        }

        private async Task<PlayerAchievements> CreateNewPlayerAchievements(PlayerOverallStats playerOverallStats){
            var newPlayerAchievements = new PlayerAchievements();
            newPlayerAchievements.PlayerId = playerOverallStats.BattleTag;
            newPlayerAchievements.PlayerAchievementList = AchievementEvaluator.AllActiveAchievements;
            var playerMatches = await GetAllPlayerMatches(playerOverallStats);
            newPlayerAchievements.PlayerAchievementList = await RunAchievementUpdates(playerOverallStats, newPlayerAchievements.PlayerAchievementList, playerMatches);
            return newPlayerAchievements;
        }
    }
}