using System;
using System.Threading.Tasks;
using W3C.Domain.MatchmakingService;
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

        
        if (match.endTime == 0) {
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
            if (player.updatedMmr == null) {
                Log.Information("Player {Player} in finished match {FinishedMatchId} has no updated MMR, skipping processing", player.battleTag);
                continue; 
            }
            var mmrRpTimeline = await _playerRepository.LoadPlayerMmrRpTimeline(player.battleTag, player.race, match.gateway, match.season, match.gameMode)
                        ?? new PlayerMmrRpTimeline(player.battleTag, player.race, match.gateway, match.season, match.gameMode);
            mmrRpTimeline.UpdateTimeline(new MmrRpAtDate(
                mmr: (int)player.updatedMmr.rating,
                rp: player.ranking?.rp,
                date: DateTimeOffset.FromUnixTimeMilliseconds(match.endTime)));
            await _playerRepository.UpsertPlayerMmrRpTimeline(mmrRpTimeline);
        }
    }
}
