using System;
using System.Threading.Tasks;
using W3ChampionsStatisticService.Matches;
using W3ChampionsStatisticService.PadEvents;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.ReadModelBase;

namespace W3ChampionsStatisticService.W3ChampionsStats.GameLengths
{
    public class HourOfPlayModelHandler : IReadModelHandler
    {
        private readonly IW3StatsRepo _w3Stats;

        public HourOfPlayModelHandler(
            IW3StatsRepo w3Stats
            )
        {
            _w3Stats = w3Stats;
        }

        public async Task Update(MatchFinishedEvent nextEvent)
        {
            var stat = await _w3Stats.LoadHourOfPlay() ?? HourOfPlay.Create();
            var startTime = DateTimeOffset.FromUnixTimeMilliseconds(nextEvent.match.startTime);
            stat.AddGame((GameMode) nextEvent.match.gameMode, startTime);
            await _w3Stats.Save(stat);
        }
    }
}