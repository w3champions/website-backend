using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using W3C.Contracts.GameObjects;
using W3C.Domain.CommonValueObjects;
using W3C.Domain.MatchmakingService;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.ReadModelBase;
using W3C.Contracts.Matchmaking;

namespace W3ChampionsStatisticService.Ladder;

public class PlayOverviewHandler(IPlayerRepository playerRepository) : IReadModelHandler
{
    private readonly IPlayerRepository _playerRepository = playerRepository;

    public async Task Update(MatchFinishedEvent nextEvent)
    {
        var winners = nextEvent.match.players.Where(p => p.won).ToList();
        var losers = nextEvent.match.players.Where(p => !p.won).ToList();

        if (winners.Count == 0 || losers.Count == 0)
        {
            // We should log the bad event here
            return;
        }

        if ((nextEvent.match.gameMode == GameMode.GM_2v2
                || nextEvent.match.gameMode == GameMode.GM_2v2_AT)
            && winners.Count != 2 && losers.Count != 2)
        {
            return;
        }

        // for broken events
        if (winners.Count == 0 || losers.Count == 0) return;

        await UpdatePlayersByTeam(nextEvent, winners);
        await UpdatePlayersByTeam(nextEvent, losers);
    }

    private async Task UpdatePlayersByTeam(MatchFinishedEvent nextEvent, List<PlayerMMrChange> players)
    {
        var atPlayers = players.Where(x => x.IsAt);
        foreach (var atTeam in atPlayers.GroupBy(x => x.team))
        {
            var loser = await UpdatePlayers(nextEvent, atTeam.ToList());
            await _playerRepository.UpsertPlayerOverview(loser);
        }

        var restPlayers = players.Where(x => !x.IsAt);
        foreach (var player in restPlayers)
        {
            var loser = await UpdatePlayers(nextEvent, new List<PlayerMMrChange>() { player });
            await _playerRepository.UpsertPlayerOverview(loser);
        }
    }

    private async Task<PlayerOverview> UpdatePlayers(MatchFinishedEvent nextEvent, List<PlayerMMrChange> players)
    {
        var playerIds = players.Select(w => PlayerId.Create(w.battleTag)).ToList();

        var match = nextEvent.match;
        var playerRaceIfSingle = match.gameMode == GameMode.GM_1v1 && match.season >= 2 ? (Race?)players.Single().race : null;

        var gameMode = GetOverviewGameMode(match.gameMode, players[0]);

        var winnerIdCombined = new BattleTagIdCombined(
            players.Select(p =>
                PlayerId.Create(p.battleTag)).ToList(),
                    match.gateway,
                    gameMode,
                    match.season,
                    playerRaceIfSingle);

        var winner = await _playerRepository.LoadOverview(winnerIdCombined.Id)
                        ?? PlayerOverview.Create(
                            playerIds,
                            match.gateway,
                            gameMode,
                            match.season,
                            playerRaceIfSingle);

        winner.RecordWin(
            players.First().won,
            (int?)players.First().updatedMmr?.rating ?? (int?)players.First().mmr?.rating ?? 0);

        return winner;
    }

    private GameMode GetOverviewGameMode(GameMode gameMode, PlayerMMrChange player)
    {
        if (gameMode == GameMode.GM_2v2 && player.IsAt)
        {
            return GameMode.GM_2v2_AT;
        }

        if (gameMode == GameMode.GM_4v4 && player.IsAt)
        {
            return GameMode.GM_4v4_AT;
        }

        if (gameMode == GameMode.GM_LEGION_4v4_x20 && player.IsAt)
        {
            return GameMode.GM_LEGION_4v4_x20_AT;
        }

        if (gameMode == GameMode.GM_DOTA_5ON5 && player.IsAt)
        {
            return GameMode.GM_DOTA_5ON5_AT;
        }

        if (gameMode == GameMode.GM_DS && player.IsAt)
        {
            return GameMode.GM_DS_AT;
        }

        return gameMode;
    }
}
