using System;
using System.Threading.Tasks;
using W3C.Domain.MatchmakingService;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.ReadModelBase;
using W3C.Domain.Tracing;

namespace W3ChampionsStatisticService.W3ChampionsStats.DistinctPlayersPerDays;

[Trace]
public class DistinctPlayersPerDayHandler(IW3StatsRepo w3Stats) : IMatchFinishedReadModelHandler
{
    private readonly IW3StatsRepo _w3Stats = w3Stats;

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
