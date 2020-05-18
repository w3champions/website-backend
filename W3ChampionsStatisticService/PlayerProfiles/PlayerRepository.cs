using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MongoDB.Driver;
using W3ChampionsStatisticService.CommonValueObjects;
using W3ChampionsStatisticService.Ladder;
using W3ChampionsStatisticService.PlayerProfiles._2v2Stats;
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
            await Upsert(playerProfile, p => p.BattleTag.Equals(playerProfile.BattleTag));
        }

        public async Task UpsertPlayerOverview(PlayerOverview playerOverview)
        {
            await Upsert(playerOverview, p => p.Id.Equals(playerOverview.Id));
        }

        public Task<PlayerWinLoss> LoadPlayerWinrate(string playerId, int season)
        {
            return LoadFirst<PlayerWinLoss>(p => p.Id == $"{season}_{playerId}");
        }

        public Task UpsertWins(List<PlayerWinLoss> winrate)
        {
            return UpsertMany(winrate);
        }

        public async Task<List<string>> LoadAllIds()
        {
            var mongoCollection = CreateCollection<PlayerProfile>();
            var overViews = await mongoCollection
                .Find(p => true)
                .SortBy(p => p.BattleTag)
                .Project(p => new { id = p.BattleTag })
                .ToListAsync();
            return overViews.Select(p => p.id).ToList();

        }

        public async Task<List<int>> LoadMmrs(int season)
        {
            var mongoCollection = CreateCollection<PlayerOverview>();
            var mmrs = await mongoCollection
                .Find(p => p.Season == season)
                .Project(p => p.MMR)
                .ToListAsync();
            return mmrs;
        }

        public Task<At2V2StatsPerGateway> LoadTeamStat(string id)
        {
            return LoadFirst<At2V2StatsPerGateway>(t => t.Id == id);
        }

        public Task UpsertTeamStat(At2V2StatsPerGateway stat)
        {
            return Upsert(stat, t => t.Id == stat.Id);
        }

        public Task<List<At2V2StatsPerGateway>> LoadPlayerTeamStatsWinrate(string battleTag, int season)
        {
            return LoadAll<At2V2StatsPerGateway>(t => t.Id == battleTag && t.Season == season);
        }

        public Task<PlayerProfile> LoadPlayer(string battleTag)
        {
            return LoadFirst<PlayerProfile>(p => p.BattleTag == battleTag);
        }

        public Task<PlayerOverview> LoadOverview(string battleTag)
        {
            return LoadFirst<PlayerOverview>(p => p.Id == battleTag);
        }

        public async Task<List<PlayerOverview>> LoadOverviewLike(string searchFor, GateWay gateWay)
        {
            if (string.IsNullOrEmpty(searchFor)) return new List<PlayerOverview>();
            var database = CreateClient();
            var mongoCollection = database.GetCollection<PlayerOverview>(nameof(PlayerOverview));

            var lower = searchFor.ToLower();
            var playerOverviews = await mongoCollection
                .Find(m => m.GateWay == gateWay && m.Id.Contains(lower))
                .Limit(5)
                .ToListAsync();

            return playerOverviews;
        }
    }
}