using System.Collections.Generic;
using System.Threading.Tasks;
using MongoDB.Driver;
using W3ChampionsStatisticService.Ladder;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.ReadModelBase;

namespace W3ChampionsStatisticService.PlayerProfiles
{
    public class PlayerRepository : MongoDbRepositoryBase, IPlayerRepository
    {
        public PlayerRepository(DbConnctionInfo connectionInfo) : base(connectionInfo)
        {
        }

        public async Task UpsertPlayer(PlayerProfile playerProfile)
        {
            await Upsert(playerProfile, p => p.Id == playerProfile.Id);
        }

        public async Task UpsertPlayer(PlayerOverview playerOverview)
        {
            await Upsert(playerOverview, p => p.Id == playerOverview.Id);
        }

        public Task<PlayerWinLoss> LoadPlayerWinrate(string playerId)
        {
            return LoadFirst<PlayerWinLoss>(p => p.Id == playerId);
        }

        public async Task Save(List<PlayerWinLoss> winrate)
        {
            foreach (var newWinrate in winrate)
            {
                await Upsert(newWinrate, p => p.Id == newWinrate.Id);
            }
        }

        public Task<PlayerProfile> Load(string battleTag)
        {
            return LoadFirst<PlayerProfile>(p => p.Id == battleTag);
        }

        public Task<PlayerOverview> LoadOverview(string battleTag)
        {
            return LoadFirst<PlayerOverview>(p => p.Id == battleTag);
        }

        public async Task<List<PlayerOverview>> LoadOverviewSince(int offset, int pageSize, int gateWay)
        {
            var database = CreateClient();
            var mongoCollection = database.GetCollection<PlayerOverview>(nameof(PlayerOverview));

            var playerOverviews = await mongoCollection.Find(m => m.GateWay == gateWay)
                .SortByDescending(s => s.MMR)
                .Skip(offset)
                .Limit(pageSize)
                .ToListAsync();

            return playerOverviews;
        }

        public async Task<List<PlayerOverview>> LoadOverviewLike(string searchFor, int gateWay)
        {
            if (string.IsNullOrEmpty(searchFor)) return new List<PlayerOverview>();
            var database = CreateClient();
            var mongoCollection = database.GetCollection<PlayerOverview>(nameof(PlayerOverview));

            var playerOverviews = await mongoCollection
                .Find(m => m.GateWay == gateWay && m.Id.ToLower().Contains(searchFor.ToLower()))
                .Limit(5)
                .ToListAsync();

            return playerOverviews;
        }
    }
}