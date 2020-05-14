using System;
using System.Threading.Tasks;
using W3ChampionsStatisticService.PadEvents;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.ReadModelBase;

namespace W3ChampionsStatisticService.W3ChampionsStats.DistinctPlayersPerDays
{
    public class DistinctPlayersPerDayHandler : IReadModelHandler
    {
        private readonly IW3StatsRepo _w3Stats;

        public DistinctPlayersPerDayHandler(
            IW3StatsRepo w3Stats
            )
        {
            _w3Stats = w3Stats;
        }

        public async Task Update(MatchFinishedEvent nextEvent)
        {
            if (nextEvent.WasFakeEvent) return;
            var match = nextEvent.match;
            var endTime = DateTimeOffset.FromUnixTimeMilliseconds(match.endTime).Date;

            var stat = await _w3Stats.LoadPlayersPerDay(endTime) ?? DistinctPlayersPerDay.Create(endTime);

            foreach (var player in match.players)
            {
                stat.AddPlayer(player.battleTag);
            }

            await _w3Stats.Save(stat);
        }
    }
}