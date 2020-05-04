using System.Collections.Generic;
using System.Linq;
using W3ChampionsStatisticService.Matches;
using W3ChampionsStatisticService.PadEvents;

namespace W3ChampionsStatisticService.W3ChampionsStats.HeroPlayedStats
{
    public class HeroStatByMode
    {
        public GameMode GameMode { get; set; }
        public List<HeroStatByPick> Stats { get; set; } = new List<HeroStatByPick>
        {
            new HeroStatByPick { Pick = 0 },
            new HeroStatByPick { Pick = 1 },
            new HeroStatByPick { Pick = 2 },
            new HeroStatByPick { Pick = 3 }
        };

        public void AddHeroes(List<Hero> heroes)
        {
            var overallPicks = Stats.Single(s => s.Pick == 0);
            foreach (var hero in heroes)
            {
                var correctPosition = Stats.Single(s => s.Pick == hero.level);

                overallPicks.AddHeroe(hero);
                correctPosition.AddHeroe(hero);
            }
        }
    }
}