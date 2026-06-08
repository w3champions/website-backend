using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using W3C.Contracts.GameObjects;
using W3C.Domain.GameModes;
using W3C.Domain.MatchmakingService;
using W3C.Domain.Tracing;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.ReadModelBase;

namespace W3ChampionsStatisticService.PlayerProfiles.ProgressionStats;

// Ingests MatchFinishedEvent into the permanent per-player peak-rank store.
// Only non-arranged-team players with a placed rank (updatedProgression != null) are recorded;
// AT rank belongs to the team, not to an individual player's prestige peak.
[Trace]
public class ProgressionPrestigeHandler(IProgressionPrestigeRepository repository) : IMatchFinishedReadModelHandler
{
    private readonly IProgressionPrestigeRepository _repository = repository;

    public async Task Update(MatchFinishedEvent nextEvent)
    {
        if (nextEvent.WasFakeEvent)
        {
            return;
        }

        var match = nextEvent.match;
        var players = match.players ?? new List<PlayerMMrChange>();
        var achievedAt = DateTimeOffset.FromUnixTimeMilliseconds(match.endTime > 0 ? match.endTime : match.startTime);

        // Only individual-rank placements: arranged-team rank is the team's, not attributable to one player's peak.
        var placed = players.Where(p => p.updatedProgression != null && !p.IsAt);
        foreach (var player in placed)
        {
            // The peak entry key is (gameMode, race) with per-season peaks held inside, so the race key must be
            // season-agnostic. Unlike the season-keyed ladder (UsesRaceInLadderKey gates on RaceSplitStartSeason),
            // we include race for a race-split mode in ALL seasons; otherwise a player's lifetime 1v1 peak would
            // fragment across a race-less entry (pre-season-2) and per-race entries.
            var race = GameModesHelper.IsRaceSplitGameMode(match.gameMode) ? (Race?)player.race : null;
            var candidate = new PeakRank
            {
                League = player.updatedProgression.league,
                Division = player.updatedProgression.division,
                Points = player.updatedProgression.points,
                ApexPoints = player.updatedProgression.apexPoints,
                Season = match.season,
                AchievedAt = achievedAt,
            };

            var prestige = await _repository.LoadPrestige(player.battleTag) ?? ProgressionPrestige.Create(player.battleTag);
            prestige.RecordPeak(match.gameMode, race, candidate);
            await _repository.UpsertPrestige(prestige);
        }
    }
}
