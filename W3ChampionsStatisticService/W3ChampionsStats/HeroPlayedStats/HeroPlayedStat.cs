using System.Collections.Generic;
using System.Linq;
using W3C.Domain.CommonValueObjects;
using W3C.Domain.Repositories;

namespace W3ChampionsStatisticService.W3ChampionsStats.HeroPlayedStats
{
    public class HeroPlayedStat : IIdentifiable
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
                    new HeroStatByMode { GameMode = GameMode.GM_2v2 },
                    new HeroStatByMode { GameMode = GameMode.GM_4v4_AT },
                }
            };
        }

        public List<HeroStatByMode> Stats { get; set; }

        public void AddHeroes(List<HeroPickDto> heroes, GameMode gameMode)
        {
            var total = Stats.SingleOrDefault(s => s.GameMode == gameMode);
            if (total != null)
            {
                total.AddHeroes(heroes);
            }
        }
        public string Id { get; set; } = nameof(HeroPlayedStat);
    }
}