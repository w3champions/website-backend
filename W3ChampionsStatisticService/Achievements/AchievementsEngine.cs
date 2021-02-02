using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using W3ChampionsStatisticService.Ladder;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.Services;
using W3ChampionsStatisticService.Matches;
using W3ChampionsStatisticService.Achievements.Models;
using W3ChampionsStatisticService.CommonValueObjects;

namespace W3ChampionsStatisticService.Achievements
{
    public class AchievementsEngine : ControllerBase

    {
        private string achievementsJsonFile = "./Achievements/Achievements.json";
        private readonly IClanRepository _clanRepository;
        private readonly TrackingService _trackingService;
        private readonly IRankRepository _rankRepository;
        private readonly IMatchRepository _matchRepository;

        public AchievementsEngine(IClanRepository clanRepository,
            IRankRepository rankRepository,
            TrackingService trackingService,
            IMatchRepository matchRepository)
        {
            _clanRepository = clanRepository;
            _trackingService = trackingService;
            _rankRepository = rankRepository;
            _matchRepository = matchRepository;
        }

        public async Task<Dictionary<string, List<Dictionary<string,string>>>> Run(string battleTag) {
            List<Matchup> allPlayerMatchups = new List<Matchup>(); // this will hold every season's match on specific maps
            var seasons = await GetAllSeasons(); // we want to get the match data across all seasons, let's get all of them 

            foreach (Season season in seasons){
              // not sure if we should define the page size of 100 or something else.....
              var playerMatches = await GetAllPlayerMatches(battleTag, season.Id, null, GameMode.Undefined,GateWay.Undefined,0,100);
              foreach(Matchup matchup in playerMatches){
                allPlayerMatchups.Add(matchup);
              }
            }

            var perMapData = ExportPlayerMapData(allPlayerMatchups, battleTag); // holds all match records
            ExportPlayerArrangedTeamData(allPlayerMatchups, battleTag); // holds all match records
            var definedAchievements = GetDefinedAchievements(); // achievements available as created by the W3C team
            var playerAchievements = GetAllPlayerAchievements(definedAchievements, perMapData, battleTag);

            return playerAchievements;
        }

        private Dictionary<string,List<Dictionary<string,string>>> GetAllPlayerAchievements(
           List<Achievement> definedAchievements,
           PlayerMatchupMapData matchupDictionary, 
           string battleTag) 
           {
            Dictionary<string,List<Dictionary<string,string>>> playerAchievements = 
              new Dictionary<string,List<Dictionary<string,string>>>{}; // this will hold all the player achievements that have been gained

            foreach(Achievement achievement in definedAchievements){
              var type = achievement.Type;
              switch (type) {
                case "map":
                
                  foreach(string map in matchupDictionary.PerMapData.Keys){

                    if (GetPlayerAchievedMapAchievements(achievement, matchupDictionary.PerMapData[map])){

                      if (!playerAchievements.ContainsKey("map")){
                        playerAchievements["map"] = new List<Dictionary<string,string>>();
                      }

                      var achievementTitle = achievement.Title;
                      string achievementCommentString = achievement.caption + map;
                      playerAchievements["map"].Add(new Dictionary<string,string>{{achievementTitle, achievementCommentString}});
                    }

                  }
                  break;
               // case "player":
               // 
               //   foreach(string map in matchupDictionary.Keys){

               //     if (GetPlayerAchievedMapAchievements(achievement, matchupDictionary[map])){

               //       if (!playerAchievements.ContainsKey("map")){
               //         playerAchievements["map"] = new List<Dictionary<string,string>>();
               //       }

               //       var achievementTitle = achievement.Title;
               //       string achievementCommentString = achievement.caption + map;
               //       playerAchievements["map"].Add(new Dictionary<string,string>{{achievementTitle, achievementCommentString}});
               //     }

               //   }

               //   break;
              }
            }
            return playerAchievements;
        }

        private long? TranslatePartOfRule(string fromRule, PlayerMatchupPerMapData mapStats) {
          switch (fromRule){
            case "wins":
              return mapStats.NumberOfWins;
            case "losses":
              return mapStats.NumberOfLosses;
            case "amountoftimeplayed":
              return mapStats.AmountOfTimePlayedInSeconds;
            default:
              break;
          }
          return null;
        }

        private bool GetPlayerAchievedMapAchievements(Achievement achievement, PlayerMatchupPerMapData mapStats) {
          var rules = achievement.Rules;
          foreach(string rule in rules){
            var ruleChunks = SplitRule(rule);
            var translation = TranslatePartOfRule(ruleChunks[0], mapStats); // expecting "wins", "losses", or "amountoftimeplayed"
            var val1 = new long();
            if (translation != null) {
              val1 = translation.GetValueOrDefault();
            }
            var logicOperatorString = ruleChunks[1]; // expecting ">" || "<" || ">=" || "<=" || "=="
            var val2 = long.Parse(ruleChunks[2]); // expecting an int value so we can convert it
            if (!CheckRuleBasedOnLogic(val1, val2, logicOperatorString)) {
              return false;
            }
          }
          return true;
        }

        private bool CheckRuleBasedOnLogic(long val1, long val2, string logicOperator) {
          switch (logicOperator){
            case ">":
              if (val1 > val2){ return true; }; break; 
            case "<": 
              if (val1 < val2){ return true; }; break;
            case ">=": 
              if (val1 >= val2){ return true; }; break;
            case "<=": 
              if (val1 <= val2){ return true; }; break;
            case "==":
              if (val1 == val2){ return true; }; break;
          }
          return false;
        }

        private string[] SplitRule(string rule) {
          return rule.Split(' ');
        }

        private PlayerMatchupMapData ExportPlayerMapData(List<Matchup> matches, string battleTag) {
            PlayerMatchupMapData mapData = new PlayerMatchupMapData();
            mapData.PerMapData = new Dictionary<string,PlayerMatchupPerMapData>();
            Dump(mapData);
            foreach(Matchup matchup in matches) {
                var map = matchup.Map;    
                var teams = matchup.Teams;
                var duration = matchup.DurationInSeconds;
                if (!mapData.PerMapData.ContainsKey(map)) {
                  PlayerMatchupPerMapData perMapData = new PlayerMatchupPerMapData();
                  perMapData.NumberOfWins = 0;
                  perMapData.NumberOfLosses = 0;
                  perMapData.AmountOfTimePlayedInSeconds = 0;
                  mapData.PerMapData[map] = perMapData;
                }
                foreach(Team team in teams) {
                  if (team.Won) {
                    if (OurPlayerOnTeam(team, battleTag)) {
                      mapData.PerMapData[map].NumberOfWins += 1;
                    } else {
                      mapData.PerMapData[map].NumberOfLosses += 1;
                    }
                  }
                }
                mapData.PerMapData[map].AmountOfTimePlayedInSeconds += duration;
            }
            return mapData;
        }
        //TODO: working here need to probe out the arranged team data
        private void ExportPlayerArrangedTeamData(List<Matchup> matches, string battleTag) {
          Dictionary<string,Dictionary<string,long>> arrangedTeamData = new Dictionary<string,Dictionary<string,long>>{};
          foreach(Matchup matchup in matches) {
            var gameMode = matchup.GameMode;   // just need to search for gameMode = 6 --- that is AT 2v2
            if (gameMode != GameMode.GM_2v2) { // looks like 2v2_GM is actually AT, probably should check later
              continue;
            }
            var map = matchup.Map;    
            var teams = matchup.Teams;
            var duration = matchup.DurationInSeconds;
            foreach(Team team in teams) {
              // look through the teams and see who was on our player's team
              // add the teammate to the arrangedTeamData, and add the counts per game
              if (OurPlayerOnTeam(team, battleTag)) {
                // look through each team player and add them to our list.
                // if they are already on the list, then add to how many times we saw him
                var teamMates = GetPlayerTeamMates(team, battleTag);
              }
            }
          }
        }

        private List<string> GetPlayerTeamMates(Team team, string battleTag) {
          List<string> teamMates = new List<string>();
          var players = team.Players;
          foreach(PlayerOverviewMatches player in players) {
            if (player.BattleTag != battleTag) {
              teamMates.Add(player.BattleTag);
            }
          }
          return teamMates;
        }

        private bool OurPlayerOnTeam( Team team, string battleTag){
          foreach(PlayerOverviewMatches player in team.Players) {
            if (player.BattleTag == battleTag) {
              return true;
            }
          }
          return false;
        }

        private async Task<List<Matchup>> GetAllPlayerMatches(
            string playerId,
            int season,
            string opponentId = null,
            GameMode gameMode = GameMode.Undefined,
            GateWay gateWay = GateWay.Undefined,
            int offset = 0,
            int pageSize = 100)
        {
            if (pageSize > 100) pageSize = 100;
            var matches = await _matchRepository.LoadFor(playerId, opponentId, gateWay, gameMode, pageSize, offset, season);
            return matches;
        }

        private List<Achievement> GetDefinedAchievements() {
            var achievementsJson = System.IO.File.ReadAllText(achievementsJsonFile);
            List<Achievement> achievements = JsonConvert.DeserializeObject<List<Achievement>>(achievementsJson);
            return achievements;
        }

        private async Task<List<Season>> GetAllSeasons () {
            var seasons =  await _rankRepository.LoadSeasons();
            return seasons;
        }

        // this is used soley for debugging purposes
        private static void Dump(object o) {
            string json = JsonConvert.SerializeObject(o, Formatting.Indented);
            Console.WriteLine(json);
        }
    }
}