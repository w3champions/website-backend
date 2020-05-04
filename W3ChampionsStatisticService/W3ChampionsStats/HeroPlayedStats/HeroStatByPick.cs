using System.Collections.Generic;
using System.Linq;
using W3ChampionsStatisticService.PadEvents;

namespace W3ChampionsStatisticService.W3ChampionsStats.HeroPlayedStats
{
    public class HeroStatByPick
    {
        public int Pick { get; set; }
        public List<HeroStat> Stats { get; set; } = new List<HeroStat>();

        public void AddHeroe(Hero hero)
        {
            hero.icon = ParseReforgedName(hero.icon);
            var heroInList = Stats.SingleOrDefault(h => hero.icon == h.Icon);
            if (heroInList == null)
            {
                Stats.Add(HeroStat.Create(hero.icon));
            }

            heroInList = Stats.Single(h => hero.icon == h.Icon);
            heroInList.Count++;

            Stats = Stats.OrderByDescending(s => s.Count).ToList();
        }

        private string ParseReforgedName(string heroIcon)
        {
            if (heroIcon == "jainasea") return "archmage";
            if (heroIcon == "thrallchampion") return "farseer";
            if (heroIcon == "fallenkingarthas") return "deathknight";
            if (heroIcon == "cenariusnightmare") return "keeperofthegrove";
            return heroIcon;
        }
    }
}