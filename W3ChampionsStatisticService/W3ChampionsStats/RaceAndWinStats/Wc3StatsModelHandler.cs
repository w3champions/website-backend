using System.Threading.Tasks;
using W3ChampionsStatisticService.MatchEvents;
using W3ChampionsStatisticService.Ports;

namespace W3ChampionsStatisticService.W3ChampionsStats.RaceAndWinStats
{
    public class Wc3StatsModelHandler : IReadModelHandler
    {
        private readonly IW3StatsRepo _w3Stats;

        public Wc3StatsModelHandler(
            IW3StatsRepo w3Stats
            )
        {
            _w3Stats = w3Stats;
        }

        public async Task Update(MatchFinishedEvent nextEvent)
        {
            var stat = await _w3Stats.Load() ?? new Wc3Stats();
            stat.Apply(nextEvent);
            await _w3Stats.Save(stat);
        }
    }
}