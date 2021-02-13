using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json;
using W3ChampionsStatisticService.Ladder;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.Services;
using W3ChampionsStatisticService.Matches;
using W3ChampionsStatisticService.Achievements.Models;
using W3ChampionsStatisticService.CommonValueObjects;

namespace W3ChampionsStatisticService.Achievements
{
    public class AchievementsEngine

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

        public async Task<PlayerAchievements> Run(string battleTag)
        {
            var seasons = await GetAllSeasons();
            var allPlayerMatchups = await GetAllPlayerMatchups(seasons, battleTag);

            PlayerMatchupData allPlayerMatchData = new PlayerMatchupData();
            var perMapData = ExportPlayerMapData(allPlayerMatchups, battleTag); // holds all match map records
            var perTeamMateData = ExportPlayerArrangedTeamData(allPlayerMatchups, battleTag); // holds AT player team stats
            var definedAchievements = GetDefinedAchievements(); // achievements available as created by the W3C team

            allPlayerMatchData.PlayerMapData = perMapData;
            allPlayerMatchData.PlayerPartnerData = perTeamMateData;
            var playerAchievements = GetAllPlayerAchievements(definedAchievements, allPlayerMatchData, battleTag);

            return playerAchievements;
        }

        private async Task<List<Matchup>> GetAllPlayerMatchups(List<Season> seasons, string battleTag)
        {
            List<Matchup> allPlayerMatchups = new List<Matchup>();
            foreach (Season season in seasons)
            {
                // not sure if we should define the page size of 100 or something else.....
                var playerMatches = await GetAllPlayerMatches(battleTag, season.Id, null, GameMode.Undefined, GateWay.Undefined, 0, 100);
                foreach (Matchup matchup in playerMatches)
                {
                    allPlayerMatchups.Add(matchup);
                }
            }
            return allPlayerMatchups;
        }

        private PlayerAchievement CreateNewPlayerAchievement(string title, string caption)
        {
            PlayerAchievement playerAchievement = new PlayerAchievement();
            playerAchievement.Title = title;
            playerAchievement.Caption = caption;
            return playerAchievement;
        }

        private PlayerAchievements GetAllPlayerAchievements(
           List<Achievement> definedAchievements,
           PlayerMatchupData matchupDictionary,
           string battleTag)
        {
            PlayerAchievements playerAchievements = new PlayerAchievements();
            playerAchievements.MapAchievements = new List<PlayerAchievement>();
            playerAchievements.TeamAchievements = new List<PlayerAchievement>();
            foreach (Achievement achievement in definedAchievements)
            {
                var type = achievement.Type;
                switch (type)
                {
                    case "map":
                        foreach (string map in matchupDictionary.PlayerMapData.PerMapData.Keys)
                        {
                            if (GetPlayerAchievedMapAchievements(achievement, matchupDictionary.PlayerMapData.PerMapData[map]))
                            {
                                playerAchievements.MapAchievements.Add(
                                  CreateNewPlayerAchievement(achievement.Title, achievement.Caption + map));
                            }
                        }
                        break;
                    case "team":
                        var partnersData = matchupDictionary.PlayerPartnerData.PartnersAndRecord;
                        if (GetPlayerAchievedTeamAchievements(achievement, partnersData))
                        {
                            playerAchievements.TeamAchievements.Add(
                              CreateNewPlayerAchievement(achievement.Title, achievement.Caption));
                        }
                        break;
                }
            }
            return playerAchievements;
        }

        private bool GetPlayerAchievedTeamAchievements(
          Achievement achievement,
          Dictionary<string, PlayerAndTeamMateRecordData> partnerData)
        {
            var rules = achievement.Rules;
            foreach (string rule in rules)
            {
                var ruleChunks = SplitRule(rule);
                var translation = TranslatePartOfRule(ruleChunks[0], null, partnerData);
                var val1 = new long();
                if (translation != null)
                {
                    val1 = translation.GetValueOrDefault();
                }
                else
                {
                    return false;
                }
                var logicOperatorString = ruleChunks[1]; // expecting ">" || "<" || ">=" || "<=" || "=="
                var val2 = Convert.ToInt64(ruleChunks[2]);
                if (!CheckRuleBasedOnLogic(val1, val2, logicOperatorString))
                {
                    return false;
                }
            }
            return true;
        }

        private long? TranslatePartOfRule(                               // Will also need more translations for more rules
          string fromRule, PlayerMatchupPerMapData mapStats,             // only choose one, mapStats or partnerData
          Dictionary<string, PlayerAndTeamMateRecordData> partnerData)   // the other should be left null
        {
            switch (fromRule)
            {
                case "wins": return mapStats.NumberOfWins;
                case "losses": return mapStats.NumberOfLosses;
                case "amountoftimeplayed": return mapStats.AmountOfTimePlayedInSeconds;
                case "partnerCount": return Convert.ToInt64(partnerData.Keys.Count);
                default: break;
            }
            return null;
        }

        private bool GetPlayerAchievedMapAchievements(Achievement achievement, PlayerMatchupPerMapData mapStats)
        {
            var rules = achievement.Rules;
            foreach (string rule in rules)
            {
                var ruleChunks = SplitRule(rule);
                var translation = TranslatePartOfRule(ruleChunks[0], mapStats, null); // expecting "wins", "losses", or "amountoftimeplayed"
                var val1 = new long();
                if (translation != null)
                {
                    val1 = translation.GetValueOrDefault();
                }
                else
                {
                    return false;
                }
                var logicOperatorString = ruleChunks[1]; // expecting ">" || "<" || ">=" || "<=" || "=="
                var val2 = long.Parse(ruleChunks[2]); // expecting an int value so we can convert it
                if (!CheckRuleBasedOnLogic(val1, val2, logicOperatorString))
                {
                    return false;
                }
            }
            return true;
        }

        private bool CheckRuleBasedOnLogic(long val1, long val2, string logicOperator)
        {
            switch (logicOperator)
            {
                case ">":
                    if (val1 > val2) { return true; }; break;
                case "<":
                    if (val1 < val2) { return true; }; break;
                case ">=":
                    if (val1 >= val2) { return true; }; break;
                case "<=":
                    if (val1 <= val2) { return true; }; break;
                case "==":
                    if (val1 == val2) { return true; }; break;
            }
            return false;
        }

        private string[] SplitRule(string rule)
        {
            return rule.Split(' ');
        }

        private PlayerMatchupPerMapData CreateNewPlayerMatchupPerMapData(){
            PlayerMatchupPerMapData perMapData = new PlayerMatchupPerMapData();
            perMapData.NumberOfWins = 0;
            perMapData.NumberOfLosses = 0;
            perMapData.AmountOfTimePlayedInSeconds = 0;
            return perMapData;
        }

        private PlayerMatchupMapData ExportPlayerMapData(List<Matchup> matches, string battleTag)
        {
            PlayerMatchupMapData mapData = new PlayerMatchupMapData();
            mapData.PerMapData = new Dictionary<string, PlayerMatchupPerMapData>();
            foreach (Matchup matchup in matches)
            {
                var map = matchup.Map;
                var teams = matchup.Teams;
                var duration = matchup.DurationInSeconds;
                if (!mapData.PerMapData.ContainsKey(map))
                {
                    mapData.PerMapData[map] = CreateNewPlayerMatchupPerMapData();
                }
                foreach (Team team in teams)
                {
                    if (team.Won)
                    {
                        if (OurPlayerOnTeam(team, battleTag))
                        {
                            mapData.PerMapData[map].NumberOfWins += 1;
                        }
                        else
                        {
                            mapData.PerMapData[map].NumberOfLosses += 1;
                        }
                    }
                }
                mapData.PerMapData[map].AmountOfTimePlayedInSeconds += duration;
            }
            return mapData;
        }

        private PlayerMatchupPartnerData ExportPlayerArrangedTeamData(List<Matchup> matches, string battleTag)
        {
            PlayerMatchupPartnerData arrangedTeamData = new PlayerMatchupPartnerData();
            arrangedTeamData.PartnersAndRecord = new Dictionary<string, PlayerAndTeamMateRecordData>();
            foreach (Matchup matchup in matches)
            {
                var gameMode = matchup.GameMode;   // just need to search for gameMode = 6 --- that is AT 2v2
                if (gameMode != GameMode.GM_2v2) { continue; } // looks like 2v2_GM is actually AT, probably should check later
                var map = matchup.Map;
                var teams = matchup.Teams;
                var duration = matchup.DurationInSeconds;
                foreach (Team team in teams)
                {
                    if (OurPlayerOnTeam(team, battleTag))
                    {
                        var teamMates = GetPlayerTeamMates(team, battleTag);
                        foreach (string teamMate in teamMates)
                        {
                            if (teamMate == battleTag) { continue; }
                            if (!arrangedTeamData.PartnersAndRecord.ContainsKey(teamMate))
                            {
                                var newPartnerRecord = CreateNewPartnerRecord();
                                arrangedTeamData.PartnersAndRecord[teamMate] = newPartnerRecord;
                            }
                            if (team.Won)
                            {
                                arrangedTeamData.PartnersAndRecord[teamMate].NumberOfWins += 1;
                            }
                            else
                            {
                                arrangedTeamData.PartnersAndRecord[teamMate].NumberOfLosses += 1;
                            }
                            arrangedTeamData.PartnersAndRecord[teamMate].EstimatedGameTimeTogether += matchup.DurationInSeconds;
                        }
                    }
                }
            }
            return arrangedTeamData;
        }

        private PlayerAndTeamMateRecordData CreateNewPartnerRecord()
        {
            PlayerAndTeamMateRecordData newPartnerRecord = new PlayerAndTeamMateRecordData();
            newPartnerRecord.NumberOfWins = 0;
            newPartnerRecord.NumberOfLosses = 0;
            newPartnerRecord.EstimatedGameTimeTogether = 0;
            return newPartnerRecord;
        }

        private List<string> GetPlayerTeamMates(Team team, string battleTag)
        {
            List<string> teamMates = new List<string>();
            var players = team.Players;
            foreach (PlayerOverviewMatches player in players)
            {
                if (player.BattleTag != battleTag)
                {
                    teamMates.Add(player.BattleTag);
                }
            }
            return teamMates;
        }

        private bool OurPlayerOnTeam(Team team, string battleTag)
        {
            foreach (PlayerOverviewMatches player in team.Players)
            {
                if (player.BattleTag == battleTag)
                {
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

        private List<Achievement> GetDefinedAchievements()
        {
            var achievementsJson = System.IO.File.ReadAllText(achievementsJsonFile);
            List<Achievement> achievements = JsonConvert.DeserializeObject<List<Achievement>>(achievementsJson);
            return achievements;
        }

        private async Task<List<Season>> GetAllSeasons()
        {
            var seasons = await _rankRepository.LoadSeasons();
            return seasons;
        }

        private static void Dump(object o) // this is used soley for debugging purposes
        {
            string json = JsonConvert.SerializeObject(o, Formatting.Indented);
            Console.WriteLine(json);
        }
    }
}