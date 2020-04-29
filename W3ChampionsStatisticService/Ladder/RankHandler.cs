using System.Linq;
using System.Threading.Tasks;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.ReadModelBase;

namespace W3ChampionsStatisticService.Ladder
{
    public class RankHandler : IAsyncUpdatable
    {
        private readonly IRankRepository _rankRepository;
        private readonly IMatchEventRepository _matchEventRepository;

        public RankHandler(
            IRankRepository rankRepository,
            IMatchEventRepository matchEventRepository
            )
        {
            _rankRepository = rankRepository;
            _matchEventRepository = matchEventRepository;
        }

        public async Task Update()
        {
            var events = await _matchEventRepository.LoadLatestRanks();

            foreach (var changedEvent in events)
            {
                var ranks = changedEvent.ranks
                    .OrderByDescending(r => r.rp)
                    .Select((r, i) =>
                    new Rank(changedEvent.gateway, changedEvent.league, i + 1, (int) r.rp, r.tagId)).ToList();

                await _rankRepository.Insert(ranks);

                await _matchEventRepository.DeleteRankEvent(changedEvent.id);
            }
        }
    }
}