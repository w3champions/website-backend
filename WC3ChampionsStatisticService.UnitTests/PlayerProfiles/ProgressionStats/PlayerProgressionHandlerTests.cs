using System.Collections.Generic;
using System.Threading.Tasks;
using Mongo2Go;
using MongoDB.Driver;
using NUnit.Framework;
using W3C.Contracts.GameObjects;
using W3C.Contracts.Matchmaking;
using W3C.Domain.MatchmakingService;
using W3ChampionsStatisticService.PlayerProfiles.ProgressionStats;

namespace WC3ChampionsStatisticService.UnitTests.PlayerProfiles.ProgressionStats;

[TestFixture]
public class PlayerProgressionHandlerTests
{
    private MongoDbRunner _runner;
    private MongoClient _mongoClient;
    private PlayerProgressionRepository _repository;
    private PlayerProgressionHandler _handler;

    [SetUp]
    public void Setup()
    {
        _runner = MongoDbRunner.Start();
        _mongoClient = new MongoClient(_runner.ConnectionString);
        _repository = new PlayerProgressionRepository(_mongoClient);
        _handler = new PlayerProgressionHandler(_repository);
    }

    [TearDown]
    public void TearDown() => _runner.Dispose();

    private static PlayerMMrChange Player(string battleTag, int team, bool won, Race race,
        string atTeamId = null, UpdatedProgression progression = null)
    {
        return new PlayerMMrChange
        {
            battleTag = battleTag,
            team = team,
            won = won,
            race = race,
            atTeamId = atTeamId,
            mmr = new Mmr { rating = 1500 },
            updatedMmr = new Mmr { rating = won ? 1520 : 1480 },
            updatedProgression = progression,
        };
    }

    private static MatchFinishedEvent Event(GameMode gameMode, int season, GateWay gateway, List<PlayerMMrChange> players)
    {
        return new MatchFinishedEvent
        {
            match = new Match
            {
                id = "match-1",
                gameMode = gameMode,
                gateway = gateway,
                season = season,
                players = players,
            },
        };
    }

    private IMongoCollection<PlayerProgression> Collection() =>
        _mongoClient.GetDatabase("W3Champions-Statistic-Service").GetCollection<PlayerProgression>("PlayerProgression");

    private Task<long> Count() => Collection().CountDocumentsAsync(FilterDefinition<PlayerProgression>.Empty);

    [Test]
    public async Task Solo1v1_Placed_WritesOneDocPerPlayer()
    {
        var ev = Event(GameMode.GM_1v1, 2, GateWay.Europe, new List<PlayerMMrChange>
        {
            Player("winner#1", 0, true, Race.HU, progression: new UpdatedProgression { league = 3, division = 2, points = 50, apexPoints = 120 }),
            Player("loser#2", 1, false, Race.OC, progression: new UpdatedProgression { league = 3, division = 3, points = 40 }),
        });

        await _handler.Update(ev);

        Assert.AreEqual(2, await Count());
        var winner = await _repository.LoadProgression("2_winner#1@20_GM_1v1_HU");
        Assert.IsNotNull(winner);
        Assert.AreEqual(3, winner.League);
        Assert.AreEqual(2, winner.Division);
        Assert.AreEqual(50, winner.Points);
        Assert.AreEqual(120, winner.ApexPoints);
        Assert.AreEqual(Race.HU, winner.Race);
    }

    [Test]
    public async Task AtTeam_WritesSingleSharedDoc_NormalizedGameMode_NoRace()
    {
        var teamRank = new UpdatedProgression { league = 4, division = 1, points = 75 };
        var ev = Event(GameMode.GM_2v2, 2, GateWay.Europe, new List<PlayerMMrChange>
        {
            Player("a#1", 0, true, Race.HU, atTeamId: "team-A", progression: teamRank),
            Player("b#2", 0, true, Race.OC, atTeamId: "team-A", progression: teamRank),
            Player("c#3", 1, false, Race.NE, atTeamId: "team-B", progression: new UpdatedProgression { league = 4, division = 2, points = 30 }),
            Player("d#4", 1, false, Race.UD, atTeamId: "team-B", progression: new UpdatedProgression { league = 4, division = 2, points = 30 }),
        });

        await _handler.Update(ev);

        Assert.AreEqual(2, await Count()); // one doc per AT team
        var teamA = await _repository.LoadProgression("2_a#1@20_b#2@20_GM_2v2_AT");
        Assert.IsNotNull(teamA);
        Assert.AreEqual(2, teamA.PlayerIds.Count);
        Assert.AreEqual(4, teamA.League);
        Assert.AreEqual(1, teamA.Division);
        Assert.IsNull(teamA.Race);
    }

    [Test]
    public async Task UnplacedPlayers_Skipped()
    {
        var ev = Event(GameMode.GM_1v1, 2, GateWay.Europe, new List<PlayerMMrChange>
        {
            Player("calibrating#1", 0, true, Race.HU, progression: null),
            Player("calibrating#2", 1, false, Race.OC, progression: null),
        });

        await _handler.Update(ev);

        Assert.AreEqual(0, await Count());
    }

    [Test]
    public async Task Replay_IsIdempotent()
    {
        var ev = Event(GameMode.GM_1v1, 2, GateWay.Europe, new List<PlayerMMrChange>
        {
            Player("winner#1", 0, true, Race.HU, progression: new UpdatedProgression { league = 3, division = 2, points = 50 }),
        });

        await _handler.Update(ev);
        await _handler.Update(ev);

        Assert.AreEqual(1, await Count());
        var doc = await _repository.LoadProgression("2_winner#1@20_GM_1v1_HU");
        Assert.AreEqual(3, doc.League);
        Assert.AreEqual(2, doc.Division);
        Assert.AreEqual(50, doc.Points);
    }

    [Test]
    public async Task AtTeam_Replay_IsIdempotent()
    {
        var teamRank = new UpdatedProgression { league = 4, division = 1, points = 75 };
        var ev = Event(GameMode.GM_2v2, 2, GateWay.Europe, new List<PlayerMMrChange>
        {
            Player("a#1", 0, true, Race.HU, atTeamId: "team-A", progression: teamRank),
            Player("b#2", 0, true, Race.OC, atTeamId: "team-A", progression: teamRank),
        });

        await _handler.Update(ev);
        await _handler.Update(ev);

        Assert.AreEqual(1, await Count());
        var doc = await _repository.LoadProgression("2_a#1@20_b#2@20_GM_2v2_AT");
        Assert.AreEqual(4, doc.League);
        Assert.AreEqual(1, doc.Division);
    }

    [Test]
    public async Task CrossSeason_RetainsBothDocs()
    {
        // season 1 -> BattleTagIdCombined omits the race suffix (race-split only for season >= 2).
        var s1 = Event(GameMode.GM_1v1, 1, GateWay.Europe, new List<PlayerMMrChange>
        {
            Player("p#1", 0, true, Race.HU, progression: new UpdatedProgression { league = 3, division = 2, points = 50 }),
        });
        var s2 = Event(GameMode.GM_1v1, 2, GateWay.Europe, new List<PlayerMMrChange>
        {
            Player("p#1", 0, true, Race.HU, progression: new UpdatedProgression { league = 4, division = 1, points = 10 }),
        });

        await _handler.Update(s1);
        await _handler.Update(s2);

        Assert.AreEqual(2, await Count());
        Assert.AreEqual(3, (await _repository.LoadProgression("1_p#1@20_GM_1v1")).League);
        Assert.AreEqual(4, (await _repository.LoadProgression("2_p#1@20_GM_1v1_HU")).League);
    }

    [Test]
    public async Task TwoAtTeamsOnSameGameTeam_WriteSeparateDocs()
    {
        // A 4v4-AT side (team 0) containing two distinct arranged teams of 2.
        var teamX = new UpdatedProgression { league = 3, division = 1, points = 60 };
        var teamY = new UpdatedProgression { league = 5, division = 4, points = 20 };
        var ev = Event(GameMode.GM_4v4, 2, GateWay.Europe, new List<PlayerMMrChange>
        {
            Player("x1#1", 0, true, Race.HU, atTeamId: "team-X", progression: teamX),
            Player("x2#2", 0, true, Race.OC, atTeamId: "team-X", progression: teamX),
            Player("y1#3", 0, true, Race.NE, atTeamId: "team-Y", progression: teamY),
            Player("y2#4", 0, true, Race.UD, atTeamId: "team-Y", progression: teamY),
        });

        await _handler.Update(ev);

        Assert.AreEqual(2, await Count());
        var x = await _repository.LoadProgression("2_x1#1@20_x2#2@20_GM_4v4_AT");
        var y = await _repository.LoadProgression("2_y1#3@20_y2#4@20_GM_4v4_AT");
        Assert.IsNotNull(x);
        Assert.IsNotNull(y);
        Assert.AreEqual(3, x.League);
        Assert.AreEqual(5, y.League);
    }
}
