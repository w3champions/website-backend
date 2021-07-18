using System.Collections.Generic;
using W3ChampionsStatisticService.PlayerProfiles;
using W3ChampionsStatisticService.Matches;

namespace W3ChampionsStatisticService.Achievements.Models {
    public abstract class Achievement {
        public long Id {get; set;}
        public string Title {get; set;}
        public string Caption {get; set;}
        public long ProgressCurrent {get; set;}
        public long ProgressEnd {get; set;}
        public bool Completed {get; set;}
        public Dictionary<string,int> Counter {get; set;}
        abstract public void Update(PlayerOverallStats playerOverallStats, List<Matchup> matches);
        protected long CheckMostWins(Dictionary<string,int> winsCount){
            long maxValue = 0;
            foreach(var wins in winsCount){
                if(wins.Value > maxValue){ maxValue = wins.Value;}
            }
            return maxValue;
        }
        protected bool AddToWinsCount(Dictionary<string,int> winsCount, string unit, int maxCount) {
            var didReachMaxCount = false;
            if(!winsCount.ContainsKey(unit)){
                winsCount.Add(unit, 1);
            } else {
                winsCount[unit] += 1;
                if (winsCount[unit] == maxCount){
                    didReachMaxCount = true;
                }
            }
            return didReachMaxCount;
        }

        protected bool PlayerDidWin(string battleTag, IList<Team> teams){
            foreach(Team team in teams){
                var players = team.Players;
                foreach(PlayerOverviewMatches player in players){
                    var playerName = player.BattleTag;
                    if (playerName == battleTag){return player.Won;}
                }
            }
            return false;
        }

        protected string GetPlayerTeamMate(string battleTag, IList<Team> teams){
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
        
    }
}