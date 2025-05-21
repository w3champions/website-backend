using System;
using System.Threading.Tasks;
using W3C.Domain.MatchmakingService;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.ReadModelBase;
using W3C.Contracts.Matchmaking;
using W3C.Contracts.GameObjects;

namespace W3ChampionsStatisticService.W3ChampionsStats.MatchupLengths;

public class MatchupLengthsHandler(IW3StatsRepo w3Stats) : IMatchFinishedReadModelHandler
{
    private readonly IW3StatsRepo _w3StatsRepo = w3Stats;

    public async Task Update(MatchFinishedEvent nextEvent)
    {
        var match = nextEvent.match;
        GameMode mode = match.gameMode;
        var isFakeEvent = nextEvent.WasFakeEvent;
        var isNot1v1 = mode != GameMode.GM_1v1;
        var endTime = DateTimeOffset.FromUnixTimeMilliseconds(match.endTime);
        var startTime = DateTimeOffset.FromUnixTimeMilliseconds(match.startTime);
        var duration = endTime - startTime;
        var durationSeconds = (int)duration.TotalSeconds;
        var isShortGame = durationSeconds < 120;
        var players = match.players;
        var race1 = players[0].race;
        var race2 = players[1].race;
        var hasRandom = race1 == Race.RnD || race2 == Race.RnD;

        if (isFakeEvent || isNot1v1 || isShortGame || hasRandom) return;

        var mmr1 = players[0].mmr.rating;
        var mmr2 = players[1].mmr.rating;
        var season = match.season;
        var mmr = (int)Math.Max(mmr1, mmr2);

        var matchupLength = await _w3StatsRepo.LoadMatchupLengthOrCreate(race1.ToString(), race2.ToString(), season.ToString());
        matchupLength.Apply(durationSeconds, mmr);
        await _w3StatsRepo.Save(matchupLength);

        // record for all season
        var matchupLengthAllSeasons = await _w3StatsRepo.LoadMatchupLengthOrCreate(race1.ToString(), race2.ToString(), "all");
        matchupLengthAllSeasons.Apply(durationSeconds, mmr);
        await _w3StatsRepo.Save(matchupLengthAllSeasons);
    }
}
