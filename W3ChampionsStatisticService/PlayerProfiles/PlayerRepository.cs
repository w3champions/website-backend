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
using W3ChampionsStatisticService.PlayerProfiles.GlobalSearch;

namespace W3ChampionsStatisticService.PlayerProfiles;

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

    public async Task<List<PlayerDetails>> LoadPlayersRaceWins(List<string> playerIds)
    {
        var playerRaceWins = CreateCollection<PlayerDetails>(nameof(PlayerOverallStats));
        var personalSettings = CreateCollection<PersonalSetting>();

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

    public async Task<List<int>> LoadMmrs(int season, GateWay gateWay, GameMode gameMode)
    {
        var mongoCollection = CreateCollection<PlayerOverview>();
        var mmrs = await mongoCollection
            .Find(p => p.Season == season &&
                        p.GateWay == gateWay &&
                        p.GameMode == gameMode)
            .Project(p => p.MMR)
            .ToListAsync();
        return mmrs;
    }

    public Task<List<PlayerOverallStats>> SearchForPlayer(string search)
    {
        var lower = search.ToLower();
        return LoadAll<PlayerOverallStats>(p => p.BattleTag.ToLower().Contains(lower));
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
        return LoadAll<PlayerGameModeStatPerGateway>(t =>
            t.PlayerIds.Any(player => player.BattleTag.ToLower() == battleTag.ToLower()) &&
            t.GateWay == gateWay &&
            t.Season == season);
    }

    public Task<List<PlayerRaceStatPerGateway>> LoadRaceStatPerGateway(string battleTag, GateWay gateWay, int season)
    {
        return LoadAll<PlayerRaceStatPerGateway>(t => t.BattleTag.ToLower() == battleTag.ToLower() && t.Season == season && t.GateWay == gateWay);
    }

    public Task<PlayerRaceStatPerGateway> LoadRaceStatPerGateway(string battleTag, Race race, GateWay gateWay, int season)
    {
        return LoadFirst<PlayerRaceStatPerGateway>(t => t.BattleTag.ToLower() == battleTag.ToLower() && t.Season == season && t.GateWay == gateWay && t.Race == race);
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

    public Task<List<PlayerOverview>> LoadOverviews(int season)
    {
        return LoadAll<PlayerOverview>(t => t.Season == season);
    }

    public async Task<Dictionary<string, PlayerOverallStats>> GetPlayerBattleTagsAsync(
        ICollection<string> personalSettingIds)
    {
        // Fetch corresponding stats to fill in seasons
        var playerStats = await CreateCollection<PlayerOverallStats>()
            .Find(ps => personalSettingIds.Contains(ps.BattleTag))
            .ToListAsync();
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
}
