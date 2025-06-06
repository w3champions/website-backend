﻿using System.Threading.Tasks;
using W3C.Domain.MatchmakingService;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.ReadModelBase;
using W3C.Domain.Tracing;

namespace W3ChampionsStatisticService.PlayerProfiles.RaceStats;

[Trace]
public class PlayerRaceStatPerGatewayHandler(IPlayerRepository playerRepository) : IMatchFinishedReadModelHandler
{
    private readonly IPlayerRepository _playerRepository = playerRepository;

    public async Task Update(MatchFinishedEvent nextEvent)
    {
        var match = nextEvent.match;

        foreach (var player in match.players)
        {
            var stat = await _playerRepository.LoadRaceStatPerGateway(player.battleTag, player.race, match.gateway, match.season)
                        ?? new PlayerRaceStatPerGateway(player.battleTag, player.race, match.gateway, match.season);

            stat.RecordWin(player.won);

            await _playerRepository.UpsertPlayerRaceStat(stat);
        }
    }
}
