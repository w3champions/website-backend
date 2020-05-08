using System;
using System.Collections.Generic;
using System.Linq;
using W3ChampionsStatisticService.PadEvents.PadSync;
using W3ChampionsStatisticService.PlayerStats.RaceOnMapVersusRaceStats;
using W3ChampionsStatisticService.W3ChampionsStats.HeroWinrate;

namespace W3ChampionsStatisticService.W3ChampionsStats
{
    public class HeroWinrateDto
    {
        public HeroWinrateDto(List<HeroWinRatePerHero> stats, string opFirst, string opSecond, string opThird)
        {
            if (opSecond == "all")
            {
                var winrates = stats.Where(s => s.Id == $"{opFirst}");
                Winrate = winrates.SelectMany(w => w.WinRates).Aggregate((h1, h2) =>
                    new HeroWinRate { WinLoss = new WinLoss { Wins = h1.WinLoss.Wins + h2.WinLoss.Wins} });
            }
            else if (opThird == "all")
            {
                var winrates = stats.Where(s => s.Id == $"{opFirst}_{opSecond}");
            }

            Winrate = stats.SingleOrDefault(s => s.Id == $"{opFirst}_{opSecond}_{opThird}") ?? HeroWinRatePerHero.Create($"{opFirst}_{opSecond}_{opThird}");
        }

        public HeroWinRatePerHero Winrate { get; set; }
    }
}