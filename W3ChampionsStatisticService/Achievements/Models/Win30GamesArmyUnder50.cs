using System;
using System.Linq;
using System.Collections.Generic;
using W3ChampionsStatisticService.PlayerProfiles;
using W3ChampionsStatisticService.Matches;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.PadEvents;


namespace W3ChampionsStatisticService.Achievements.Models {
    public class Win30GamesWithArmyUnder50: Achievement {

        public Win30GamesWithArmyUnder50() {
            Type = "detail";
            Id = 3;
            Title = "Win 30 Games With An Army Size Under 50 Units";
            Caption = "Player has yet to win 30 games with an army size under 50 units.";
            ProgressCurrent = 0;
            ProgressEnd = 30;
            Completed = false;
        }

        override public void UpdateFromMatchupDetails(PlayerOverallStats playerOverallStats, List<MatchupDetail> matchupDetails) {
            if(Completed){return;}
            var battleTag = playerOverallStats.BattleTag;
            foreach(MatchupDetail matchupDetail in matchupDetails){
                if(Completed){break;}
                var teams = matchupDetail.Match.Teams;
                if(!base.PlayerDidWin(battleTag, teams)){continue;}
                var playerScores = matchupDetail.PlayerScores;
                foreach(PlayerScore playerScore in playerScores){
                    if(playerScore.BattleTag != battleTag){continue;}
                    var unitScore = playerScore.UnitScore;
                    if (unitScore.LARGEST_ARMY > 50) {
                        ProgressCurrent += 1;
                    }
                    if (ProgressCurrent == ProgressEnd) {
                        Completed = true;
                        Caption = "Player has won 50 games with an army size of less than 50.";
                        break;
                    }
                }
            }
        }
    }
}