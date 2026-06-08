using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using W3C.Contracts.GameObjects;
using W3C.Contracts.Matchmaking;
using W3C.Domain.CommonValueObjects;
using W3C.Domain.MatchmakingService;
using W3C.Domain.Tracing;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.ReadModelBase;

namespace W3ChampionsStatisticService.PlayerProfiles.ProgressionStats;

// Ingests MatchFinishedEvent.match.players[].updatedProgression into the season-keyed
// PlayerProgression read-model. One doc per entity: AT players grouped by team (shared
// team rank), solo players individually. Keying mirrors PlayerGameModeStatPerGatewayHandler
// (gameMode normalized to its AT variant; race in the key only for GM_1v1 season >= 2).
[Trace]
public class PlayerProgressionHandler(IPlayerProgressionRepository progressionRepository) : IMatchFinishedReadModelHandler
{
    private readonly IPlayerProgressionRepository _progressionRepository = progressionRepository;

    public async Task Update(MatchFinishedEvent nextEvent)
    {
        var match = nextEvent.match;
        var placed = match.players.Where(p => p.updatedProgression != null).ToList();

        // AT players are grouped by team into one shared rank doc (mirrors
        // PlayerGameModeStatPerGatewayHandler). Safe because a single match side carries at
        // most one AT team per `team` number for every current AT mode (4-stacks, not 2+2).
        foreach (var team in placed.Where(p => p.IsAt).GroupBy(p => p.team))
        {
            await RecordRank(match, team.ToList());
        }

        foreach (var soloPlayer in placed.Where(p => !p.IsAt))
        {
            await RecordRank(match, new List<PlayerMMrChange> { soloPlayer });
        }
    }

    private async Task RecordRank(Match match, List<PlayerMMrChange> entityPlayers)
    {
        var gameMode = GetProgressionGameMode(match.gameMode, entityPlayers[0]);

        var id = new BattleTagIdCombined(
            entityPlayers.Select(p => PlayerId.Create(p.battleTag)).ToList(),
            match.gateway,
            gameMode,
            match.season,
            match.gameMode == GameMode.GM_1v1 && match.season >= 2 ? (Race?)entityPlayers.Single().race : null);

        // Every AT team member carries the same rank snapshot (MM emits a shared team rank),
        // so the first member's snapshot represents the whole entity.
        var snapshot = entityPlayers[0].updatedProgression;

        var progression = await _progressionRepository.LoadProgression(id.Id) ?? PlayerProgression.Create(id);
        progression.RecordRank(snapshot.league, snapshot.division, snapshot.points, snapshot.apexPoints);

        await _progressionRepository.UpsertProgression(progression);
    }

    [NoTrace]
    private GameMode GetProgressionGameMode(GameMode gameMode, PlayerMMrChange player)
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

        if (gameMode == GameMode.GM_CF && player.IsAt)
        {
            return GameMode.GM_CF_AT;
        }

        if (gameMode == GameMode.GM_MINIDOTA_3ON3 && player.IsAt)
        {
            return GameMode.GM_MINIDOTA_3ON3_AT;
        }

        return gameMode;
    }
}
