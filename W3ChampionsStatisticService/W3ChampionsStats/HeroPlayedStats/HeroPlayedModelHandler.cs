using System.Linq;
using System.Threading.Tasks;
using W3ChampionsStatisticService.PadEvents;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.ReadModelBase;

namespace W3ChampionsStatisticService.W3ChampionsStats.HeroPlayedStats
{
    public class HeroPlayedModelHandler : IReadModelHandler
    {
        private readonly IW3StatsRepo _w3Stats;

        public HeroPlayedModelHandler(
            IW3StatsRepo w3Stats
            )
        {
            _w3Stats = w3Stats;
        }

        public async Task Update(MatchFinishedEvent nextEvent)
        {
            var stats = await _w3Stats.LoadHourOfPlay();
            var stat = await _w3Stats.LoadHeroPlayedStat() ?? HeroPlayedStat.Create();
            if (nextEvent.result == null) return;

            var heroes = nextEvent.result.players.SelectMany(p => p.heroes);
            stat.AddHeroes(heroes);
            await _w3Stats.Save(stat);
        }
    }
}