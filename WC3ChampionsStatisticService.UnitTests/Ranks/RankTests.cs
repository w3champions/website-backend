using MongoDB.Driver;
using Moq;
using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using W3C.Contracts.GameObjects;
using W3C.Domain.CommonValueObjects;
using W3C.Domain.Repositories;
using W3ChampionsStatisticService.Clans;
using W3ChampionsStatisticService.Ladder;
using W3ChampionsStatisticService.PersonalSettings;
using W3ChampionsStatisticService.PlayerProfiles;
using W3ChampionsStatisticService.Ports;
using W3C.Contracts.Matchmaking;

namespace WC3ChampionsStatisticService.Tests.Ranks;

[TestFixture]
public class RankTests : IntegrationTestBase
{
    [Test]
    public async Task OnlyOneRankIsSyncedBecausePreviousWasSynced()
    {
        var matchEventRepository = new MatchEventRepository(MongoClient);
        var rankRepository = new Mock<IRankRepository>();
        var rankHandler = new RankSyncHandler(rankRepository.Object, matchEventRepository);

        await InsertRankChangedEvent(TestDtoHelper.CreateRankChangedEvent("peter#123"));

        await rankHandler.Update();

        rankRepository.Verify(r => r.InsertRanks(It.Is<List<Rank>>(rl => rl.Count == 1)));

        await InsertRankChangedEvent(TestDtoHelper.CreateRankChangedEvent("wolf#456"));

        await rankHandler.Update();

        rankRepository.Verify(r => r.InsertRanks(It.Is<List<Rank>>(rl => rl.Count == 1)));
    }

    [Test]
    public async Task EmptyRanksDoesNotThrwoBulkWriteException()
    {
        var matchEventRepository = new MatchEventRepository(MongoClient);
        var rankHandler = new RankSyncHandler(new RankRepository(MongoClient, personalSettingsProvider), matchEventRepository);

        await InsertRankChangedEvent(TestDtoHelper.CreateRankChangedEvent("peter#123"));

        await rankHandler.Update();
        await rankHandler.Update();
    }

    [Test]
    public async Task LoadAndSave()
    {
        var rankRepository = new RankRepository(MongoClient, personalSettingsProvider);
        var playerRepository = new PlayerRepository(MongoClient);

        var ranks = new List<Rank> { new(new List<string> { "peter#123" }, 1, 12, 12.5, null, GateWay.America,
        GameMode.GM_1v1, 0)};
        await rankRepository.InsertRanks(ranks);
        var player = PlayerOverview.Create(new List<PlayerId> { PlayerId.Create("peter#123") }, GateWay.America, GameMode.GM_1v1, 0, null);
        player.RecordWin(true, 1234);
        await playerRepository.UpsertPlayerOverview(player);
        await playerRepository.UpsertPlayerOverview(player);
        await playerRepository.UpsertPlayerOverview(player);
        var playerLoaded = await rankRepository.LoadPlayersOfLeague(1, 0, GateWay.America, GameMode.GM_1v1);

        Assert.AreEqual(1, playerLoaded.Count);
        Assert.AreEqual("0_peter#123@10_GM_1v1", playerLoaded[0].Players.First().Id);
        Assert.AreEqual(1, playerLoaded[0].Players.First().Wins);
        Assert.AreEqual(12, playerLoaded[0].RankNumber);
        Assert.AreEqual(12.5, playerLoaded[0].RankingPoints);
        Assert.AreEqual(0, playerLoaded[0].Players.First().Losses);
    }

    [Test]
    public async Task LoadAndSave_NotFound()
    {
        var rankRepository = new RankRepository(MongoClient, personalSettingsProvider);
        var playerRepository = new PlayerRepository(MongoClient);

        var ranks = new List<Rank> { new(new List<string> { "peter#123" }, 1, 12, 4.2, null, GateWay.Europe, GameMode.GM_1v1, 0) };
        await rankRepository.InsertRanks(ranks);
        var player = PlayerOverview.Create(new List<PlayerId> { PlayerId.Create("peter#123") }, GateWay.Europe, GameMode.GM_1v1, 0, null);
        await playerRepository.UpsertPlayerOverview(player);
        var playerLoaded = await rankRepository.LoadPlayersOfLeague(1, 0, GateWay.America, GameMode.GM_1v1);

        Assert.IsEmpty(playerLoaded);
    }

    [Test]
    public async Task LoadAndSave_NotDuplicatingWhenGoingUp()
    {
        var rankRepository = new RankRepository(MongoClient, personalSettingsProvider);
        var playerRepository = new PlayerRepository(MongoClient);

        var ranks1 = new List<Rank> { new(new List<string> { "peter#123" }, 1, 12, 6.1, null, GateWay.Europe, GameMode.GM_1v1, 0) };
        var ranks2 = new List<Rank> { new(new List<string> { "peter#123" }, 1, 8, 6.1, null, GateWay.Europe, GameMode.GM_1v1, 0) };
        await rankRepository.InsertRanks(ranks1);
        await rankRepository.InsertRanks(ranks2);
        var player = PlayerOverview.Create(new List<PlayerId> { PlayerId.Create("peter#123") }, GateWay.Europe, GameMode.GM_1v1, 0, null);
        await playerRepository.UpsertPlayerOverview(player);
        var playerLoaded = await rankRepository.LoadPlayersOfLeague(1, 0, GateWay.Europe, GameMode.GM_1v1);

        Assert.AreEqual(1, playerLoaded.Count);
        Assert.AreEqual(8, playerLoaded[0].RankNumber);
    }

    [Test]
    public async Task LoadPlayersOfLeague_RaceBasedMMR()
    {
        var rankRepository = new RankRepository(MongoClient, personalSettingsProvider);
        var playerRepository = new PlayerRepository(MongoClient);

        var ranks = new List<Rank>
        {
            new(new List<string> { "peter#123" }, 1, 12, 1456, Race.HU, GateWay.Europe, GameMode.GM_1v1, 2),
            new(new List<string> { "peter#123" }, 1, 8, 1456, Race.NE, GateWay.Europe, GameMode.GM_1v1, 2)
        };
        await rankRepository.InsertRanks(ranks);
        var player1 = PlayerOverview.Create(new List<PlayerId> { PlayerId.Create("peter#123") }, GateWay.Europe, GameMode.GM_1v1, 2, Race.HU);
        await playerRepository.UpsertPlayerOverview(player1);
        var player2 = PlayerOverview.Create(new List<PlayerId> { PlayerId.Create("peter#123") }, GateWay.Europe, GameMode.GM_1v1, 2, Race.NE);
        await playerRepository.UpsertPlayerOverview(player2);

        var playerLoaded = await rankRepository.LoadPlayersOfLeague(1, 2, GateWay.Europe, GameMode.GM_1v1);

        Assert.AreEqual(2, playerLoaded.Count);
    }

    [Test]
    public async Task RankIntegrationWithMultipleIds()
    {
        var matchEventRepository = new MatchEventRepository(MongoClient);
        var rankRepository = new RankRepository(MongoClient, personalSettingsProvider);
        var playerRepository = new PlayerRepository(MongoClient);

        var matchFinishedEvent = TestDtoHelper.CreateFakeEvent();
        var rankingChangedEvent = TestDtoHelper.CreateRankChangedEvent();

        matchFinishedEvent.match.players[0].battleTag = "peTer#123";
        matchFinishedEvent.match.gameMode = GameMode.GM_1v1;
        matchFinishedEvent.match.gateway = GateWay.America;

        rankingChangedEvent.ranks[0].battleTags = new List<string> { "peTer#123" };
        rankingChangedEvent.gateway = GateWay.America;
        rankingChangedEvent.gameMode = GameMode.GM_1v1;

        await InsertRankChangedEvent(rankingChangedEvent);
        await matchEventRepository.InsertIfNotExisting(matchFinishedEvent);

        var playOverviewHandler = new PlayOverviewHandler(playerRepository);
        await playOverviewHandler.Update(matchFinishedEvent);

        var rankHandler = new RankSyncHandler(rankRepository, matchEventRepository);

        await playOverviewHandler.Update(matchFinishedEvent);
        await rankHandler.Update();

        var rank = await rankRepository.SearchPlayerOfLeague("peT", 0, GateWay.America, GameMode.GM_1v1);

        Assert.AreEqual(1, rank.Count);
    }

    [Test]
    public async Task RaceBasedMMRUpdate()
    {
        var matchEventRepository = new MatchEventRepository(MongoClient);
        var rankRepository = new RankRepository(MongoClient, personalSettingsProvider);
        var playerRepository = new PlayerRepository(MongoClient);

        var matchFinishedEvent = TestDtoHelper.CreateFakeEvent();
        var rankingChangedEvent = TestDtoHelper.CreateRankChangedEvent();

        matchFinishedEvent.match.players[0].battleTag = "peTer#123";
        matchFinishedEvent.match.players[0].race = Race.NE;
        matchFinishedEvent.match.gameMode = GameMode.GM_1v1;
        matchFinishedEvent.match.season = 2;
        matchFinishedEvent.match.gateway = GateWay.America;

        rankingChangedEvent.ranks[0].battleTags = new List<string> { "peTer#123" };
        rankingChangedEvent.ranks[0].race = Race.NE;
        rankingChangedEvent.gateway = GateWay.America;
        rankingChangedEvent.gameMode = GameMode.GM_1v1;
        rankingChangedEvent.season = 2;

        await InsertRankChangedEvent(rankingChangedEvent);
        await matchEventRepository.InsertIfNotExisting(matchFinishedEvent);

        var playOverviewHandler = new PlayOverviewHandler(playerRepository);
        await playOverviewHandler.Update(matchFinishedEvent);

        var rankHandler = new RankSyncHandler(rankRepository, matchEventRepository);

        await playOverviewHandler.Update(matchFinishedEvent);
        await rankHandler.Update();

        var rank = await rankRepository.SearchPlayerOfLeague("peT", 2, GateWay.America, GameMode.GM_1v1);

        Assert.AreEqual(1, rank.Count);
        Assert.AreEqual(Race.NE, rank[0].Race);
    }

    [Test]
    public async Task RaceBasedMMRUpdate_DifferentSeason()
    {
        var matchEventRepository = new MatchEventRepository(MongoClient);
        var rankRepository = new RankRepository(MongoClient, personalSettingsProvider);
        var playerRepository = new PlayerRepository(MongoClient);

        var matchFinishedEvent = TestDtoHelper.CreateFakeEvent();
        var rankingChangedEvent = TestDtoHelper.CreateRankChangedEvent();

        matchFinishedEvent.match.players[0].battleTag = "peTer#123";
        matchFinishedEvent.match.gameMode = GameMode.GM_1v1;
        matchFinishedEvent.match.season = 1;
        matchFinishedEvent.match.gateway = GateWay.America;

        rankingChangedEvent.ranks[0].battleTags = new List<string> { "peTer#123" };
        rankingChangedEvent.ranks[0].race = Race.NE;
        rankingChangedEvent.gateway = GateWay.America;
        rankingChangedEvent.gameMode = GameMode.GM_1v1;

        await InsertRankChangedEvent(rankingChangedEvent);
        await matchEventRepository.InsertIfNotExisting(matchFinishedEvent);

        var playOverviewHandler = new PlayOverviewHandler(playerRepository);
        await playOverviewHandler.Update(matchFinishedEvent);

        var rankHandler = new RankSyncHandler(rankRepository, matchEventRepository);

        await playOverviewHandler.Update(matchFinishedEvent);
        await rankHandler.Update();

        var rank = await rankRepository.SearchPlayerOfLeague("peT", 2, GateWay.America, GameMode.GM_1v1);

        Assert.AreEqual(0, rank.Count);
    }


    [Test]
    public async Task ReturnRanks_WhenPlayersHavePersonalSettingsConfigured_MustHaveCorrectPersonalSettings()
    {
        // Arrange
        var rankRepository = new RankRepository(MongoClient, personalSettingsProvider);
        var playerRepository = new PlayerRepository(MongoClient);
        var personalSettingsRepository = new PersonalSettingsRepository(MongoClient);
        var clanRepository = new ClanRepository(MongoClient);
        var queryHandler = new RankQueryHandler(rankRepository, playerRepository, clanRepository);

        var ranks = new List<Rank> { new(new List<string> { "peter#123" }, 1, 12, 1456, null, GateWay.America, GameMode.GM_1v1, 1) };
        await rankRepository.InsertRanks(ranks);

        var player = PlayerOverview.Create(new List<PlayerId> { PlayerId.Create("peter#123") }, GateWay.America, GameMode.GM_1v1, 1, null);
        player.RecordWin(true, 1234);
        await playerRepository.UpsertPlayerOverview(player);

        var playerStats = new PlayerOverallStats()
        {
            BattleTag = "peter#123",
        };
        await playerRepository.UpsertPlayer(playerStats);

        var settings = new PersonalSetting("peter#123")
        {
            ProfilePicture = new ProfilePicture() { Race = AvatarCategory.HU, PictureId = 5 },
            Country = "BG"
        };
        await personalSettingsRepository.Save(settings);

        // Act
        var playerLoaded = await queryHandler.LoadPlayersOfLeague(1, 1, GateWay.America, GameMode.GM_1v1);

        // Assert
        Assert.AreEqual(1, playerLoaded.Count);

        var playerRank = playerLoaded[0];
        Assert.AreEqual("1_peter#123@10_GM_1v1", playerRank.Players.First().Id);
        Assert.AreEqual(playerRank.PlayersInfo[0].SelectedRace, AvatarCategory.HU);
        Assert.AreEqual(playerRank.PlayersInfo[0].PictureId, 5);
        Assert.AreEqual(playerRank.PlayersInfo[0].Country, "BG");
    }

    [Test]
    public async Task ReturnRanks_WhenPlayersDoNotHavePersonalSettingsConfigured_MustHaveNotThrowError()
    {
        // Arrange
        var rankRepository = new RankRepository(MongoClient, personalSettingsProvider);
        var playerRepository = new PlayerRepository(MongoClient);
        var clanRepository = new ClanRepository(MongoClient);
        var queryHandler = new RankQueryHandler(rankRepository, playerRepository, clanRepository);

        var ranks = new List<Rank> { new(new List<string> { "peter#123" }, 1, 12, 1456, null, GateWay.America, GameMode.GM_1v1, 1) };
        await rankRepository.InsertRanks(ranks);

        var player = PlayerOverview.Create(new List<PlayerId> { PlayerId.Create("peter#123") }, GateWay.America, GameMode.GM_1v1, 1, null);
        player.RecordWin(true, 1234);
        await playerRepository.UpsertPlayerOverview(player);

        var playerStats = new PlayerOverallStats()
        {
            BattleTag = "peter#123",
        };
        await playerRepository.UpsertPlayer(playerStats);

        // Act
        var playerLoaded = await queryHandler.LoadPlayersOfLeague(1, 1, GateWay.America, GameMode.GM_1v1);

        // Assert
        Assert.AreEqual(1, playerLoaded.Count);

        var playerRank = playerLoaded[0];
        Assert.AreEqual("1_peter#123@10_GM_1v1", playerRank.Players.First().Id);
    }

    [Test]
    public async Task ReturnRanks_ClanGetsResolved()
    {
        // Arrange
        var rankRepository = new RankRepository(MongoClient, personalSettingsProvider);
        var playerRepository = new PlayerRepository(MongoClient);
        var clanRepository = new ClanRepository(MongoClient);
        var queryHandler = new RankQueryHandler(rankRepository, playerRepository, clanRepository);

        var ranks = new List<Rank> { new(new List<string> { "peter#123" }, 1, 12, 1456, null, GateWay.America, GameMode.GM_1v1, 1) };
        await rankRepository.InsertRanks(ranks);

        var player = PlayerOverview.Create(new List<PlayerId> { PlayerId.Create("peter#123") }, GateWay.America, GameMode.GM_1v1, 1, null);
        player.RecordWin(true, 1234);
        await playerRepository.UpsertPlayerOverview(player);

        var playerStats = new PlayerOverallStats()
        {
            BattleTag = "peter#123",
        };
        await playerRepository.UpsertPlayer(playerStats);
        await clanRepository.UpsertMemberShip(new ClanMembership { BattleTag = "peter#123", ClanId = "W3C" });

        // Act
        var playerLoaded = await queryHandler.LoadPlayersOfLeague(1, 1, GateWay.America, GameMode.GM_1v1);

        // Assert
        Assert.AreEqual(1, playerLoaded.Count);

        var playerRank = playerLoaded[0];
        Assert.AreEqual("W3C", playerRank.PlayersInfo.Single().ClanId);
    }

    [Test]
    public async Task ReturnRanks_WithRaceSpecificRank()
    {
        // Arrange
        var rankRepository = new RankRepository(MongoClient, personalSettingsProvider);
        var playerRepository = new PlayerRepository(MongoClient);
        var queryHandler = new RankQueryHandler(rankRepository, playerRepository, new ClanRepository(MongoClient));

        var ranks = new List<Rank>
        {
            new(new List<string> { "peter#123" }, 1, 2, 1000, Race.HU, GateWay.America, GameMode.GM_1v1, 2),
            new(new List<string> { "peter#123" }, 1, 3, 2000, Race.NE, GateWay.America, GameMode.GM_1v1, 2)
        };
        await rankRepository.InsertRanks(ranks);

        var player1 = PlayerOverview.Create(new List<PlayerId> { PlayerId.Create("peter#123") }, GateWay.America, GameMode.GM_1v1, 2, Race.HU);
        player1.RecordWin(true, 1234);
        await playerRepository.UpsertPlayerOverview(player1);
        var player2 = PlayerOverview.Create(new List<PlayerId> { PlayerId.Create("peter#123") }, GateWay.America, GameMode.GM_1v1, 2, Race.NE);
        player2.RecordWin(true, 1234);
        await playerRepository.UpsertPlayerOverview(player2);

        // Act
        var playerLoaded = await queryHandler.LoadPlayersOfLeague(1, 2, GateWay.America, GameMode.GM_1v1);

        // Assert
        Assert.AreEqual(2, playerLoaded.Count);

        Assert.AreEqual("peter#123", playerLoaded[0].Player.PlayerIds.Single().BattleTag);
        Assert.AreEqual("peter#123", playerLoaded[1].Player.PlayerIds.Single().BattleTag);
    }

    [Test]
    public async Task SearchRanks_WithRaceSpecificRank()
    {
        // Arrange
        var rankRepository = new RankRepository(MongoClient, personalSettingsProvider);
        var playerRepository = new PlayerRepository(MongoClient);

        var ranks = new List<Rank>
        {
            new(new List<string> { "peter#123" }, 1, 2, 1000, Race.HU, GateWay.America, GameMode.GM_1v1, 2),
            new(new List<string> { "peter#123" }, 1, 3, 2000, Race.NE, GateWay.America, GameMode.GM_1v1, 2)
        };
        await rankRepository.InsertRanks(ranks);

        var player1 = PlayerOverview.Create(new List<PlayerId> { PlayerId.Create("peter#123") }, GateWay.America, GameMode.GM_1v1, 2, Race.HU);
        player1.RecordWin(true, 1234);
        await playerRepository.UpsertPlayerOverview(player1);
        var player2 = PlayerOverview.Create(new List<PlayerId> { PlayerId.Create("peter#123") }, GateWay.America, GameMode.GM_1v1, 2, Race.NE);
        player2.RecordWin(true, 1234);
        await playerRepository.UpsertPlayerOverview(player2);

        // Act
        var playerLoaded = await rankRepository.SearchPlayerOfLeague("ete", 2, GateWay.America, GameMode.GM_1v1);

        // Assert
        Assert.AreEqual(2, playerLoaded.Count);

        Assert.AreEqual("peter#123", playerLoaded[0].Player.PlayerIds.Single().BattleTag);
        Assert.AreEqual("peter#123", playerLoaded[1].Player.PlayerIds.Single().BattleTag);
    }

    [Test]
    public async Task SearchRanks_WithRaceSpecificRank_DifferentSeasons()
    {
        // Arrange
        var rankRepository = new RankRepository(MongoClient, personalSettingsProvider);
        var playerRepository = new PlayerRepository(MongoClient);

        var ranks = new List<Rank>
        {
            //old one
            new(new List<string> { "peter#123" }, 1, 2, 1000, null, GateWay.America, GameMode.GM_1v1, 1),

            //mmr based
            new(new List<string> { "peter#123" }, 1, 2, 1000, Race.HU, GateWay.America, GameMode.GM_1v1, 2),
            new(new List<string> { "peter#123" }, 1, 3, 2000, Race.NE, GateWay.America, GameMode.GM_1v1, 2)
        };
        await rankRepository.InsertRanks(ranks);

        var player1 = PlayerOverview.Create(new List<PlayerId> { PlayerId.Create("peter#123") }, GateWay.America, GameMode.GM_1v1, 1, null);
        player1.RecordWin(true, 1234);
        await playerRepository.UpsertPlayerOverview(player1);

        var player2 = PlayerOverview.Create(new List<PlayerId> { PlayerId.Create("peter#123") }, GateWay.America, GameMode.GM_1v1, 2, Race.HU);
        player2.RecordWin(true, 1234);
        await playerRepository.UpsertPlayerOverview(player2);
        var player3 = PlayerOverview.Create(new List<PlayerId> { PlayerId.Create("peter#123") }, GateWay.America, GameMode.GM_1v1, 2, Race.NE);
        player3.RecordWin(true, 1234);
        await playerRepository.UpsertPlayerOverview(player3);

        // Act
        var playerLoaded = await rankRepository.SearchPlayerOfLeague("ete", 1, GateWay.America, GameMode.GM_1v1);

        // Assert
        Assert.AreEqual(1, playerLoaded.Count);

        // Act
        var playerLoaded2 = await rankRepository.SearchPlayerOfLeague("ete", 2, GateWay.America, GameMode.GM_1v1);

        // Assert
        Assert.AreEqual(2, playerLoaded2.Count);
    }

    [Test]
    public async Task LoadLadderStandings_ReturnsPositions_MatchingEitherTeamMember()
    {
        var rankRepository = new RankRepository(MongoClient, personalSettingsProvider);
        var playerRepository = new PlayerRepository(MongoClient);

        var soloRank = new Rank(new List<string> { "solo#123" }, 1, 5, 100, null, GateWay.Europe, GameMode.GM_1v1, 13);
        var teamRank = new Rank(new List<string> { "first#456", "second#789" }, 2, 7, 90, null, GateWay.Europe, GameMode.GM_2v2_AT, 13);
        await rankRepository.InsertRanks(new List<Rank> { soloRank, teamRank });

        var solo = PlayerOverview.Create(new List<PlayerId> { PlayerId.Create("solo#123") }, GateWay.Europe, GameMode.GM_1v1, 13, null);
        var team = PlayerOverview.Create(new List<PlayerId> { PlayerId.Create("first#456"), PlayerId.Create("second#789") }, GateWay.Europe, GameMode.GM_2v2_AT, 13, null);
        await playerRepository.UpsertPlayerOverview(solo);
        await playerRepository.UpsertPlayerOverview(team);

        // Act
        var soloStandings = await rankRepository.LoadLadderStandings(new List<string> { "solo#123" }, 13, GateWay.Europe, GameMode.GM_1v1);
        // second#789 is the team's second member — a standing must be found through any member
        var teamStandings = await rankRepository.LoadLadderStandings(new List<string> { "second#789" }, 13, GateWay.Europe, GameMode.GM_2v2_AT);

        // Assert
        Assert.AreEqual(1, soloStandings.Count);
        Assert.AreEqual(100, soloStandings[0].RankingPoints);
        CollectionAssert.AreEqual(new[] { "solo#123" }, soloStandings[0].MemberIds);

        Assert.AreEqual(1, teamStandings.Count);
        Assert.AreEqual(90, teamStandings[0].RankingPoints);
        CollectionAssert.AreEqual(new[] { "first#456", "second#789" }, teamStandings[0].MemberIds);
    }

    [Test]
    public async Task LoadLadderStandings_ScopesToTheLadderAskedFor()
    {
        var rankRepository = new RankRepository(MongoClient, personalSettingsProvider);
        var playerRepository = new PlayerRepository(MongoClient);

        // The same player ranked on three more ladders, each differing in exactly one dimension;
        // distinct ranking points prove which ladder the standing came from.
        var asked = new Rank(new List<string> { "peter#123" }, 1, 5, 100, null, GateWay.Europe, GameMode.GM_1v1, 13);
        var otherSeason = new Rank(new List<string> { "peter#123" }, 2, 9, 90, null, GateWay.Europe, GameMode.GM_1v1, 12);
        var otherGateway = new Rank(new List<string> { "peter#123" }, 3, 9, 80, null, GateWay.America, GameMode.GM_1v1, 13);
        var otherMode = new Rank(new List<string> { "peter#123" }, 4, 9, 70, null, GateWay.Europe, GameMode.GM_2v2_AT, 13);
        await rankRepository.InsertRanks(new List<Rank> { asked, otherSeason, otherGateway, otherMode });

        await playerRepository.UpsertPlayerOverview(PlayerOverview.Create(new List<PlayerId> { PlayerId.Create("peter#123") }, GateWay.Europe, GameMode.GM_1v1, 13, null));
        await playerRepository.UpsertPlayerOverview(PlayerOverview.Create(new List<PlayerId> { PlayerId.Create("peter#123") }, GateWay.Europe, GameMode.GM_1v1, 12, null));
        await playerRepository.UpsertPlayerOverview(PlayerOverview.Create(new List<PlayerId> { PlayerId.Create("peter#123") }, GateWay.America, GameMode.GM_1v1, 13, null));
        await playerRepository.UpsertPlayerOverview(PlayerOverview.Create(new List<PlayerId> { PlayerId.Create("peter#123") }, GateWay.Europe, GameMode.GM_2v2_AT, 13, null));

        // Act
        var standings = await rankRepository.LoadLadderStandings(new List<string> { "peter#123" }, 13, GateWay.Europe, GameMode.GM_1v1);

        // Assert
        Assert.AreEqual(1, standings.Count);
        Assert.AreEqual(100, standings[0].RankingPoints);
    }

    [Test]
    public async Task LoadLadderStandings_DropsRanksWithoutPlayerOverview()
    {
        var rankRepository = new RankRepository(MongoClient, personalSettingsProvider);
        var playerRepository = new PlayerRepository(MongoClient);

        var joined = new Rank(new List<string> { "kept#123" }, 1, 5, 100, null, GateWay.Europe, GameMode.GM_1v1, 13);
        var orphan = new Rank(new List<string> { "orphan#456" }, 1, 6, 90, null, GateWay.Europe, GameMode.GM_1v1, 13);
        await rankRepository.InsertRanks(new List<Rank> { joined, orphan });
        // Only kept#123 gets a PlayerOverview — the orphan rank must be dropped, keeping this
        // method in agreement with LoadRanksForPlayers about who counts as ranked
        await playerRepository.UpsertPlayerOverview(PlayerOverview.Create(new List<PlayerId> { PlayerId.Create("kept#123") }, GateWay.Europe, GameMode.GM_1v1, 13, null));

        // Act
        var standings = await rankRepository.LoadLadderStandings(new List<string> { "kept#123", "orphan#456" }, 13, GateWay.Europe, GameMode.GM_1v1);

        // Assert
        Assert.AreEqual(1, standings.Count);
        CollectionAssert.AreEqual(new[] { "kept#123" }, standings[0].MemberIds);
    }

    [Test]
    public async Task LoadRanksForPlayers_WithContext_DropsRanksWithoutPlayerOverview()
    {
        var rankRepository = new RankRepository(MongoClient, personalSettingsProvider);
        var playerRepository = new PlayerRepository(MongoClient);

        var joined = new Rank(new List<string> { "kept#123" }, 1, 5, 100, null, GateWay.Europe, GameMode.GM_1v1, 13);
        var orphan = new Rank(new List<string> { "orphan#456" }, 1, 6, 90, null, GateWay.Europe, GameMode.GM_1v1, 13);
        await rankRepository.InsertRanks(new List<Rank> { joined, orphan });
        // Only kept#123 gets a PlayerOverview — the display half of the agreement the test above
        // pins for the ordering half. Both calls must drop the orphan, or the search would order a
        // player its enrichment then reports as unranked.
        await playerRepository.UpsertPlayerOverview(PlayerOverview.Create(new List<PlayerId> { PlayerId.Create("kept#123") }, GateWay.Europe, GameMode.GM_1v1, 13, null));

        // Act
        var ranks = await rankRepository.LoadRanksForPlayers(new List<string> { "kept#123", "orphan#456" }, 13, GateWay.Europe, GameMode.GM_1v1);

        // Assert
        Assert.AreEqual(1, ranks.Count);
        Assert.AreEqual("kept#123", ranks[0].Player1Id);
    }

    [Test]
    public async Task EnsureIndexes_BackfillsMemberIdsFromThePlayerOverview()
    {
        var rankRepository = new RankRepository(MongoClient, personalSettingsProvider);
        var playerRepository = new PlayerRepository(MongoClient);

        // A three-player team: the member the two stored fields cannot hold is what the backfill
        // must recover, and only the PlayerOverview still knows them.
        var team = new List<string> { "aaa#1", "bbb#2", "ccc#3" };
        var rank = new Rank(team, 1, 5, 100, null, GateWay.Europe, GameMode.GM_4v4_AT, 13);
        await rankRepository.InsertRanks(new List<Rank> { rank });
        await playerRepository.UpsertPlayerOverview(PlayerOverview.Create(team.Select(PlayerId.Create).ToList(), GateWay.Europe, GameMode.GM_4v4_AT, 13, null));

        // Strip the field to the shape of rows written before it existed
        var ranksCollection = MongoClient.GetDatabase("W3Champions-Statistic-Service").GetCollection<Rank>(nameof(Rank));
        await ranksCollection.UpdateManyAsync(FilterDefinition<Rank>.Empty, Builders<Rank>.Update.Unset(r => r.MemberIds));

        // Act — twice: the second run must find nothing left to fill and change nothing
        await rankRepository.EnsureIndexesAsync();
        await rankRepository.EnsureIndexesAsync();

        var standings = await rankRepository.LoadLadderStandings(new List<string> { "ccc#3" }, 13, GateWay.Europe, GameMode.GM_4v4_AT);

        // Assert — the third member finds the team again
        Assert.AreEqual(1, standings.Count);
        CollectionAssert.AreEqual(team, standings[0].MemberIds);
    }

    [Test]
    public async Task EnsureIndexes_BackfillFallsBackToTheStoredMembers_WhenTheOverviewIsMissing()
    {
        var rankRepository = new RankRepository(MongoClient, personalSettingsProvider);

        var rank = new Rank(new List<string> { "aaa#1", "bbb#2" }, 1, 5, 100, null, GateWay.Europe, GameMode.GM_2v2_AT, 13);
        await rankRepository.InsertRanks(new List<Rank> { rank });

        var ranksCollection = MongoClient.GetDatabase("W3Champions-Statistic-Service").GetCollection<Rank>(nameof(Rank));
        await ranksCollection.UpdateManyAsync(FilterDefinition<Rank>.Empty, Builders<Rank>.Update.Unset(r => r.MemberIds));

        // Act — no PlayerOverview exists, so the backfill only has Player1Id/Player2Id to go on
        await rankRepository.EnsureIndexesAsync();

        // Assert on the stored document: an orphan rank is invisible to the joined queries either way
        var stored = await ranksCollection.Find(FilterDefinition<Rank>.Empty).FirstAsync();
        CollectionAssert.AreEqual(new[] { "aaa#1", "bbb#2" }, stored.MemberIds);
    }

}
