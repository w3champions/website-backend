﻿using System;
using System.Linq;
using System.Threading.Tasks;
using W3C.Domain.MatchmakingService;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.ReadModelBase;
using W3C.Contracts.Matchmaking;

namespace W3ChampionsStatisticService.W3ChampionsStats.GameLengths;

public class GameLengthStatHandler(IW3StatsRepo w3Stats) : IReadModelHandler
{
    private readonly IW3StatsRepo _w3Stats = w3Stats;

    public async Task Update(MatchFinishedEvent nextEvent)
    {
        if (nextEvent.WasFakeEvent) return;
        var mode = nextEvent.match.gameMode;
        var stat = await _w3Stats.LoadGameLengths(mode) ?? GameLengthStat.Create(mode);
        var endTime = DateTimeOffset.FromUnixTimeMilliseconds(nextEvent.match.endTime);
        var startTime = DateTimeOffset.FromUnixTimeMilliseconds(nextEvent.match.startTime);
        var mmr = (int)nextEvent.match.players.Max(p => p.mmr.rating);
        var duration = (int)(endTime - startTime).TotalSeconds;
        stat.Apply(duration, mmr, mode);
        await _w3Stats.Save(stat);
    }
}
