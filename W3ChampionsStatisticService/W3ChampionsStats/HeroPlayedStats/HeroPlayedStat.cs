using System.Collections.Generic;
using System.Linq;
using W3ChampionsStatisticService.PadEvents;

namespace W3ChampionsStatisticService.W3ChampionsStats.HeroPlayedStats
{
    public class HeroPlayedStat : List<HeroStat>
    {
        public static HeroPlayedStat Create()
        {
            return new HeroPlayedStat();
        }

        public void Count(IEnumerable<Hero> heroes)
        {
            foreach (var hero in heroes)
            {
                var heroInList = this.SingleOrDefault(h => hero.icon == h.Icon);
                if (heroInList == null)
                {
                    Add(HeroStat.Create(hero.icon));
                }

                heroInList = this.Single(h => hero.icon == h.Icon);
                heroInList.Count++;
            }
        }

        public string Id => nameof(HeroPlayedStat);
    }
}