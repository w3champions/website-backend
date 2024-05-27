using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Linq;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using W3C.Contracts.GameObjects;
using W3C.Domain.CommonValueObjects;
using W3C.Domain.MatchmakingService;
using W3C.Domain.Repositories;
using W3ChampionsStatisticService.Ports;
using W3C.Contracts.Matchmaking;
using W3ChampionsStatisticService.Ladder;

namespace W3ChampionsStatisticService.Matches;

public class MatchRepository : MongoDbRepositoryBase, IMatchRepository
{
    private readonly IOngoingMatchesCache _cache;

    public MatchRepository(MongoClient mongoClient, IOngoingMatchesCache cache) : base(mongoClient)
    {
        _cache = cache;
    }

    public Task Insert(Matchup matchup)
    {
        return Upsert(matchup, m => m.MatchId == matchup.MatchId);
    }

    public async Task<List<Matchup>> LoadFor(
        string playerId,
        string opponentId = null,
        GateWay gateWay = GateWay.Undefined,
        GameMode gameMode = GameMode.Undefined,
        Race playerRace = Race.Total,
        Race opponentRace = Race.Total,
        int pageSize = 100,
        int offset = 0,
        int season = 1)
    {
        var mongoCollection = CreateCollection<Matchup>();
        var textSearchOpts = new TextSearchOptions();
        if (string.IsNullOrEmpty(opponentId))
        {
            return await mongoCollection
                .Find(m => Builders<Matchup>.Filter.Text($"\"{playerId}\"", textSearchOpts).Inject()
                    && (gameMode == GameMode.Undefined || m.GameMode == gameMode)
                    && (gateWay == GateWay.Undefined || m.GateWay == gateWay)
                    && (playerRace == Race.Total || m.Teams.Any(team => team.Players[0].Race == playerRace && playerId == team.Players[0].BattleTag))
                    && (opponentRace == Race.Total || m.Teams.Any(team => team.Players[0].Race == opponentRace && playerId != team.Players[0].BattleTag))
                    && (m.Season == season))
                .SortByDescending(s => s.Id)
                .Skip(offset)
                .Limit(pageSize)
                .ToListAsync();
        }

        return await mongoCollection
            .Find(m =>
                Builders<Matchup>.Filter.Text($"\"{playerId}\" \"{opponentId}\"", textSearchOpts).Inject()
                && (gameMode == GameMode.Undefined || m.GameMode == gameMode)
                && (gateWay == GateWay.Undefined || m.GateWay == gateWay)
                && (m.Season == season))
            .SortByDescending(s => s.Id)
            .Skip(offset)
            .Limit(pageSize)
            .ToListAsync();
    }

    public Task<long> CountFor(
        string playerId,
        string opponentId = null,
        GateWay gateWay = GateWay.Undefined,
        GameMode gameMode = GameMode.Undefined,
        Race playerRace = Race.Total,
        Race opponentRace = Race.Total,
        int season = 1)
    {
        var textSearchOpts = new TextSearchOptions();
        var mongoCollection = CreateCollection<Matchup>();
        if (string.IsNullOrEmpty(opponentId))
        {
            return mongoCollection.CountDocumentsAsync(m =>
                Builders<Matchup>.Filter.Text($"\"{playerId}\"", textSearchOpts).Inject()
                && (gameMode == GameMode.Undefined || m.GameMode == gameMode)
                && (gateWay == GateWay.Undefined || m.GateWay == gateWay)
                && (playerRace == Race.Total || m.Teams.Any(team => team.Players[0].Race == playerRace && playerId == team.Players[0].BattleTag))
                && (opponentRace == Race.Total || m.Teams.Any(team => team.Players[0].Race == opponentRace && playerId != team.Players[0].BattleTag))
                && (m.Season == season));
        }

        return mongoCollection.CountDocumentsAsync(m =>
            Builders<Matchup>.Filter.Text($"\"{playerId}\" \"{opponentId}\"", textSearchOpts).Inject()
            && (gameMode == GameMode.Undefined || m.GameMode == gameMode)
            && (gateWay == GateWay.Undefined || m.GateWay == gateWay)
            && (m.Season == season));
    }

    public async Task<MatchupDetail> LoadDetails(ObjectId id)
    {
        var originalMatch = await LoadFirst<MatchFinishedEvent>(t => t.Id == id);
        var match = await LoadFirst<Matchup>(t => t.Id == id);

        return new MatchupDetail
        {
            Match = match,
            PlayerScores = originalMatch?.result?.players.Select(p => CreateDetail(p)).ToList()
        };
    }

    public async Task<MatchupDetail> LoadDetailsByOngoingMatchId(string id)
    {
        var originalMatch = await LoadFirst<MatchFinishedEvent>(t => t.match.id == id);
        var match = await LoadFirst<Matchup>(t => t.MatchId == id);

        return new MatchupDetail
        {
            Match = match,
            PlayerScores = originalMatch?.result?.players.Select(p => CreateDetail(p)).ToList()
        };
    }

    public async Task<MatchFinishedEvent> LoadDetailsByGameName(string gameName)
    {
        return await LoadFirst<MatchFinishedEvent>(t => t.match.gamename == gameName);
    }

    public Task EnsureIndices()
    {
        var collection = CreateCollection<Matchup>();

        var matchUpLogBuilder = Builders<Matchup>.IndexKeys;

        var textIndex = new CreateIndexModel<Matchup>(
            matchUpLogBuilder
            .Text(x => x.Team1Players)
            .Text(x => x.Team2Players)
            .Text(x => x.Team3Players)
            .Text(x => x.Team4Players)
        );
        return collection.Indexes.CreateOneAsync(textIndex);
    }

    private PlayerScore CreateDetail(PlayerBlizzard playerBlizzard)
    {
        foreach (var player in playerBlizzard.heroes)
        {
            player.icon = player.icon.ParseReforgedName();
        }

        return new PlayerScore(
            playerBlizzard.battleTag,
            playerBlizzard.unitScore,
            playerBlizzard.heroes,
            playerBlizzard.heroScore,
            playerBlizzard.resourceScore,
            playerBlizzard.teamIndex);
    }

    public Task<List<Matchup>> Load(
        int season,
        GameMode gameMode,
        int offset = 0,
        int pageSize = 100)
    {
        var mongoCollection = CreateCollection<Matchup>();
        return mongoCollection
            .Find(m => gameMode == m.GameMode && m.Season == season)
            .SortByDescending(s => s.EndTime)
            .Skip(offset)
            .Limit(pageSize)
            .ToListAsync();
    }

    public async Task<int> GetFloIdFromId(string gameId)
    {
        var gameIdObj = new ObjectId($"{gameId}");
        var match = await LoadFirst<Matchup>(x => x.Id == gameIdObj);
        return (match == null || match.FloMatchId == null) ? 0 : match.FloMatchId.Value;
    }

    public Task<long> Count(
        int season,
        GameMode gameMode)
    {
        return CreateCollection<Matchup>().CountDocumentsAsync(m =>
                gameMode == m.GameMode && m.Season == season);
    }

    public Task InsertOnGoingMatch(OnGoingMatchup matchup)
    {
        _cache.Upsert(matchup);
        return Upsert(matchup, m => m.MatchId == matchup.MatchId);
    }


    public Task<OnGoingMatchup> LoadOnGoingMatchForPlayer(string playerId)
    {
        var mongoCollection = CreateCollection<OnGoingMatchup>();

        return mongoCollection
            .Find(m => m.Team1Players.Contains(playerId) 
                    || m.Team2Players.Contains(playerId)
                    || m.Team3Players.Contains(playerId)
                    || m.Team4Players.Contains(playerId)
            )
            .FirstOrDefaultAsync();
    }

    public Task<OnGoingMatchup> TryLoadOnGoingMatchForPlayer(string playerId)
    {
        return _cache.LoadOnGoingMatchForPlayer(playerId);
    }

    public Task DeleteOnGoingMatch(string matchId)
    {
        _cache.Delete(matchId);
        return Delete<OnGoingMatchup>(x => x.MatchId == matchId);
    }

    public Task<List<OnGoingMatchup>> LoadOnGoingMatches(
        GameMode gameMode = GameMode.Undefined,
        GateWay gateWay = GateWay.Undefined,
        int offset = 0,
        int pageSize = 100,
        string map = "Overall",
        int minMmr = 0,
        int maxMmr = 3000,
        string sort = "startTimeDescending")
    {
        return _cache.LoadOnGoingMatches(gameMode, gateWay, offset, pageSize, map, minMmr, maxMmr, sort);
    }

    public Task<long> CountOnGoingMatches(
        GameMode gameMode = GameMode.Undefined,
        GateWay gateWay = GateWay.Undefined,
        string map = "Overall",
        int minMmr = 0,
        int maxMmr = 3000)
    {
        return _cache.CountOnGoingMatches(gameMode, gateWay, map, minMmr, maxMmr);
    }

    public Task<Season> LoadLastSeason()
    {
        var mongoCollection = CreateCollection<Season>();
        return mongoCollection.AsQueryable().OrderByDescending(c => c.Id).FirstOrDefaultAsync();
    }
}
