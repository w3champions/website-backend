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
        public PlayerRepository(MongoClient mongoClient) : base(mongoClient)
        {
        }

        public async Task UpsertPlayer(PlayerProfile playerProfile)
        {
            await Upsert(playerProfile, p => p.Id.Equals(playerProfile.Id));
        }

        public async Task UpsertPlayer(PlayerOverview1v1 playerOverview1V1)
        {
            await Upsert(playerOverview1V1, p => p.Id.Equals(playerOverview1V1.Id));
        }

        public Task<PlayerWinLoss> LoadPlayerWinrate(string playerId)
        {
            return LoadFirst<PlayerWinLoss>(p => p.Id == playerId);
        }

        public Task Save(List<PlayerWinLoss> winrate)
        {
            return UpsertMany(winrate);
        }

        public Task<PlayerProfile> Load(string battleTag)
        {
            return LoadFirst<PlayerProfile>(p => p.Id == battleTag);
        }

        public Task<PlayerOverview1v1> LoadOverview(string battleTag)
        {
            return LoadFirst<PlayerOverview1v1>(p => p.Id == battleTag);
        }

        public async Task<List<PlayerOverview1v1>> LoadOverviewLike(string searchFor, int gateWay)
        {
            if (string.IsNullOrEmpty(searchFor)) return new List<PlayerOverview1v1>();
            var database = CreateClient();
            var mongoCollection = database.GetCollection<PlayerOverview1v1>(nameof(PlayerOverview1v1));

            var lower = searchFor.ToLower();
            var playerOverviews = await mongoCollection
                .Find(m => m.GateWay == gateWay && m.Id.Contains(lower))
                .Limit(5)
                .ToListAsync();

            return playerOverviews;
        }
    }
}