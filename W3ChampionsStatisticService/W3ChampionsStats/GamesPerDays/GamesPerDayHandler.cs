using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using W3C.Contracts.Matchmaking;
using W3C.Domain.MatchmakingService;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.ReadModelBase;

namespace W3ChampionsStatisticService.W3ChampionsStats.GamesPerDays;

public class GamesPerDayHandler : IReadModelHandler
{
    private readonly IW3StatsRepo _w3Stats;

    public GamesPerDayHandler(
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

        await MakeSureEveryDayHasAStat(endTime);

        var stat = await _w3Stats.LoadGamesPerDay(endTime, match.gameMode, match.gateway)
                    ?? GamesPerDay.Create(endTime, match.gameMode, match.gateway);
        var statOverallForGateway = await _w3Stats.LoadGamesPerDay(endTime, GameMode.Undefined, match.gateway)
                            ?? GamesPerDay.Create(endTime, GameMode.Undefined, match.gateway);

        var statForGameModeOnAllGateways = await _w3Stats.LoadGamesPerDay(endTime, match.gameMode, GateWay.Undefined)
                                        ?? GamesPerDay.Create(endTime, match.gameMode, GateWay.Undefined);
        var statOverall = await _w3Stats.LoadGamesPerDay(endTime, GameMode.Undefined, GateWay.Undefined)
                                        ?? GamesPerDay.Create(endTime, GameMode.Undefined, GateWay.Undefined);

        stat.AddGame();
        statOverall.AddGame();
        statOverallForGateway.AddGame();
        statForGameModeOnAllGateways.AddGame();

        await _w3Stats.Save(new List<GamesPerDay>
        {
            stat,
            statOverall,
            statOverallForGateway,
            statForGameModeOnAllGateways
        });
    }

    private async Task MakeSureEveryDayHasAStat(DateTime endTime)
    {
        foreach (GameMode mode in Enum.GetValues(typeof(GameMode)))
        {
            var gamesPerDays = new List<GamesPerDay>();
            foreach (GateWay gw in Enum.GetValues(typeof(GateWay)))
            {
                var stat = await _w3Stats.LoadGamesPerDay(endTime, mode, gw)
                            ?? GamesPerDay.Create(endTime, mode, gw);
                gamesPerDays.Add(stat);
            }

            await _w3Stats.Save(gamesPerDays);
        }

    }
}
