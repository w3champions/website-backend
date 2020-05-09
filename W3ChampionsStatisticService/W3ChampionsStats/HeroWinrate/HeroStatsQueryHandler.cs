using System.Collections.Generic;
using System.Threading.Tasks;
using W3ChampionsStatisticService.PlayerStats.RaceOnMapVersusRaceStats;
using W3ChampionsStatisticService.Ports;

namespace W3ChampionsStatisticService.W3ChampionsStats.HeroWinrate
{
    public class HeroStatsQueryHandler
    {
        private readonly IW3StatsRepo _w3StatsRepo;

        public HeroStatsQueryHandler(IW3StatsRepo w3StatsRepo)
        {
            _w3StatsRepo = w3StatsRepo;
        }
        public async Task<WinLoss> GetStats(
            string first,
            string second,
            string third,
            string opFirst,
            string opSecond,
            string opThird)
        {
            var searchString = first;
            if (second == "none" || third == "none")
            {
                if (second != "none") searchString += $"_{second}";
                if (third != "none") searchString += $"_{third}";
                var stats = await _w3StatsRepo.LoadHeroWinrate(searchString);
                var heroWinrateDto = new HeroWinrateDto(new List<HeroWinRatePerHero> { stats }, opFirst, opSecond, opThird);
                return heroWinrateDto.Winrate.WinLoss;
            }
            else
            {
                if (second != "all") searchString += $"_{second}";
                if (third != "all") searchString += $"_{third}";
                var stats = await _w3StatsRepo.LoadHeroWinrateLike(searchString);
                var heroWinrateDto = new HeroWinrateDto(stats, opFirst, opSecond, opThird);
                return heroWinrateDto.Winrate.WinLoss;
            }

        }
    }
}