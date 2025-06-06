using System.Collections.Generic;
using System.Linq;
using W3C.Contracts.Matchmaking;

namespace W3ChampionsStatisticService.W3ChampionsStats.HeroPlayedStats;

public class HeroStatByMode
{
    public GameMode GameMode { get; set; }
    public List<HeroStatByPick> OrderedPicks { get; set; } = new List<HeroStatByPick>
    {
        new() { Pick = EPick.Overall },
        new() { Pick = EPick.First },
        new() { Pick = EPick.Second },
        new() { Pick = EPick.Third }
    };

    public void AddHeroes(List<HeroPickDto> heroes)
    {
        var overallPicks = OrderedPicks.Single(s => s.Pick == EPick.Overall);
        foreach (var hero in heroes)
        {
            var correctPosition = OrderedPicks.Single(s => s.Pick == hero.Pick);

            overallPicks.AddHeroe(hero);
            correctPosition.AddHeroe(hero);
        }
    }
}
