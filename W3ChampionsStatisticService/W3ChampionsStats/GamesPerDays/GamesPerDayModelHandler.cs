using System;
using System.Threading.Tasks;
using W3ChampionsStatisticService.MatchEvents;
using W3ChampionsStatisticService.Ports;

namespace W3ChampionsStatisticService.W3ChampionsStats.GamesPerDays
{
    public class GamesPerDayModelHandler : IReadModelHandler
    {
        private readonly IW3StatsRepo _w3Stats;

        public GamesPerDayModelHandler(
            IW3StatsRepo w3Stats
            )
        {
            _w3Stats = w3Stats;
        }

        public async Task Update(MatchFinishedEvent nextEvent)
        {
            var match = nextEvent.match;
            var endTime = DateTimeOffset.FromUnixTimeMilliseconds(match.endTime).Date;

            var stat = await _w3Stats.LoadGamesPerDay(endTime) ?? GameDay.Create(endTime);

            stat.AddGame();

            await _w3Stats.Save(stat);
        }
    }
}