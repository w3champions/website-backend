using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using W3C.Contracts.GameObjects;
using W3C.Contracts.Matchmaking;
using W3C.Domain.CommonValueObjects;
using W3C.Domain.GameModes;
using W3C.Domain.MatchmakingService;
using W3C.Domain.Tracing;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.ReadModelBase;

namespace W3ChampionsStatisticService.PlayerProfiles.ProgressionStats;

// Ingests MatchFinishedEvent.match.players[].updatedProgression into the season-keyed
// PlayerProgression read-model. One doc per entity: AT players grouped by atTeamId (shared
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

        // Arranged-team players share one rank doc per team (keyed by atTeamId — multiple AT teams can share a game team number).
        foreach (var team in placed.Where(p => p.IsAt).GroupBy(p => p.atTeamId))
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
        var gameMode = GameModesHelper.ToArrangedTeamVariant(match.gameMode, entityPlayers[0].IsAt);

        var id = new BattleTagIdCombined(
            entityPlayers.Select(p => PlayerId.Create(p.battleTag)).ToList(),
            match.gateway,
            gameMode,
            match.season,
            GameModesHelper.UsesRaceInLadderKey(match.gameMode, match.season) ? (Race?)entityPlayers.Single().race : null);

        // Every AT team member carries the same rank snapshot (MM emits a shared team rank),
        // so the first member's snapshot represents the whole entity.
        var snapshot = entityPlayers[0].updatedProgression;

        var progression = await _progressionRepository.LoadProgression(id.Id) ?? PlayerProgression.Create(id);
        progression.RecordRank(snapshot.league, snapshot.division, snapshot.points, snapshot.apexPoints);

        await _progressionRepository.UpsertProgression(progression);
    }
}
