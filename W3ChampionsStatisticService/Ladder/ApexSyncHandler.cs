using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using W3C.Domain.Repositories;
using W3ChampionsStatisticService.ReadModelBase;
using W3C.Domain.Tracing;

namespace W3ChampionsStatisticService.Ladder;

[Trace]
public class ApexSyncHandler(
    IApexLeaderboardRepository apexLeaderboardRepository,
    IMatchEventRepository matchEventRepository
) : IAsyncUpdatable
{
    private readonly IApexLeaderboardRepository _apexLeaderboardRepository = apexLeaderboardRepository;
    private readonly IMatchEventRepository _matchEventRepository = matchEventRepository;

    public async Task Update()
    {
        var events = await _matchEventRepository.CheckoutApexStandingsChanged();

        foreach (var ev in events)
        {
            var players = ev.players?
                .Select((raw, index) => new ApexLeaderboardEntry
                {
                    BattleTags = raw.battleTags ?? new List<string>(),
                    Race = raw.race,
                    ApexPoints = raw.apexPoints,
                    League = raw.league,
                    RankNumber = index + 1,
                })
                .ToList() ?? new List<ApexLeaderboardEntry>();

            var leaderboard = new ApexLeaderboard
            {
                Id = $"{ev.season}_{(int)ev.gameMode}",
                Season = ev.season,
                GameMode = ev.gameMode,
                CutoffApexPoints = ev.cutoffApexPoints,
                GmCount = ev.gmCount,
                Players = players,
            };

            await _apexLeaderboardRepository.UpsertOne(leaderboard);
        }
    }
}
