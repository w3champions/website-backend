using System;
using System.Threading.Tasks;
using W3C.Domain.MatchmakingService;
using W3ChampionsStatisticService.Matches;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.ReadModelBase;
using W3C.Domain.Tracing;
using Serilog;

namespace W3ChampionsStatisticService.PlayerProfiles.MmrRankingStats;

[Trace]
public class PlayerMmrRpTimelineHandler(IPlayerRepository playerRepository) : IMatchFinishedReadModelHandler
{
    private readonly IPlayerRepository _playerRepository = playerRepository;

    /// <summary>How many times to reload and reapply before handing the event back to the pipeline.</summary>
    private const int MaxWriteAttempts = 5;

    public async Task Update(MatchFinishedEvent nextEvent)
    {
        var match = nextEvent.match;


        if (match.endTime == 0)
        {
            Log.Information("Finished match {FinishedMatchId} has no end time, skipping processing", match.id);
            return;
        }

        foreach (var player in match.players)
        {
            if (player.IsAt)
            {
                Log.Information("Player {Player} in finished match {FinishedMatchId} is in an arranged team {AtTeamId}, skipping processing", player.battleTag, match.id, player.atTeamId);
                continue;
            }
            if (player.updatedMmr == null)
            {
                Log.Information("Player {Player} in finished match {FinishedMatchId} has no updated MMR, skipping processing", player.battleTag);
                continue;
            }
            await UpdateTimelineForPlayer(match, player);
        }
    }

    /// <summary>
    /// Folds one player's result into their timeline, retrying if the document moves
    /// underneath us.
    ///
    /// The whole read-modify-write repeats rather than just the write: the merge
    /// depends on what the day already holds, so reapplying it to a stale copy would
    /// reinstate the state the other writer replaced. The backfill job rewrites these
    /// same documents whole while this handler is live, which is what makes the
    /// conflict real rather than theoretical.
    /// </summary>
    private async Task UpdateTimelineForPlayer(Match match, PlayerMMrChange player)
    {
        for (var attempt = 1; attempt <= MaxWriteAttempts; attempt++)
        {
            var existing = await _playerRepository.LoadPlayerMmrRpTimeline(player.battleTag, player.race, match.gateway, match.season, match.gameMode);

            // The event pipeline delivers at least once, and the caller commits one
            // player at a time - so a throw on a later player leaves the earlier ones
            // written and the whole event is retried. Re-folding a match that is
            // already in the timeline would inflate its games count on every retry,
            // without bound. Skipping is also what makes the retry cheap: the players
            // that already succeeded are no longer rewritten.
            if (existing?.LastProcessedMatchId == match.id)
            {
                Log.Information("Match {FinishedMatchId} is already in {Player}'s timeline, skipping", match.id, player.battleTag);
                return;
            }

            var mmrRpTimeline = existing ?? new PlayerMmrRpTimeline(player.battleTag, player.race, match.gateway, match.season, match.gameMode);

            // Games and DailyMaxMmr are left null: a single game on a day means
            // one game whose close is also its peak, which is what absence
            // already encodes. Rd is kept only while it still says something.
            mmrRpTimeline.UpdateTimeline(new MmrRpAtDate(
                mmr: (int)player.updatedMmr.rating,
                rp: player.ranking?.rp,
                date: DateTimeOffset.FromUnixTimeMilliseconds(match.endTime),
                rd: player.updatedMmr.rd >= PlayersObfuscator.RankDeviationObfuscationThreshold ? player.updatedMmr.rd : null));
            mmrRpTimeline.LastProcessedMatchId = match.id;

            if (await _playerRepository.TryUpsertPlayerMmrRpTimeline(mmrRpTimeline, existing?.Revision))
            {
                return;
            }

            Log.Information("Timeline {TimelineId} changed while folding in match {FinishedMatchId}, retrying ({Attempt}/{MaxAttempts})",
                mmrRpTimeline.Id, match.id, attempt, MaxWriteAttempts);
        }

        // Give up and let the pipeline retry the whole event. That is safe now that
        // LastProcessedMatchId makes re-folding a no-op for players already written.
        throw new InvalidOperationException(
            $"Could not write {player.battleTag}'s MMR timeline for match {match.id} after {MaxWriteAttempts} attempts; it kept being modified concurrently.");
    }
}
