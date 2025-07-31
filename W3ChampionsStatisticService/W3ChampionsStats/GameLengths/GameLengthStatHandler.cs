using System;
using System.Threading.Tasks;
using W3C.Domain.MatchmakingService;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.ReadModelBase;
using W3C.Contracts.Matchmaking;
using W3C.Domain.Tracing;
using Serilog;


namespace W3ChampionsStatisticService.W3ChampionsStats.GameLengths;

[Trace]
public class GameLengthStatHandler(IW3StatsRepo w3Stats) : IMatchFinishedReadModelHandler
{
    private readonly IW3StatsRepo _w3Stats = w3Stats;

    public async Task Update(MatchFinishedEvent nextEvent)
    {
        if (nextEvent.WasFakeEvent) return;

        GameMode mode = nextEvent.match.gameMode;
        var stat = await _w3Stats.LoadGameLengths(mode) ?? GameLengthStat.Create(mode);
        var endTime = DateTimeOffset.FromUnixTimeMilliseconds(nextEvent.match.endTime);
        var startTime = DateTimeOffset.FromUnixTimeMilliseconds(nextEvent.match.startTime);
        var duration = endTime - startTime;

        if (duration.TotalSeconds <= 0)
        {
            Log.Debug("Skipping game length recording for match {MatchId} with zero or negative duration: {Duration} seconds",
                nextEvent.match.id, duration.TotalSeconds);
            return;
        }

        stat.Apply(duration);
        await _w3Stats.Save(stat);
    }
}
