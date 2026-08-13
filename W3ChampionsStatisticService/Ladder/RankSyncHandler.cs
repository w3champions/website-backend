using System.Linq;
using System.Threading.Tasks;
using W3C.Domain.Repositories;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.ReadModelBase;
using W3C.Domain.Tracing;

namespace W3ChampionsStatisticService.Ladder;

[Trace]
public class RankSyncHandler(
    IRankRepository rankRepository,
    IMatchEventRepository matchEventRepository,
    IFriendRankPromotionNotifier friendRankPromotionNotifier
        ) : IAsyncUpdatable
{
    private readonly IRankRepository _rankRepository = rankRepository;
    private readonly IMatchEventRepository _matchEventRepository = matchEventRepository;
    private readonly IFriendRankPromotionNotifier _friendRankPromotionNotifier = friendRankPromotionNotifier;

    public async Task Update()
    {
        var events = await _matchEventRepository.CheckoutForRead();

        var ranks = events.SelectMany(changedEvent => changedEvent.ranks
            .OrderByDescending(r => r.rp)
            .Select((r, i) =>
                new Rank(r.battleTags,
                    changedEvent.league,
                    i + 1,
                    r.rp,
                    r.race,
                    changedEvent.gateway,
                    changedEvent.gameMode,
                    changedEvent.season))
            .ToList())
            .ToList();

        // Snapshot the standings this batch replaces, BEFORE the upsert overwrites them.
        // The notifier swallows its own failures; InsertRanks stays unconditional.
        var oldRanks = await _friendRankPromotionNotifier.CaptureOldRanks(ranks);

        await _rankRepository.InsertRanks(ranks);

        await _friendRankPromotionNotifier.NotifyPromotions(ranks, oldRanks);
    }
}
