using System.Collections.Generic;
using System.Threading.Tasks;
using MongoDB.Driver;
using W3ChampionsStatisticService.ReadModelBase;

namespace W3ChampionsStatisticService.PadEvents.FakeEventSync
{
    public class TempLossesRepo : MongoDbRepositoryBase, ITempLossesRepo
    {
        public async Task<List<RaceAndWinDto>> LoadLosses(string playerAccount)
        {
            var loadFirst = await LoadFirst<TempRemainingLosses>(l => l.Id == playerAccount);
            return loadFirst?.RemainingWins;
        }

        public async Task<List<RaceAndWinDto>> LoadWins(string playerAccount)
        {
            var loadFirst = await LoadFirst<TempRemainingWins>(l => l.Id == playerAccount);
            return loadFirst?.RemainingWins;
        }

        public Task SaveWins(string playerAccount, List<RaceAndWinDto> newDiffWins)
        {
            return Upsert(new TempRemainingWins(playerAccount, newDiffWins), r => r.Id == playerAccount);
        }

        public Task SaveLosses(string playerAccount, List<RaceAndWinDto> newDiffLosses)
        {
            return Upsert(new TempRemainingLosses(playerAccount, newDiffLosses), r => r.Id == playerAccount);
        }

        public TempLossesRepo(MongoClient mongoClient) : base(mongoClient)
        {
        }
    }
}