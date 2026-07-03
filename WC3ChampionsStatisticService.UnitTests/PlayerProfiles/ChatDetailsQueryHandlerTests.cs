using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using W3C.Contracts.Matchmaking;
using W3ChampionsStatisticService.Ladder;
using W3ChampionsStatisticService.PlayerProfiles.ChatDetails;
using W3ChampionsStatisticService.PlayerProfiles.GameModeStats;
using W3ChampionsStatisticService.Ports;

namespace WC3ChampionsStatisticService.UnitTests.PlayerProfiles;

[TestFixture]
public class ChatDetailsQueryHandlerTests
{
    private const string BattleTag = "peter#123";

    private Mock<IMatchRepository> _matchRepo;
    private Mock<IRankRepository> _rankRepo;
    private Mock<IPlayerRepository> _playerRepo;

    [SetUp]
    public void SetUp()
    {
        _matchRepo = new Mock<IMatchRepository>();
        _rankRepo = new Mock<IRankRepository>();
        _playerRepo = new Mock<IPlayerRepository>();
    }

    private ChatDetailsQueryHandler CreateHandler(
        int? season = 5,
        List<PlayerGameModeStatPerGateway> stats = null,
        List<Rank> ranks = null,
        List<LeagueConstellation> constellations = null)
    {
        _matchRepo.Setup(m => m.LoadLastSeason())
            .ReturnsAsync(season.HasValue ? new Season(season.Value) : null);
        if (season.HasValue)
        {
            _playerRepo.Setup(p => p.LoadGameModeStatPerGateway(BattleTag, season.Value))
                .ReturnsAsync(stats ?? new List<PlayerGameModeStatPerGateway>());
            _rankRepo.Setup(r => r.LoadRanksForPlayers(
                    It.Is<List<string>>(l => l.Count == 1 && l[0] == BattleTag), season.Value))
                .ReturnsAsync(ranks ?? new List<Rank>());
            _rankRepo.Setup(r => r.LoadLeagueConstellation(season.Value))
                .ReturnsAsync(constellations ?? new List<LeagueConstellation>());
        }
        return new ChatDetailsQueryHandler(_matchRepo.Object, _rankRepo.Object, _playerRepo.Object);
    }

    private static Rank CreateRank(int league, int rankNumber, GateWay gateway, GameMode gameMode, int season = 5)
    {
        return new Rank(new List<string> { BattleTag }, league, rankNumber, 100, null, gateway, gameMode, season);
    }

    private static LeagueConstellation Constellation(GateWay gateway, GameMode gameMode, params League[] leagues)
    {
        return new LeagueConstellation(5, gateway, gameMode, new List<League>(leagues));
    }

    [Test]
    public async Task NoSeasonsInDb_ReturnsEmptyEnrichment()
    {
        var handler = CreateHandler(season: null);

        var enrichment = await handler.LoadEnrichment(BattleTag);

        Assert.That(enrichment.Rank, Is.Null);
        Assert.That(enrichment.GamesPlayed, Is.EqualTo(0));
        Assert.That(enrichment.Season, Is.Null);
    }

    [Test]
    public async Task UnrankedPlayerWithGames_SumsGamesAcrossDocs_RankIsNull()
    {
        var handler = CreateHandler(stats: new List<PlayerGameModeStatPerGateway>
        {
            new PlayerGameModeStatPerGateway { Wins = 2, Losses = 1 },
            new PlayerGameModeStatPerGateway { Wins = 0, Losses = 1 },
        });

        var enrichment = await handler.LoadEnrichment(BattleTag);

        Assert.That(enrichment.Rank, Is.Null);
        Assert.That(enrichment.GamesPlayed, Is.EqualTo(4));
        Assert.That(enrichment.Season, Is.EqualTo(5));
    }

    [Test]
    public async Task PicksBestRank_ByLowestLeagueOrder_AcrossGatewaysAndModes()
    {
        var handler = CreateHandler(
            ranks: new List<Rank>
            {
                CreateRank(league: 2, rankNumber: 5, GateWay.Europe, GameMode.GM_1v1),
                CreateRank(league: 1, rankNumber: 30, GateWay.America, GameMode.GM_2v2_AT),
            },
            constellations: new List<LeagueConstellation>
            {
                Constellation(GateWay.Europe, GameMode.GM_1v1, new League(2, 2, "Gold", 1)),
                Constellation(GateWay.America, GameMode.GM_2v2_AT, new League(1, 1, "Platinum", 0)),
            });

        var enrichment = await handler.LoadEnrichment(BattleTag);

        Assert.That(enrichment.Rank, Is.Not.Null);
        Assert.That(enrichment.Rank.LeagueId, Is.EqualTo(1));
        Assert.That(enrichment.Rank.LeagueName, Is.EqualTo("Platinum"));
        Assert.That(enrichment.Rank.LeagueOrder, Is.EqualTo(1));
        Assert.That(enrichment.Rank.LeagueDivision, Is.EqualTo(0));
        Assert.That(enrichment.Rank.RankNumber, Is.EqualTo(30));
        Assert.That(enrichment.Rank.GameMode, Is.EqualTo(GameMode.GM_2v2_AT));
        Assert.That(enrichment.Rank.GateWay, Is.EqualTo(GateWay.America));
    }

    [Test]
    public async Task TieOnLeagueOrder_BrokenByRankNumber_ThenByGameMode()
    {
        var handler = CreateHandler(
            ranks: new List<Rank>
            {
                CreateRank(league: 1, rankNumber: 7, GateWay.Europe, GameMode.GM_2v2),
                CreateRank(league: 1, rankNumber: 3, GateWay.Europe, GameMode.GM_4v4),
                CreateRank(league: 1, rankNumber: 3, GateWay.Europe, GameMode.GM_1v1),
            },
            constellations: new List<LeagueConstellation>
            {
                Constellation(GateWay.Europe, GameMode.GM_2v2, new League(1, 1, "Platinum", 0)),
                Constellation(GateWay.Europe, GameMode.GM_4v4, new League(1, 1, "Platinum", 0)),
                Constellation(GateWay.Europe, GameMode.GM_1v1, new League(1, 1, "Platinum", 0)),
            });

        var enrichment = await handler.LoadEnrichment(BattleTag);

        // rankNumber 3 beats 7; GameMode.GM_1v1 (1) beats GM_4v4 (4) on the remaining tie
        Assert.That(enrichment.Rank.RankNumber, Is.EqualTo(3));
        Assert.That(enrichment.Rank.GameMode, Is.EqualTo(GameMode.GM_1v1));
    }

    [Test]
    public async Task SkipsRankNumberZero_AndRanksWithUnresolvableLeague()
    {
        var handler = CreateHandler(
            ranks: new List<Rank>
            {
                CreateRank(league: 9, rankNumber: 0, GateWay.Europe, GameMode.GM_1v1), // placement artifact — skip
                CreateRank(league: 7, rankNumber: 4, GateWay.Europe, GameMode.GM_1v1), // league 7 missing — skip
                CreateRank(league: 3, rankNumber: 8, GateWay.Europe, GameMode.GM_1v1),
            },
            constellations: new List<LeagueConstellation>
            {
                Constellation(GateWay.Europe, GameMode.GM_1v1,
                    new League(9, 0, "GrandMaster", 0),
                    new League(3, 3, "Diamond", 1)),
            });

        var enrichment = await handler.LoadEnrichment(BattleTag);

        Assert.That(enrichment.Rank.LeagueId, Is.EqualTo(3));
        Assert.That(enrichment.Rank.LeagueName, Is.EqualTo("Diamond"));
        Assert.That(enrichment.Rank.RankNumber, Is.EqualTo(8));
    }

    [Test]
    public void RepositoryThrows_ReturnsEmptyEnrichment_DoesNotPropagate()
    {
        var handler = CreateHandler();
        _rankRepo.Setup(r => r.LoadRanksForPlayers(
                It.Is<List<string>>(l => l.Count == 1 && l[0] == BattleTag), 5))
            .ThrowsAsync(new Exception("boom"));

        ChatDetailsEnrichment enrichment = null;
        Assert.DoesNotThrowAsync(async () => enrichment = await handler.LoadEnrichment(BattleTag));

        Assert.That(enrichment, Is.Not.Null);
        Assert.That(enrichment.Rank, Is.Null);
        Assert.That(enrichment.GamesPlayed, Is.EqualTo(0));
    }
}
