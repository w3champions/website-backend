using System.Collections.Generic;
using W3ChampionsStatisticService.PlayerProfiles;
using W3ChampionsStatisticService.Matches;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.PadEvents;

namespace W3ChampionsStatisticService.Achievements.Models {
    public class Use10DifferentHeros: Achievement {

        public Use10DifferentHeros() {
            Type = "detail";
            Id = 5;
            Title = "Use 10 Different Heros.";
            Caption = "Player has yet to use 10 different heros.";
            ProgressCurrent = 0;
            ProgressEnd = 10;
            Completed = false;
            Counter = new Dictionary<string, int>();
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
                    var heroes = playerScore.Heroes;
                    foreach(Hero hero in heroes) {
                        if (!Counter.ContainsKey(hero.icon)){
                            Counter[hero.icon] = 1;
                        }
                    }
                    ProgressCurrent = Counter.Keys.Count;
                    if (ProgressCurrent >= ProgressEnd) {
                        ProgressCurrent = ProgressEnd;
                        Completed = true;
                        Caption = "Player has used 10 different heros.";
                        break;
                    }
                }
            }
        }
    }
}