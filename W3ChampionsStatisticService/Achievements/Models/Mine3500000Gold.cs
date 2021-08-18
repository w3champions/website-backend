using System.Collections.Generic;
using W3ChampionsStatisticService.PlayerProfiles;
using W3ChampionsStatisticService.Matches;
using W3ChampionsStatisticService.Ports;

namespace W3ChampionsStatisticService.Achievements.Models {
    public class Mine3500000Gold: Achievement {

        public Mine3500000Gold() {
            Type = "detail";
            Id = 4;
            Title = "Mine 3,500,000 Gold";
            Caption = "Player has yet to mine 3,500,000 Gold.";
            ProgressCurrent = 0;
            ProgressEnd = 3500000;
            Completed = false;
        }

        override public void UpdateFromMatchupDetails(PlayerOverallStats playerOverallStats, List<MatchupDetail> matchupDetails) {
            if(Completed){return;}
            var battleTag = playerOverallStats.BattleTag;
            foreach(MatchupDetail matchupDetail in matchupDetails){
                if(Completed){break;}
                var teams = matchupDetail.Match.Teams;
                var playerScores = matchupDetail.PlayerScores;
                if(playerScores == null){continue;} // it appears that some games listed could have null scores
                foreach(PlayerScore playerScore in playerScores){
                    if(playerScore.BattleTag != battleTag){continue;}
                    var resourceScore = playerScore.ResourceScore;
                        ProgressCurrent += resourceScore.GOLD_COLLECTED;
                    if (ProgressCurrent >= ProgressEnd) {
                        Completed = true;
                        Caption = "Player has collected 3,500,000 Gold.";
                        break;
                    }
                }
            }
        }
    }
}