using System;
using System.Threading.Tasks;
using W3ChampionsStatisticService.Matches;
using W3ChampionsStatisticService.PadEvents;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.ReadModelBase;

namespace W3ChampionsStatisticService.W3ChampionsStats.GameLengths
{
    public class GameLengthsModelHandler : IReadModelHandler
    {
        private readonly IW3StatsRepo _w3Stats;

        public GameLengthsModelHandler(
            IW3StatsRepo w3Stats
            )
        {
            _w3Stats = w3Stats;
        }

        public async Task Update(MatchFinishedEvent nextEvent)
        {
            var stat = await _w3Stats.LoadGameLengths() ?? GameLengthStats.Create();
            var endTime = DateTimeOffset.FromUnixTimeMilliseconds(nextEvent.match.endTime);
            var startTime = DateTimeOffset.FromUnixTimeMilliseconds(nextEvent.match.startTime);
            var duration = endTime - startTime;
            stat.Apply(nextEvent.match.gameMode, duration);
            await _w3Stats.Save(stat);
        }
    }
}