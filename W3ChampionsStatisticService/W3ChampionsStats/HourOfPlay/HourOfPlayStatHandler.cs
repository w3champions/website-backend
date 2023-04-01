using System;
using System.Threading.Tasks;
using W3C.Domain.MatchmakingService;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.ReadModelBase;
using Serilog;

namespace W3ChampionsStatisticService.W3ChampionsStats.HourOfPlay
{
    public class HourOfPlayStatHandler : IReadModelHandler
    {
        private readonly IW3StatsRepo _w3Stats;

        public HourOfPlayStatHandler(
            IW3StatsRepo w3Stats
            )
        {
            _w3Stats = w3Stats;
        }

        public async Task Update(MatchFinishedEvent nextEvent)
        {
            if (nextEvent.WasFakeEvent) return;
            var startTime = DateTimeOffset.FromUnixTimeMilliseconds(nextEvent.match.startTime);
            var now = DateTimeOffset.UtcNow.Date;
            var daysOfDifference = now - startTime.Date;
            if (daysOfDifference >= TimeSpan.FromDays(14))
            {
                return;
            }

            var stat = await _w3Stats.LoadHourOfPlay() ?? HourOfPlayStat.Create();
            Log.Information($"Recording Popular Hours stat for {nextEvent.match.id}");
            stat.Apply(nextEvent.match.gameMode, startTime);
            await _w3Stats.Save(stat);
        }
    }
}
