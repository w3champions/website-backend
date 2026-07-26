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
            var existing = await _playerRepository.LoadPlayerMmrRpTimeline(player.battleTag, player.race, match.gateway, match.season, match.gameMode);
            var mmrRpTimeline = existing ?? new PlayerMmrRpTimeline(player.battleTag, player.race, match.gateway, match.season, match.gameMode)
            {
                // A timeline starting now is fully populated from its first entry.
                // An existing one is only as complete as its oldest entry, so its
                // version is left alone for the backfill to raise; bumping it here
                // would claim the whole history has Rd when only the tail does.
                SchemaVersion = PlayerMmrRpTimeline.CurrentSchemaVersion,
            };

            // Games and DailyMaxMmr are left null: a single game on a day means
            // one game whose close is also its peak, which is what absence
            // already encodes. Rd is kept only while it still says something.
            mmrRpTimeline.UpdateTimeline(new MmrRpAtDate(
                mmr: (int)player.updatedMmr.rating,
                rp: player.ranking?.rp,
                date: DateTimeOffset.FromUnixTimeMilliseconds(match.endTime),
                rd: player.updatedMmr.rd >= PlayersObfuscator.RankDeviationObfuscationThreshold ? player.updatedMmr.rd : null));
            await _playerRepository.UpsertPlayerMmrRpTimeline(mmrRpTimeline);
        }
    }
}
