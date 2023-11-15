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
        if (nextEvent.WasFakeEvent) return;
        GameMode mode = nextEvent.match.gameMode;
        if (nextEvent.WasFakeEvent || mode != GameMode.GM_1v1) return;
        var players = nextEvent.match.players;
        var race1 = players[0].race

        for (var i = 0; i < 2; i++) {
            var players = nextEvent.match.players;
            var player = players[i];
            var opponent = i == 0 ? players[1] : players[0];
            var opponentRace = opponent.race;
            var battleTag = player.battleTag;
            var endTime = DateTimeOffset.FromUnixTimeMilliseconds(nextEvent.match.endTime);
            var startTime = DateTimeOffset.FromUnixTimeMilliseconds(nextEvent.match.startTime);
            var duration = endTime - startTime;
            var season = nextEvent.match.season;
            PlayerGameLength gameLengthStats = await _playerRepo.LoadOrCreateGameLengthForPlayerStats(battleTag, season);
            gameLengthStats.AddGameLength((int)duration.TotalSeconds, (int)opponentRace);
            await _playerRepo.Save(gameLengthStats);
        }
    }
}
