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
using W3ChampionsStatisticService.Matches;
using W3ChampionsStatisticService.ReadModelBase;
using W3ChampionsStatisticService.PadEvents;

namespace W3ChampionsStatisticService.Achievements {

     public class AchievementRepositoryHandler : IReadModelHandler  {

        private readonly IAchievementRepository _achievementRepository;
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

        public async Task Update(MatchFinishedEvent nextEvent) {
            //TODO: get this to work when match is finished....
             if (nextEvent == null || nextEvent.match == null || nextEvent.result == null) {
                 return;
             }
        } 

        public async Task<PlayerAchievements> GetPlayerAchievements(string playerId){
            var playerAchievements = await _achievementRepository.GetPlayerAchievements(playerId);
            if (playerAchievements == null || playerAchievements.PlayerAchievementList.Count < ActiveAchievementIds.Length){
                // check if the player exists....
                var playerProfile = await _playerRepository.LoadPlayerProfile(playerId);
                if (playerProfile != null){
                    playerAchievements = await CreateNewPlayerAchievements(playerProfile);
                    await _achievementRepository.UpsertPlayerAchievements(playerAchievements);
                } else {
                    // get the newly achievement(s)
                    playerAchievements.PlayerAchievementList = UpdateCurrentPlayerAchievementList(playerAchievements.PlayerAchievementList);
                    // update the whole achievement object
                    //TODO: fix this because its going to run through all achievements again.....
                    playerAchievements = await UpdateCurrentPlayerAchievements(playerAchievements, playerProfile, true);
                }
            }

            return playerAchievements;
        }

        private List<int> ConvertSeasonsToSimpleList(List<Season> seasons) {
            var seasonList = new List<int>();
            foreach (Season s in seasons){seasonList.Add(s.Id);}
            seasonList.Reverse();
            return seasonList;
        }

        private List<Achievement> UpdateCurrentPlayerAchievementList(List<Achievement> currentAchievementsList) {
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

                foreach(Matchup matchup in seasonalMatches) {
                    playerMatches.Add(matchup);
                }
            }
            return playerMatches;
        }

        private async Task<PlayerAchievements> UpdateCurrentPlayerAchievements(PlayerAchievements playerAchievements, PlayerOverallStats playerOverallStats, bool needsNewAchievementUpdate){

            var playerMatches = new List<Matchup>();

            if(needsNewAchievementUpdate){
                playerMatches = await GetAllPlayerMatches(playerOverallStats);
            } else {
                // TODO:
                // playerMatches will be the [ match ] that was just completed........
            }
            var battleTag = playerAchievements.PlayerId;

            foreach(Achievement achievement in playerAchievements.PlayerAchievementList) {
                if(achievement.Completed){continue;}
                var achievementProgressCounter = new Dictionary<string, int>();
                if(!needsNewAchievementUpdate){achievementProgressCounter = achievement.Counter;}
                switch(achievement.Id){
                    case 0:
                        var firstMapTo25Wins = "";
                        foreach(Matchup matchup in playerMatches){
                            var map = matchup.Map;
                            var teams = matchup.Teams;
                            if(PlayerDidWin(battleTag, teams)) {
                                var hitWinsLimit = AddToWinsCount(achievementProgressCounter, map, 25);
                                if(achievement.ProgressCurrent < achievement.ProgressEnd){
                                    achievement.ProgressCurrent = CheckMostWins(achievementProgressCounter);
                                }
                                if (hitWinsLimit){firstMapTo25Wins = map; break;}
                            }
                        }
                        if(firstMapTo25Wins != ""){
                            achievement.Caption = $"Player has completed this achievement with 25 games won on {firstMapTo25Wins}";
                            achievement.Completed = true;
                        }
                        break;
                    case 1:
                        var firstPartnerTo10Wins = "";
                        foreach(Matchup matchup in playerMatches){
                            if (matchup.GameMode != GameMode.GM_2v2_AT){continue;}
                            if (PlayerDidWin(battleTag, matchup.Teams)){
                                var teamMate = GetPlayerTeamMate(battleTag, matchup.Teams);
                                var hitWinsLimit = AddToWinsCount(achievementProgressCounter, teamMate, 10);
                                if(achievement.ProgressCurrent < achievement.ProgressEnd){
                                    achievement.ProgressCurrent = CheckMostWins(achievementProgressCounter);
                                }
                                if(hitWinsLimit){firstPartnerTo10Wins = teamMate; break;}
                            }
                        }
                        if(firstPartnerTo10Wins != "") {
                            achievement.Caption = $"Player has completed this achievement with {firstPartnerTo10Wins}";
                            achievement.Completed = true;
                        }
                        break;
                }
                achievement.Counter = achievementProgressCounter;
            }
            return playerAchievements;
        }

        private string GetPlayerTeamMate(string battleTag, IList<Team> teams){
            foreach(Team team in teams){
                var players = team.Players;
                foreach(PlayerOverviewMatches player in players){
                    if(player.BattleTag != battleTag && player.Won){
                        return player.BattleTag;
                    }
                }
            }
            return string.Empty;
        }

        private long CheckMostWins(Dictionary<string,int> winsCount){
            long maxValue = 0;
            foreach(var wins in winsCount){
                if(wins.Value > maxValue){ maxValue = wins.Value;}
            }
            return maxValue;
        }

        private bool AddToWinsCount(Dictionary<string,int> winsCount, string unit, int maxCount) {
            var didReachMaxCount = false;
            if(!winsCount.ContainsKey(unit)) {
                winsCount.Add(unit, 1);
            } else {
                winsCount[unit] += 1;
                if (winsCount[unit] == maxCount) {
                    didReachMaxCount = true;
                }
            }
            return didReachMaxCount;
        }

        private bool PlayerDidWin(string battleTag, IList<Team> teams) {
            foreach(Team team in teams) {
                var players = team.Players;
                foreach(PlayerOverviewMatches player in players){
                    var playerName = player.BattleTag;
                    if (playerName == battleTag){return player.Won;}
                }
            }
            return false;
        }

        private async Task<PlayerAchievements> CreateNewPlayerAchievements(PlayerOverallStats playerOverallStats) {
            var newPlayerAchievements = new PlayerAchievements();
            newPlayerAchievements.PlayerId = playerOverallStats.BattleTag;
            newPlayerAchievements.PlayerAchievementList = UpdateCurrentPlayerAchievementList(null);
            newPlayerAchievements = await UpdateCurrentPlayerAchievements(newPlayerAchievements, playerOverallStats, true);
            return newPlayerAchievements;
        }
    }
}