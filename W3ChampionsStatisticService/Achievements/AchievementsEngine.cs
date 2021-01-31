using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using W3ChampionsStatisticService.Ladder;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.Services;
using W3ChampionsStatisticService.Matches;
using Microsoft.AspNetCore.Mvc;
using W3ChampionsStatisticService.Achievements.Models;
using W3ChampionsStatisticService.CommonValueObjects;
using Newtonsoft.Json;

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

        public async void Run(string battleTag) {
            List<Matchup> allPlayerMatchups = new List<Matchup>(); // this will hold every season's match on specific maps
            var seasons = await GetAllSeasons(); // we want to get the match data across all seasons, let's get all of them 

            foreach (Season season in seasons){
              // not sure if we should define the page size of 100 or something else.....
              var playerMatches = await GetAllPlayerMatches(battleTag, season.Id, null, GameMode.Undefined,GateWay.Undefined,0,100);
              foreach(Matchup matchup in playerMatches){
                allPlayerMatchups.Add(matchup);
              }
            }

            var matchupDictionary = CreatePlayerMapDictionary(allPlayerMatchups, battleTag); // holds all match records
            var definedAchievements = GetDefinedAchievements(); // achievements available as created by the W3C team
            var playerAchievements = GetAllPlayerAchievements(definedAchievements, matchupDictionary, battleTag);
            Dump(playerAchievements);
        }

        private Dictionary<string,Dictionary<string,string>> GetAllPlayerAchievements( List<Achievement> definedAchievements,
         Dictionary<string, Dictionary<string,long>> matchupDictionary, string battleTag) {
            Dictionary<string,Dictionary<string,string>> playerAchievements = 
              new Dictionary<string,Dictionary<string,string>>{}; // this will hold all the player achievements that have been gained
            foreach(Achievement achievement in definedAchievements){
              var type = achievement.Type;
              switch (type) {
                case "map":
                  foreach(string map in matchupDictionary.Keys){
                    if (GetPlayerAchievedMapAchievements(achievement, matchupDictionary[map])){
                      if (!playerAchievements.ContainsKey("map")){
                        string achievementString = "Achieved! " + battleTag;
                        string achievementCommentString = achievement.caption + map;
                        playerAchievements["map"] = new Dictionary<string,string>{{achievementString, achievementCommentString}};
                      }
                    }
                  }
                  break;
              }
            }
            return playerAchievements;
        }

        private bool GetPlayerAchievedMapAchievements(Achievement achievement, Dictionary<string,long> mapStats) {
          var rules = achievement.Rules;
          foreach(string rule in rules){
            var ruleChunks = SplitRule(rule);
            var val1 = mapStats[ruleChunks[0]]; // expecting "wins", "losses", or "amountoftimeplayed"
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

        private Dictionary<string,Dictionary<string,long>> CreatePlayerMapDictionary(List<Matchup> matches, string battleTag) {
            Dictionary<string,Dictionary<string,long>> matchupDictionary = new Dictionary<string,Dictionary<string,long>>{};
            foreach(Matchup matchup in matches) {
                var map = matchup.Map;    
                var teams = matchup.Teams;
                var duration = matchup.DurationInSeconds;
                if (!matchupDictionary.ContainsKey(map)) {
                  matchupDictionary[map] =  new Dictionary<string,long>(){{"win", 0}, {"loss", 0}, {"amountOfTimePlayed", 0}};
                }
                foreach(Team team in teams) {
                  if (team.Won) {
                    if (DidOurPlayerWin(team, battleTag)) {
                      matchupDictionary[map]["win"] += 1;
                    } else {
                      matchupDictionary[map]["loss"] += 1;
                    }
                  }
                }
                matchupDictionary[map]["amountOfTimePlayed"] += duration;
            }
            return matchupDictionary;
        }

        private bool DidOurPlayerWin( Team team, string battleTag){
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
            //var matches = await _matchRepository.LoadFor(playerId, opponentId, GateWay.Undefined, GameMode.Undefined, pageSize, offset, season);
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