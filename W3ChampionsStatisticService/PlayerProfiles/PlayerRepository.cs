using MongoDB.Driver;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using W3C.Contracts.GameObjects;
using W3C.Domain.Repositories;
using W3ChampionsStatisticService.Ladder;
using W3ChampionsStatisticService.PersonalSettings;
using W3ChampionsStatisticService.PlayerProfiles.GameModeStats;
using W3ChampionsStatisticService.PlayerProfiles.MmrRankingStats;
using W3ChampionsStatisticService.PlayerProfiles.RaceStats;
using W3ChampionsStatisticService.Ports;
using W3C.Contracts.Matchmaking;
using W3ChampionsStatisticService.PlayerStats.GameLengthForPlayerStatistics;

namespace W3ChampionsStatisticService.PlayerProfiles;

public class PlayerRepository(MongoClient mongoClient) : MongoDbRepositoryBase(mongoClient), IPlayerRepository
{
    public async Task UpsertPlayer(PlayerOverallStats playerOverallStats)
    {
        await Upsert(playerOverallStats, Builders<PlayerOverallStats>.Filter.Eq(p => p.BattleTag, playerOverallStats.BattleTag));
    }

    public async Task UpsertPlayerOverview(PlayerOverview playerOverview)
    {
        await Upsert(playerOverview);
    }

    public Task<PlayerWinLoss> LoadPlayerWinrate(string playerId, int season)
    {
        return LoadFirst<PlayerWinLoss>($"{season}_{playerId}");
    }

    public async Task<List<PlayerDetails>> LoadPlayersRaceWins(List<string> playerIds)
    {
        var aggregator = Aggregate<PlayerDetails>()
            .Match(Builders<PlayerDetails>.Filter.In(p => p.Id, playerIds));

        var lookup = Lookup<PlayerDetails, PersonalSetting, PlayerDetails>(aggregator,
                raceWins => raceWins.Id,
                settings => settings.Id,
                details => details.PersonalSettings);

        return await lookup.ToListAsync();
    }

    public Task UpsertWins(List<PlayerWinLoss> winrate)
    {
        return UpsertMany(winrate);
    }

    public async Task<List<int>> LoadMmrs(int season, GateWay gateWay, GameMode gameMode)
    {
        var filter = Builders<PlayerOverview>.Filter.And(
            Builders<PlayerOverview>.Filter.Eq(p => p.Season, season),
            Builders<PlayerOverview>.Filter.Eq(p => p.GateWay, gateWay),
            Builders<PlayerOverview>.Filter.Eq(p => p.GameMode, gameMode)
        );
        
        var mmrs = await Find(filter).Project(p => p.MMR).ToListAsync();
        return mmrs;
    }

    public Task<List<PlayerOverallStats>> SearchForPlayer(string search)
    {
        var filter = Builders<PlayerOverallStats>.Filter.Regex(
            p => p.BattleTag, 
            new MongoDB.Bson.BsonRegularExpression(search, "i"));
        return LoadAll(filter);
    }

    public Task<PlayerGameModeStatPerGateway> LoadGameModeStatPerGateway(string id)
    {
        return LoadFirst<PlayerGameModeStatPerGateway>(id);
    }

    public Task UpsertPlayerGameModeStatPerGateway(PlayerGameModeStatPerGateway stat)
    {
        return Upsert(stat);
    }

    public Task<List<PlayerGameModeStatPerGateway>> LoadGameModeStatPerGateway(
        string battleTag,
        GateWay gateWay,
        int season)
    {
        return LoadAll(
            Builders<PlayerGameModeStatPerGateway>.Filter.And(
                Builders<PlayerGameModeStatPerGateway>.Filter.ElemMatch(p => p.PlayerIds, player => player.BattleTag == battleTag),
                Builders<PlayerGameModeStatPerGateway>.Filter.Eq(p => p.GateWay, gateWay),
                Builders<PlayerGameModeStatPerGateway>.Filter.Eq(p => p.Season, season)
            )
        );
    }

    public Task<List<PlayerRaceStatPerGateway>> LoadRaceStatPerGateway(string battleTag, GateWay gateWay, int season)
    {
        return LoadAll(
            Builders<PlayerRaceStatPerGateway>.Filter.And(
                Builders<PlayerRaceStatPerGateway>.Filter.Eq(p => p.BattleTag, battleTag),
                Builders<PlayerRaceStatPerGateway>.Filter.Eq(p => p.Season, season),
                Builders<PlayerRaceStatPerGateway>.Filter.Eq(p => p.GateWay, gateWay)
            )
        );
    }

    public Task<PlayerRaceStatPerGateway> LoadRaceStatPerGateway(string battleTag, Race race, GateWay gateWay, int season)
    {
        return LoadFirst(
            Builders<PlayerRaceStatPerGateway>.Filter.And(
                Builders<PlayerRaceStatPerGateway>.Filter.Eq(p => p.BattleTag, battleTag),
                Builders<PlayerRaceStatPerGateway>.Filter.Eq(p => p.Season, season),
                Builders<PlayerRaceStatPerGateway>.Filter.Eq(p => p.GateWay, gateWay),
                Builders<PlayerRaceStatPerGateway>.Filter.Eq(p => p.Race, race)
            )
        );
    }

    public Task UpsertPlayerRaceStat(PlayerRaceStatPerGateway stat)
    {
        return Upsert(stat);
    }

    public Task<PlayerOverallStats> LoadPlayerOverallStats(string battleTag)
    {
        return LoadFirst(Builders<PlayerOverallStats>.Filter.Eq(p => p.BattleTag, battleTag));
    }

    public Task<PlayerOverview> LoadOverview(string battleTag)
    {
        return LoadFirst<PlayerOverview>(battleTag);
    }

    public Task<List<PlayerOverview>> LoadOverviews(int season)
    {
        return LoadAll(Builders<PlayerOverview>.Filter.Eq(p => p.Season, season));
    }

    public async Task<Dictionary<string, PlayerOverallStats>> GetPlayerBattleTagsAsync(
        ICollection<string> personalSettingIds)
    {
        // Fetch corresponding stats to fill in seasons
        var playerStats = await LoadAll(
            Builders<PlayerOverallStats>.Filter.In(p => p.BattleTag, personalSettingIds)
        );
        var playerStatsMap = playerStats.ToDictionary(ps => ps.BattleTag);
        return playerStatsMap;
    }

    public Task<PlayerMmrRpTimeline> LoadPlayerMmrRpTimeline(string battleTag, Race race, GateWay gateWay, int season, GameMode gameMode)
    {
        return LoadFirst<PlayerMmrRpTimeline>($"{season}_{battleTag}_@{gateWay}_{race}_{gameMode}");
    }

    public Task UpsertPlayerMmrRpTimeline(PlayerMmrRpTimeline mmrRpTimeline)
    {
        return Upsert(mmrRpTimeline);
    }

    public Task<PlayerGameLength> LoadGameLengthForPlayerStats(string battleTag, int season)
    {
        var compoundId = PlayerGameLength.CompoundId(battleTag, season);
        return LoadFirst<PlayerGameLength>(compoundId);
    }

    public async Task<PlayerGameLength> LoadOrCreateGameLengthForPlayerStats(string battleTag, int season)
    {
        var compoundId = PlayerGameLength.CompoundId(battleTag, season);
        var gameLengthsForPlayer = await LoadAll(
            Builders<PlayerGameLength>.Filter.Eq(p => p.Id, compoundId)
        );

        if (gameLengthsForPlayer.Count > 0)
        {
            return gameLengthsForPlayer[0];
        }

        return new PlayerGameLength
        {
            BattleTag = battleTag,
            Season = season,
            PlayerGameLengthIntervalByOpponentRace = new Dictionary<string, PlayerGameLengthStat>(),
            GameLengthsByOpponentRace = new Dictionary<string, List<int>>(),
            AverageGameLengthByOpponentRace = new Dictionary<string, int>()
        };
    }
    public Task Save(PlayerGameLength gameLengthStats)
    {
        return Upsert(gameLengthStats);
    }
}
