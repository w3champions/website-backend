using System.Linq;
using System.Threading.Tasks;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.ReadModelBase;

namespace W3ChampionsStatisticService.Ladder
{
    public class RankHandler : IAsyncUpdatable
    {
        private readonly IMatchEventRepository _matchEventRepository;

        public RankHandler(
            IMatchEventRepository matchEventRepository
            )
        {
            _matchEventRepository = matchEventRepository;
        }

        public async Task Update()
        {
            var events = await _matchEventRepository.LoadRanks();
            var ranks = events.SelectMany(x =>
                x.ranks.Select((r, i) =>
                    new Rank(x.gateway, x.league, i, r.rp, r.id))).ToList();

            await _matchEventRepository.Insert(ranks);
        }
    }
}