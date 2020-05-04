using System.Collections.Generic;
using System.Linq;
using W3ChampionsStatisticService.Matches;
using W3ChampionsStatisticService.PadEvents;

namespace W3ChampionsStatisticService.W3ChampionsStats.HeroPlayedStats
{
    public class HeroPlayedStat
    {
        public static HeroPlayedStat Create()
        {
            return new HeroPlayedStat
            {
                Stats = new List<HeroStatByMode>
                {
                    new HeroStatByMode { GameMode = GameMode.GM_1v1 },
                    new HeroStatByMode { GameMode = GameMode.FFA },
                    new HeroStatByMode { GameMode = GameMode.GM_4v4 },
                    new HeroStatByMode { GameMode = GameMode.GM_2v2_AT },
                }
            };
        }

        public List<HeroStatByMode> Stats { get; set; }

        public void AddHeroes(List<Hero> heroes, GameMode gameMode)
        {
            var total = Stats.Single(s => s.GameMode == gameMode);
            total.AddHeroes(heroes);
        }
        public string Id { get; set; } = nameof(HeroPlayedStat);
    }

    public class HeroStatByMode
    {
        public GameMode GameMode { get; set; }
        public List<HeroStat> Stats { get; set; } = new List<HeroStat>();

        public void AddHeroes(List<Hero> heroes)
        {
            foreach (var hero in heroes)
            {
                hero.icon = ParseReforgedName(hero.icon);
                var heroInList = Stats.SingleOrDefault(h => hero.icon == h.Icon);
                if (heroInList == null)
                {
                    Stats.Add(HeroStat.Create(hero.icon));
                }

                heroInList = Stats.Single(h => hero.icon == h.Icon);
                heroInList.Count++;
            }

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