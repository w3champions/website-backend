using System.Collections.Generic;
using W3ChampionsStatisticService.PlayerProfiles;
using W3ChampionsStatisticService.Matches;

namespace W3ChampionsStatisticService.Achievements.Models {
    public class MapWith25WinsAchievement: Achievement {

        public MapWith25WinsAchievement(){
            Id = 0;
            Title = "Win 25 Games On Any Map";
            Caption = "Player has yet to win 25 games on any map.";
            ProgressCurrent = 0;
            ProgressEnd = 25;
            Completed = false;
            Counter = new Dictionary<string, int>();
        }

        override public void Update(PlayerOverallStats playerOverallStats, List<Matchup> matches) {
            var battleTag = playerOverallStats.BattleTag;
            var firstMapTo25Wins = "";
            foreach(Matchup matchup in matches){
                var map = matchup.Map;
                var teams = matchup.Teams;
                    if(base.PlayerDidWin(battleTag, teams)){
                        var hitWinsLimit = base.AddToWinsCount(Counter, map, 25);
                        if(ProgressCurrent < ProgressEnd){
                            ProgressCurrent = base.CheckMostWins(Counter);
                        }
                        if (hitWinsLimit){firstMapTo25Wins = map; break;}
                    }
            }
            if(firstMapTo25Wins != ""){
                Caption = $"Player has completed this achievement with 25 games won on {firstMapTo25Wins}";
                Completed = true;
            }
        }
    }
}