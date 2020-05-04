using System.Collections.Generic;
using System.Linq;

namespace W3ChampionsStatisticService.W3ChampionsStats.HeroPlayedStats
{
    public class HeroStatByPick
    {
        public int Pick { get; set; }
        public List<HeroStat> Stats { get; set; } = new List<HeroStat>();

        public void AddHeroe(HeroPickDto hero)
        {
            var heroInList = Stats.SingleOrDefault(h => hero.Icon == h.Icon);
            if (heroInList == null)
            {
                Stats.Add(HeroStat.Create(hero.Icon));
            }

            heroInList = Stats.Single(h => hero.Icon == h.Icon);
            heroInList.Count++;

            Stats = Stats.OrderByDescending(s => s.Count).ToList();
        }
    }
}