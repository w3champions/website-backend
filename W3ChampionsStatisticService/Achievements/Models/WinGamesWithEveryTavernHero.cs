using System.Linq;
using System.Collections.Generic;
using W3ChampionsStatisticService.PlayerProfiles;
using W3ChampionsStatisticService.Matches;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.PadEvents;


namespace W3ChampionsStatisticService.Achievements.Models {
    public class WinGamesWithEveryTavernHero: Achievement {

        private string[] TavernHeros;

        public WinGamesWithEveryTavernHero() {
            TavernHeros = new string[]{ "alchemist", "seawitch", "tinker", "beastmaster", "bansheeranger", "firelord", 
            "pandarenbrewmaster", "pitlord" };
            Type = "detail";
            Id = 2;
            Title = "Win Games With Every Tavern Hero";
            Caption = "Player has yet to win using every Tavern Hero.";
            ProgressCurrent = 0;
            ProgressEnd = 8;
            Completed = false;
            Counter = new Dictionary<string, int>();
        }

        override public void UpdateFromMatchupDetails(PlayerOverallStats playerOverallStats, List<MatchupDetail> matchupDetails) {
            if(Completed){return;}
            var battleTag = playerOverallStats.BattleTag;
            foreach(MatchupDetail matchupDetail in matchupDetails){
                 var teams = matchupDetail.Match.Teams;
                 if(!base.PlayerDidWin(battleTag, teams)){continue;}
                 var playerScores = matchupDetail.PlayerScores;
                 foreach(PlayerScore playerScore in playerScores){
                    if(playerScore.BattleTag != battleTag){continue;}
                    var heroes = playerScore.Heroes;
                    foreach(Hero hero in heroes){
                        if (TavernHeros.Contains(hero.icon) && !Counter.ContainsKey(hero.icon)){
                            Counter[hero.icon] = 1;
                        }
                    }
                }
            }
            ProgressCurrent = Counter.Keys.Count;
            if (ProgressCurrent == ProgressEnd) {
                Completed = true;
            }
        }
    }
}