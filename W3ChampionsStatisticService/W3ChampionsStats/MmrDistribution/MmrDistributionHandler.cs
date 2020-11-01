using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using W3ChampionsStatisticService.CommonValueObjects;
using W3ChampionsStatisticService.Ports;

namespace W3ChampionsStatisticService.W3ChampionsStats.MmrDistribution
{
    public class MmrDistributionHandler
    {
        private readonly IPlayerRepository _playerRepository;

        public MmrDistributionHandler(IPlayerRepository playerRepository)
        {
            _playerRepository = playerRepository;
        }

        public async Task<MmrStats> GetDistributions(int season, GateWay gateWay, GameMode gameMode)
        {
            var mmrs = await _playerRepository.LoadMmrs(season, gateWay, gameMode);
            var orderedMMrs = mmrs.OrderByDescending(m => m).ToList();
            var ranges = Ranges(2325, 575, 25).ToList();
            var highest = ranges.First();
            var grouped = ranges.Select(r => new MmrCount(r, orderedMMrs.Count(x => ((x - r < 25) && (x >= r)) || x >= highest))).ToList();
            grouped.Remove(grouped.Last());
            return new MmrStats(grouped, orderedMMrs);
        }

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
        public int Top25ercentIndex { get; set; }
        public int Top50PercentIndex { get; set; }

        public List<MmrCount> DistributedMmrs { get; }

        public MmrStats(List<MmrCount> distributedMmrs, List<int> mmrs)
        {
            DistributedMmrs = distributedMmrs;

            Top2PercentIndex = DistributedMmrs.IndexOf(DistributedMmrs.Last(d => d.Mmr > mmrs[mmrs.Count / 50]));
            Top5PercentIndex = DistributedMmrs.IndexOf(DistributedMmrs.Last(d => d.Mmr > mmrs[mmrs.Count / 20]));
            Top10PercentIndex = DistributedMmrs.IndexOf(DistributedMmrs.Last(d => d.Mmr > mmrs[mmrs.Count / 10]));
            Top25ercentIndex = DistributedMmrs.IndexOf(DistributedMmrs.Last(d => d.Mmr > mmrs[mmrs.Count / 4]));
            Top50PercentIndex = DistributedMmrs.IndexOf(DistributedMmrs.Last(d => d.Mmr > mmrs[mmrs.Count / 2]));
        }
    }
}
