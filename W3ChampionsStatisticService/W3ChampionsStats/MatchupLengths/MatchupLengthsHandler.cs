using System;
using System.Threading.Tasks;
using W3C.Domain.MatchmakingService;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.ReadModelBase;
using W3C.Contracts.Matchmaking;

namespace W3ChampionsStatisticService.W3ChampionsStats.MatchupLengths;

public class MatchupLengthsHandler : IReadModelHandler
{
    private readonly IW3StatsRepo _w3Stats;

    public MatchupLengthsHandler(
        IW3StatsRepo w3Stats
        )
    {
        _w3Stats = w3Stats;
    }

    public async Task Update(MatchFinishedEvent nextEvent)
    {
        var match = nextEvent.match;
        GameMode mode = match.gameMode;
        var isFakeEvent = nextEvent.WasFakeEvent;
        var isNot1v1 = mode != GameMode.GM_1v1;
        if (isFakeEvent || isNot1v1) return;

        var endTime = DateTimeOffset.FromUnixTimeMilliseconds(match.endTime);
        var startTime = DateTimeOffset.FromUnixTimeMilliseconds(match.startTime);
        var duration = endTime - startTime;
        var durationSeconds = (int) duration.TotalSeconds;
        var players = match.players;
        var race1 = players[0].race.ToString();
        var race2 = players[1].race.ToString();
        var season = match.season;
        var matchupLength = await _w3Stats.LoadMatchupLengthOrCreate(race1, race2, season);
        var mmr = (int) Math.Max(players[0].mmr.rating, players[1].mmr.rating);
        matchupLength.Apply(durationSeconds, mmr);
        await _w3Stats.Save(matchupLength);
    }
}
