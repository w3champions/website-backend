using System;
using System.Threading.Tasks;
using W3C.Domain.MatchmakingService;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.ReadModelBase;
using W3C.Contracts.Matchmaking;

namespace W3ChampionsStatisticService.PlayerStats.GameLengthForPlayerStatistics;
public class GameLengthForPlayerStatisticsHandler(IPlayerRepository playerRepo) : IReadModelHandler
{
    private readonly IPlayerRepository _playerRepo = playerRepo;

    public async Task Update(MatchFinishedEvent nextEvent)
    {
        var match = nextEvent.match;
        
        if (nextEvent.WasFakeEvent || match.gameMode != GameMode.GM_1v1) return;

        var endTime = DateTimeOffset.FromUnixTimeMilliseconds(match.endTime);
        var startTime = DateTimeOffset.FromUnixTimeMilliseconds(match.startTime);
        var duration = endTime - startTime;
        var season = match.season;
        
        for (var i = 0; i < 2; i++) {
            var players = match.players;
            var player = players[i];
            var opponent = i == 0 ? players[1] : players[0];
            var opponentRace = opponent.race;
            var battleTag = player.battleTag;
            PlayerGameLength gameLengthStats = await _playerRepo.LoadOrCreateGameLengthForPlayerStats(battleTag, season);
            gameLengthStats.AddGameLength((int)duration.TotalSeconds, (int)opponentRace);
            await _playerRepo.Save(gameLengthStats);
        }
    }
}
