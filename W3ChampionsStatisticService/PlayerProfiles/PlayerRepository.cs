using System.Collections.Generic;
using System.Threading.Tasks;
using MongoDB.Driver;
using W3ChampionsStatisticService.CommonValueObjects;
using W3ChampionsStatisticService.Ladder;
using W3ChampionsStatisticService.PlayerProfiles.GameModeStats;
using W3ChampionsStatisticService.PlayerProfiles.RaceStats;
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

        public async Task<List<int>> LoadMmrs(int season)
        {
            var mongoCollection = CreateCollection<PlayerOverview>();
            var mmrs = await mongoCollection
                .Find(p => p.Season == season)
                .Project(p => p.MMR)
                .ToListAsync();
            return mmrs;
        }

        public Task<PlayerGameModeStatPerGateway> LoadGameModeStatPerGateway(string id)
        {
            return LoadFirst<PlayerGameModeStatPerGateway>(t => t.Id == id);
        }

        public Task UpsertPlayerGameModeStatPerGateway(PlayerGameModeStatPerGateway stat)
        {
            return Upsert(stat, t => t.Id == stat.Id);
        }

        public Task<List<PlayerGameModeStatPerGateway>> LoadGameModeStatPerGateway(string battleTag,
            GateWay gateWay,
            int season)
        {
            return LoadAll<PlayerGameModeStatPerGateway>(t =>
                t.Id.Contains(battleTag) &&
                t.GateWay == gateWay &&
                t.Season == season );
        }

        public Task<List<PlayerRaceStatPerGateway>> LoadRaceStatPerGateway(string battleTag, GateWay gateWay, int season)
        {
            return LoadAll<PlayerRaceStatPerGateway>(t => t.Id.StartsWith($"{season}_{battleTag}_@{gateWay}"));
        }

        public Task<PlayerRaceStatPerGateway> LoadRaceStatPerGateway(string battleTag, Race race, GateWay gateWay, int season)
        {
            return LoadFirst<PlayerRaceStatPerGateway>(t => t.Id == $"{season}_{battleTag}_@{gateWay}_{race}");
        }

        public Task UpsertPlayerRaceStat(PlayerRaceStatPerGateway stat)
        {
            return Upsert(stat, t => t.Id == stat.Id);
        }

        public Task<PlayerProfile> LoadPlayerProfile(string battleTag)
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