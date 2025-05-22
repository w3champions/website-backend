﻿using System.Threading.Tasks;
using W3ChampionsStatisticService.Matches;
using W3C.Domain.MatchmakingService;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.ReadModelBase;
using W3C.Domain.Tracing;
namespace W3ChampionsStatisticService.W3ChampionsStats.MapsPerSeasons;

[Trace]
public class MapsPerSeasonHandler(IW3StatsRepo w3Stats) : IReadModelHandler
{
    private readonly IW3StatsRepo _w3Stats = w3Stats;

    public async Task Update(MatchFinishedEvent nextEvent)
    {
        if (nextEvent.WasFakeEvent) return;

        var match = nextEvent.match;
        var statOverall = await _w3Stats.LoadMapsPerSeason(-1) ?? MapsPerSeason.Create(-1);

        var statCurrent = await _w3Stats.LoadMapsPerSeason(match.season) ?? MapsPerSeason.Create(match.season);

        statOverall.Count(new MapName(match.map).Name, match.gameMode);
        statCurrent.Count(new MapName(match.map).Name, match.gameMode);

        await _w3Stats.Save(statOverall);
        await _w3Stats.Save(statCurrent);
    }
}
