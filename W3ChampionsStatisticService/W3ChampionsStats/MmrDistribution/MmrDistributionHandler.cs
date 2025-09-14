using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using W3C.Contracts.Matchmaking;
using W3ChampionsStatisticService.Ports;
using System;
using W3C.Domain.Tracing;
using W3ChampionsStatisticService.Common.Constants;

namespace W3ChampionsStatisticService.W3ChampionsStats.MmrDistribution;
using Microsoft.Extensions.Caching.Memory;

[Trace]
public class MmrDistributionHandler(IPlayerRepository playerRepository, TimeSpan? cacheTtl = null)
{
    private readonly IPlayerRepository _playerRepository = playerRepository;
    private static readonly MemoryCache _cache = new MemoryCache(new MemoryCacheOptions());
    private readonly TimeSpan _cacheTtl = cacheTtl ?? TimeSpan.FromHours(1);

    public async Task<MmrStats> GetDistributions(int season, GateWay gateWay, GameMode gameMode)
    {
        string cacheKey = $"mmr-dist-{season}-{gateWay}-{gameMode}";
        if (_cache.TryGetValue(cacheKey, out MmrStats cachedStats))
        {
            return cachedStats;
        }
        var mmrs = await _playerRepository.LoadMmrs(season, gateWay, gameMode);
        var orderedMMrs = mmrs.OrderByDescending(m => m).ToList();
        if (orderedMMrs.Count == 0)
        {
            var stats = new MmrStats(new List<MmrCount>(), orderedMMrs);
            return stats;
        }
        var max = orderedMMrs.FirstOrDefault(MmrConstants.MaxMmr);
        var min = orderedMMrs.LastOrDefault();
        var ranges = Ranges(max, min, 25).ToList();
        var highest = ranges.Count > 0 ? ranges.First() : max;
        var grouped = ranges.Select(r => new MmrCount(r, orderedMMrs.Count(x => ((x - r < 25) && (x >= r)) || x >= highest))).ToList();
        if (grouped.Count > 0) {
            grouped.Remove(grouped.Last());
        }
        var statsFinal = new MmrStats(grouped, orderedMMrs);
        _cache.Set(cacheKey, statsFinal, _cacheTtl);
        return statsFinal;
    }

    [NoTrace]
    private static IEnumerable<int> Ranges(int max, int min, int steps)
    {
        while (max > min)
        {
            max -= steps;
            yield return max;
        }
    }
}

public class MmrStats
{

    public int Top2PercentIndex { get; set; }
    public int Top5PercentIndex { get; set; }
    public int Top10PercentIndex { get; set; }
    public int Top25PercentIndex { get; set; }
    public int Top50PercentIndex { get; set; }

    public List<MmrCount> DistributedMmrs { get; }

    public double StandardDeviation { get; set; }

    public MmrStats(List<MmrCount> distributedMmrs, List<int> mmrs)
    {
        DistributedMmrs = distributedMmrs;

        if (DistributedMmrs.Count == 0 || mmrs.Count == 0)
        {
            Top2PercentIndex = Top5PercentIndex = Top10PercentIndex = Top25PercentIndex = Top50PercentIndex = -1;
        }
        else
        {
            int mmr2 = mmrs[mmrs.Count / 50];
            int mmr5 = mmrs[mmrs.Count / 20];
            int mmr10 = mmrs[mmrs.Count / 10];
            int mmr25 = mmrs[mmrs.Count / 4];
            int mmr50 = mmrs[mmrs.Count / 2];

            Top2PercentIndex = DistributedMmrs.FindLastIndex(d => d.Mmr > mmr2);
            Top5PercentIndex = DistributedMmrs.FindLastIndex(d => d.Mmr > mmr5);
            Top10PercentIndex = DistributedMmrs.FindLastIndex(d => d.Mmr > mmr10);
            Top25PercentIndex = DistributedMmrs.FindLastIndex(d => d.Mmr > mmr25);
            Top50PercentIndex = DistributedMmrs.FindLastIndex(d => d.Mmr > mmr50);
        }

        StandardDeviation = CalculateStandardDeviation(mmrs);
    }

    [Trace]
    private static double CalculateStandardDeviation(List<int> values)
    {
        if (!values.Any())
        {
            return 0.0;
        }

        double average = values.Average();
        double sum = values.Sum(val => (val - average) * (val - average));
        double result = Math.Sqrt(sum / values.Count());
        result = Math.Round(result, 2);

        return result;
    }
}
