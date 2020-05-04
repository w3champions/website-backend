using System.Collections.Generic;
using System.Linq;
using W3ChampionsStatisticService.Matches;

namespace W3ChampionsStatisticService.W3ChampionsStats.HeroPlayedStats
{
    public class HeroStatByMode
    {
        public GameMode GameMode { get; set; }
        public List<HeroStatByPick> OrderedPicks { get; set; } = new List<HeroStatByPick>
        {
            new HeroStatByPick { Pick = 0 },
            new HeroStatByPick { Pick = 1 },
            new HeroStatByPick { Pick = 2 },
            new HeroStatByPick { Pick = 3 }
        };

        public void AddHeroes(List<HeroPickDto> heroes)
        {
            var overallPicks = OrderedPicks.Single(s => s.Pick == 0);
            foreach (var hero in heroes)
            {
                var correctPosition = OrderedPicks.Single(s => s.Pick == hero.Pick);

                overallPicks.AddHeroe(hero);
                correctPosition.AddHeroe(hero);
            }
        }
    }
}