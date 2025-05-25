using System;
using System.Threading.Tasks;
using W3C.Domain.MatchmakingService;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.ReadModelBase;
using W3C.Domain.Tracing;

namespace W3ChampionsStatisticService.W3ChampionsStats.PopularHours;

[Trace]
public class PopularHoursStatHandler(IW3StatsRepo w3Stats) : IMatchFinishedReadModelHandler
{
    private readonly IW3StatsRepo _w3Stats = w3Stats;

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

        var mode = nextEvent.match.gameMode;
        var stat = await _w3Stats.LoadPopularHoursStat(mode) ?? PopularHoursStat.Create(mode);
        stat.Apply(startTime.UtcDateTime);
        await _w3Stats.Save(stat);
    }
}
