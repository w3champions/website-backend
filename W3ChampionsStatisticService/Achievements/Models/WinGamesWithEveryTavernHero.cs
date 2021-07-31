using System.Linq;
using System.Collections.Generic;
using W3ChampionsStatisticService.PlayerProfiles;
using W3ChampionsStatisticService.Matches;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.PadEvents;


namespace W3ChampionsStatisticService.Achievements.Models {
    public class WinGamesWithEveryTavernHero: Achievement {
        private readonly IMatchRepository _matchRepository;
        private string[] TavernHeros;

        public WinGamesWithEveryTavernHero(IMatchRepository matchRepository){
            _matchRepository = matchRepository;
            TavernHeros = new string[]{ "alchemist", "seawitch", "tinker", "beastmaster", "bansheeranger", "firelord", 
            "pandarenbrewmaster", "pitlord" };
            Id = 2;
            Title = "Win Games With Every Tavern Hero";
            Caption = "Player has yet to win using every Tavern Hero.";
            ProgressCurrent = 0;
            ProgressEnd = 8;
            Completed = false;
            Counter = new Dictionary<string, int>();
        }

        override public async void Update(PlayerOverallStats playerOverallStats, List<Matchup> matches) {
            if(Completed){return;}
            var battleTag = playerOverallStats.BattleTag;
            foreach(Matchup matchup in matches){
                var teams = matchup.Teams;
                    if(base.PlayerDidWin(battleTag, teams)){
                        var id = matchup.Id;
                        var match = await _matchRepository.LoadDetails(id);
                        var playerScores = match.PlayerScores;
                        foreach(PlayerScore playerScore in playerScores){
                            if (playerScore.BattleTag == battleTag){
                                var heros = playerScore.Heroes;
                                foreach(Hero hero in heros) {
                                    if(TavernHeros.Contains(hero.icon)){
                                        if(!Counter.ContainsKey(hero.icon)){
                                            Counter[hero.icon] = 1;
                                        }
                                    }
                                }
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