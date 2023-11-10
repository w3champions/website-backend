using System;
using System.Threading.Tasks;
using W3C.Domain.MatchmakingService;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.ReadModelBase;
using W3C.Contracts.Matchmaking;

namespace W3ChampionsStatisticService.PlayerStats.GameLengthForPlayerStatistics;
public class GameLengthForPlayerStatisticsHandler : IReadModelHandler
{
    private readonly IPlayerRepository _playerRepo;

    public GameLengthForPlayerStatisticsHandler(
        IPlayerRepository playerRepo
        )
    {
        _playerRepo = playerRepo;
    }

    public async Task Update(MatchFinishedEvent nextEvent)
    {
        if (nextEvent.WasFakeEvent || nextEvent.match.gameMode != GameMode.GM_1v1) return;

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
