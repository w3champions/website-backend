using System.Linq;
using System.Threading.Tasks;
using W3ChampionsStatisticService.PadEvents;
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
            var events = await _matchEventRepository.CheckoutForRead();
            if (events.Count == 0) return;

            var ranks = events.SelectMany(changedEvent => changedEvent.ranks
                .OrderByDescending(r => r.rp)
                .Select((r, i) =>
                    new Rank(
                        changedEvent.gateway,
                        changedEvent.league,
                        i + 1,
                        (int) r.rp,
                        CreatPlayerId(changedEvent, r),
                        changedEvent.gameMode)).ToList()).ToList();

            await _rankRepository.InsertRanks(ranks);
        }

        private static string CreatPlayerId(RankingChangedEvent changedEvent, RankRaw r)
        {
            var btags = r.battleTags.Select(b => $"{b}@{(int)changedEvent.gateway}").OrderBy(t => t);
            var creatPlayerId = $"{string.Join("_", btags)}_{changedEvent.gameMode}";
            return creatPlayerId;
        }
    }
}