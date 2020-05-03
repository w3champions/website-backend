using System.Collections.Generic;
using System.Linq;
using MongoDB.Bson.Serialization.Attributes;
using W3ChampionsStatisticService.PadEvents;

namespace W3ChampionsStatisticService.W3ChampionsStats.HeroPlayedStats
{
    public class HeroPlayedStat
    {
        public static HeroPlayedStat Create()
        {
            return new HeroPlayedStat();
        }

        public List<HeroStat> Stats { get; set; } = new List<HeroStat>();

        public void AddHeroes(IEnumerable<Hero> heroes)
        {
            foreach (var hero in heroes)
            {
                var heroInList = Stats.SingleOrDefault(h => hero.icon == h.Icon);
                if (heroInList == null)
                {
                    Stats.Add(HeroStat.Create(hero.icon));
                }

                heroInList = Stats.Single(h => hero.icon == h.Icon);
                heroInList.Count++;
            }
        }

        public string Id { get; set; } = nameof(HeroPlayedStat);
    }
}