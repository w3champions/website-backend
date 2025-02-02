using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using W3C.Domain.CommonValueObjects;
using W3ChampionsStatisticService.Ports;

namespace W3ChampionsStatisticService.W3ChampionsStats.HeroWinrate;

public class HeroStatsQueryHandler(IW3StatsRepo w3StatsRepo)
{
    private readonly IW3StatsRepo _w3StatsRepo = w3StatsRepo;

    public async Task<WinLoss> GetStats(
        string first,
        string second,
        string third,
        string opFirst,
        string opSecond,
        string opThird)
    {
        var searchString = first;
        if (first == "all" && opFirst == "all") return new WinLoss();
        if (second == "none" || third == "none")
        {
            if (second != "none") searchString += $"_{second}";
            if (third != "none") searchString += $"_{third}";
            var stats = await _w3StatsRepo.LoadHeroWinrate(searchString);
            return HeroWinrateDto(new List<OverallHeroWinRatePerHero> { stats }, opFirst, opSecond, opThird);
        }
        else
        {
            if (second != "all") searchString += $"_{second}";
            if (third != "all") searchString += $"_{third}";
            var stats = await _w3StatsRepo.LoadHeroWinrateLike(searchString);
            return HeroWinrateDto(stats, opFirst, opSecond, opThird);
        }
    }

    private WinLoss HeroWinrateDto(List<OverallHeroWinRatePerHero> stats, string opFirst, string opSecond, string opThird)
    {
        if (opFirst == "all" && opSecond == "none") return CombineWinrates(stats, s => s.HeroCombo.Split("_").Length == 1);
        if (opFirst == "all" && opSecond == "all" && opThird == "none") return CombineWinrates(stats, s => s.HeroCombo.Split("_").Length == 2);
        if (opFirst == "all") return CombineWinrates(stats, s => true);
        if (opSecond == "all") return CombineWinrates(stats, s => s.HeroCombo.StartsWith($"{opFirst}"));
        if (opThird == "all") return CombineWinrates(stats, s => s.HeroCombo.StartsWith($"{opFirst}_{opSecond}"));
        if (opSecond == "none") return CombineWinrates(stats, s => s.HeroCombo == $"{opFirst}");
        if (opThird == "none") return CombineWinrates(stats, s => s.HeroCombo == $"{opFirst}_{opSecond}");

        return CombineWinrates(stats, s => s.HeroCombo == $"{opFirst}_{opSecond}_{opThird}");
    }

    private WinLoss CombineWinrates(
        List<OverallHeroWinRatePerHero> stats,
        Func<HeroWinRate, bool> func)
    {
        var winrates = stats.SelectMany(s => s.WinRates).Where(func).ToList();
        var wins = winrates.Sum(w => w.WinLoss.Wins);
        var losses = winrates.Sum(w => w.WinLoss.Losses);
        return new WinLoss
        {
            Wins = wins,
            Losses = losses
        };
    }
}
