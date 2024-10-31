using System;
using System.Threading.Tasks;
using W3ChampionsStatisticService.Matches;
using W3C.Domain.MatchmakingService;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.ReadModelBase;

namespace W3ChampionsStatisticService.W3ChampionsStats.MapsPerSeasonTemporaryHandler;

// This handler will add mapNames to previously stored GamesPlayedOnMap objects in the MapsPerSeason collection
// This handler could be removed after going through currently stored MatchFinishedEvents
public class MapsPerSeasonTemporaryHandler(IW3StatsRepo w3Stats) : IReadModelHandler
{
    private readonly IW3StatsRepo _w3Stats = w3Stats;

    public async Task Update(MatchFinishedEvent nextEvent)
    {
        if (nextEvent.WasFakeEvent) return;

        var match = nextEvent.match;

        if (match == null) return;
        
        var statOverall = await _w3Stats.LoadMapsPerSeason(-1);
        var statCurrent = await _w3Stats.LoadMapsPerSeason(match.season);

        if (statCurrent == null || statOverall == null || nextEvent.MapName == null)
        {
            return;
        }

        bool overallUpdated = statOverall.UpdateMapName(new MapName(match.map).Name, nextEvent.MapName, match.gameMode);
        bool currentUpdated = statCurrent.UpdateMapName(new MapName(match.map).Name, nextEvent.MapName, match.gameMode);

        // Once the MapsPerSeasonTemporaryHandler catches up to the MapsPerSeasonHandler,
        // We don't want to produce race conditions. So don't create documents and don't update ones that are up to date.
        if (overallUpdated && currentUpdated)
        {
            await _w3Stats.Save(statOverall);
            await _w3Stats.Save(statCurrent);
        }
    }
}
