﻿using System.Linq;
using System.Threading.Tasks;
using W3C.Domain.MatchmakingService;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.ReadModelBase;
using W3C.Domain.Tracing;

namespace W3ChampionsStatisticService.Ladder;

[Trace]
public class PlayerWinrateHandler(IPlayerRepository playerRepository) : IMatchFinishedReadModelHandler
{
    private readonly IPlayerRepository _playerRepository = playerRepository;

    public async Task Update(MatchFinishedEvent nextEvent)
    {
        var playerMMrChanges = nextEvent.match.players;
        var winrateTasks = playerMMrChanges.Select(async p => await LoadAndApply(p, nextEvent.match.season));
        var newWinrates = (await Task.WhenAll(winrateTasks)).ToList();
        await _playerRepository.UpsertWins(newWinrates);
    }

    private async Task<PlayerWinLoss> LoadAndApply(PlayerMMrChange p, int season)
    {
        var playerWinLoss = await _playerRepository.LoadPlayerWinrate(p.battleTag, season);
        var loadPlayerWinrate = playerWinLoss ?? PlayerWinLoss.Create(p.battleTag, season);
        return loadPlayerWinrate.Apply(p.won);
    }
}
