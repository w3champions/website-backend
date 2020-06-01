using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MongoDB.Driver;
using W3ChampionsStatisticService.CommonValueObjects;
using W3ChampionsStatisticService.Ladder;
using W3ChampionsStatisticService.PersonalSettings;
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

        public async Task UpsertPlayer(PlayerOverallStats playerOverallStats)
        {
            await Upsert(playerOverallStats, p => p.BattleTag == playerOverallStats.BattleTag);
        }

        public async Task UpsertPlayerOverview(PlayerOverview playerOverview)
        {
            await Upsert(playerOverview);
        }

        public Task<PlayerWinLoss> LoadPlayerWinrate(string playerId, int season)
        {
            return LoadFirst<PlayerWinLoss>($"{season}_{playerId}");
        }

        public async Task<List<PlayerDetails>> LoadPlayersRaceWins(string[] playerIds)
        {
            var database = CreateClient();

            var playerRaceWins = database.GetCollection<PlayerDetails>(nameof(PlayerOverallStats));
            var personalSettings = database.GetCollection<PersonalSetting>(nameof(PersonalSetting));

            return await playerRaceWins
                .Aggregate()
                .Match(x => playerIds.Contains(x.Id))
                .Lookup<PlayerDetails, PersonalSetting, PlayerDetails>(personalSettings,
                    raceWins => raceWins.Id,
                    settings => settings.Id,
                    details => details.PersonalSettings)
                .ToListAsync();
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

        public Task<List<PlayerOverallStats>> SearchForPlayer(string search)
        {
            return LoadAll<PlayerOverallStats>(p => p.BattleTag.ToLower().Contains(search), 5);
        }

        public Task<PlayerGameModeStatPerGateway> LoadGameModeStatPerGateway(string id)
        {
            return LoadFirst<PlayerGameModeStatPerGateway>(id);
        }

        public Task UpsertPlayerGameModeStatPerGateway(PlayerGameModeStatPerGateway stat)
        {
            return Upsert(stat);
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
            return LoadFirst<PlayerRaceStatPerGateway>($"{season}_{battleTag}_@{gateWay}_{race}");
        }

        public Task UpsertPlayerRaceStat(PlayerRaceStatPerGateway stat)
        {
            return Upsert(stat);
        }

        public Task<PlayerOverallStats> LoadPlayerProfile(string battleTag)
        {
            return LoadFirst<PlayerOverallStats>(p => p.BattleTag == battleTag);
        }

        public Task<PlayerOverview> LoadOverview(string battleTag)
        {
            return LoadFirst<PlayerOverview>(battleTag);
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