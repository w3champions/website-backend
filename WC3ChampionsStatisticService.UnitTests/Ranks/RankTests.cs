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
using W3ChampionsStatisticService.PlayerProfiles.MmrRankingStats;

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
}
