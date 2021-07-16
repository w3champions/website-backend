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
        }

        new public void Update(Achievement playerAchievement, PlayerOverallStats playerOverallStats, List<Matchup> matches) {
/*             var firstMapTo25Wins = "";
            foreach(Matchup matchup in matches){
                var map = matchup.Map;
                var teams = matchup.Teams;
                    if(PlayerDidWin(battleTag, teams)){
                        var hitWinsLimit = AddToWinsCount(achievementProgressCounter, map, 25);
                        if(playerAchievement.ProgressCurrent < playerAchievement.ProgressEnd){
                            playerAchievement.ProgressCurrent = CheckMostWins(achievementProgressCounter);
                        }
                        if (hitWinsLimit){firstMapTo25Wins = map; break;}
                    }
            }
            if(firstMapTo25Wins != ""){
                playerAchievement.Caption = $"Player has completed this achievement with 25 games won on {firstMapTo25Wins}";
                playerAchievement.Completed = true;
            } */
        }
    }
}