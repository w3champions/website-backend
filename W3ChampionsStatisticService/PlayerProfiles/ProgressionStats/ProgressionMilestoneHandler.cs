using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using W3C.Contracts.GameObjects;
using W3C.Domain.CommonValueObjects;
using W3C.Domain.GameModes;
using W3C.Domain.MatchmakingService;
using W3C.Domain.Tracing;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.ReadModelBase;

namespace W3ChampionsStatisticService.PlayerProfiles.ProgressionStats;

// Ingests MatchFinishedEvent into the permanent per-entity/mode/race win-milestone store.
// Mirrors PlayerProgressionHandler's AT grouping + keying, but is driven by the per-player `won`
// flag rather than `updatedProgression`, so lifetime wins accrue regardless of whether a rank was
// recorded for the match. Uses a season-less key so totals accumulate across seasons. totalWins
// increments on a win; the weekly activity window records every game (won or lost), pruned to ~90 days.
[Trace]
public class ProgressionMilestoneHandler(IProgressionMilestoneRepository milestoneRepository) : IMatchFinishedReadModelHandler
{
    private readonly IProgressionMilestoneRepository _milestoneRepository = milestoneRepository;

    public async Task Update(MatchFinishedEvent nextEvent)
    {
        if (nextEvent.WasFakeEvent)
        {
            return;
        }

        var match = nextEvent.match;
        var players = match.players ?? new List<PlayerMMrChange>();
        var rawMs = match.endTime > 0 ? match.endTime : match.startTime;
        var playedAt = DateTimeOffset.FromUnixTimeMilliseconds(rawMs);

        foreach (var team in players.Where(p => p.IsAt).GroupBy(p => p.atTeamId))
        {
            await RecordEntity(match, team.ToList(), playedAt);
        }

        foreach (var soloPlayer in players.Where(p => !p.IsAt))
        {
            await RecordEntity(match, new List<PlayerMMrChange> { soloPlayer }, playedAt);
        }
    }

    private async Task RecordEntity(Match match, List<PlayerMMrChange> entityPlayers, DateTimeOffset playedAt)
    {
        var first = entityPlayers[0];
        var playerIds = entityPlayers.Select(p => PlayerId.Create(p.battleTag)).ToList();
        var gameMode = GameModesHelper.ToArrangedTeamVariant(match.gameMode, first.IsAt);
        // Milestones accumulate across seasons, so the key must be season-less and stable. Unlike the
        // season-keyed ladder (UsesRaceInLadderKey gates on RaceSplitStartSeason), we include race for a
        // race-split mode in ALL seasons so pre-season-2 wins roll up into the same per-race doc.
        var race = GameModesHelper.IsRaceSplitGameMode(match.gameMode)
            ? (Race?)entityPlayers.Single().race
            : null;

        var id = ProgressionMilestone.BuildId(playerIds, match.gateway, gameMode, race);
        var milestone = await _milestoneRepository.LoadMilestone(id)
                        ?? ProgressionMilestone.Create(playerIds, match.gateway, gameMode, race);

        // Every member of an AT team shares the same match result (MM emits one team outcome), so
        // entityPlayers[0].won represents the whole entity. Activity is recorded for every game (won
        // or lost); PruneStaleActivity runs after RecordActivity — the just-added bucket can never be
        // its own victim because the prune margin exceeds the window.
        if (first.won)
        {
            milestone.RecordWin();
        }
        milestone.RecordActivity(playedAt);
        milestone.PruneStaleActivity(playedAt); // model owns the window+margin policy

        await _milestoneRepository.UpsertMilestone(milestone);
    }
}
