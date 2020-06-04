using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using W3ChampionsStatisticService.Clans;
using W3ChampionsStatisticService.CommonValueObjects;
using W3ChampionsStatisticService.Ladder;
using W3ChampionsStatisticService.PadEvents;
using W3ChampionsStatisticService.PersonalSettings;
using W3ChampionsStatisticService.PlayerProfiles;
using W3ChampionsStatisticService.Ports;

namespace WC3ChampionsStatisticService.UnitTests
{
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
            var rankHandler = new RankSyncHandler(new RankRepository(MongoClient), matchEventRepository);

            await InsertRankChangedEvent(TestDtoHelper.CreateRankChangedEvent("peter#123"));

            await rankHandler.Update();
            await rankHandler.Update();
        }

        [Test]
        public async Task LoadAndSave()
        {
            var rankRepository = new RankRepository(MongoClient);
            var playerRepository = new PlayerRepository(MongoClient);

            var ranks = new List<Rank> { new Rank("0_peter#123@10_GM_1v1", 1, 12, 1456, GateWay.America, GameMode.GM_1v1, 0)};
            await rankRepository.InsertRanks(ranks);
            var player = PlayerOverview.Create(new List<PlayerId> { PlayerId.Create("peter#123")}, GateWay.America, GameMode.GM_1v1, 0);
            player.RecordWin(true, 1234);
            await playerRepository.UpsertPlayerOverview(player);
            await playerRepository.UpsertPlayerOverview(player);
            await playerRepository.UpsertPlayerOverview(player);
            var playerLoaded = await rankRepository.LoadPlayersOfLeague(1, 0, GateWay.America, GameMode.GM_1v1);

            Assert.AreEqual(1, playerLoaded.Count);
            Assert.AreEqual("0_peter#123@10_GM_1v1", playerLoaded[0].Players.First().Id);
            Assert.AreEqual(1, playerLoaded[0].Players.First().Wins);
            Assert.AreEqual(12, playerLoaded[0].RankNumber);
            Assert.AreEqual(1456, playerLoaded[0].RankingPoints);
            Assert.AreEqual(0, playerLoaded[0].Players.First().Losses);
        }

        [Test]
        public async Task LoadAndSave_NotFound()
        {
            var rankRepository = new RankRepository(MongoClient);
            var playerRepository = new PlayerRepository(MongoClient);

            var ranks = new List<Rank> { new Rank("0_peter#123@10_GM_1v1", 1, 12, 1456, GateWay.Europe, GameMode.GM_1v1, 0)};
            await rankRepository.InsertRanks(ranks);
            var player = PlayerOverview.Create(new List<PlayerId> { PlayerId.Create("peter#123")}, GateWay.Europe, GameMode.GM_1v1, 0);
            await playerRepository.UpsertPlayerOverview(player);
            var playerLoaded = await rankRepository.LoadPlayersOfLeague(1, 0, GateWay.America, GameMode.GM_1v1);

            Assert.IsEmpty(playerLoaded);
        }

        [Test]
        public async Task LoadAndSave_NotDuplicatingWhenGoingUp()
        {
            var rankRepository = new RankRepository(MongoClient);
            var playerRepository = new PlayerRepository(MongoClient);

            var ranks1 = new List<Rank> { new Rank("0_peter#123@10_GM_1v1", 1, 12, 1456, GateWay.Europe, GameMode.GM_1v1, 0)};
            var ranks2 = new List<Rank> { new Rank("0_peter#123@10_GM_1v1", 1, 8, 1456, GateWay.Europe, GameMode.GM_1v1, 0)};
            await rankRepository.InsertRanks(ranks1);
            await rankRepository.InsertRanks(ranks2);
            var player = PlayerOverview.Create(new List<PlayerId> { PlayerId.Create("peter#123")}, GateWay.America, GameMode.GM_1v1, 0);
            await playerRepository.UpsertPlayerOverview(player);
            var playerLoaded = await rankRepository.LoadPlayersOfLeague(1, 0, GateWay.Europe, GameMode.GM_1v1);

            Assert.AreEqual(1, playerLoaded.Count);
            Assert.AreEqual(8, playerLoaded[0].RankNumber);
        }

        [Test]
        public async Task RankIntegrationWithMultipleIds()
        {
            var matchEventRepository = new MatchEventRepository(MongoClient);
            var rankRepository = new RankRepository(MongoClient);
            var playerRepository = new PlayerRepository(MongoClient);

            var matchFinishedEvent = TestDtoHelper.CreateFakeEvent();
            var rankingChangedEvent = TestDtoHelper.CreateRankChangedEvent();

            matchFinishedEvent.match.players[0].battleTag = "peTer#123";
            matchFinishedEvent.match.gameMode = GameMode.GM_1v1;
            matchFinishedEvent.match.gateway = GateWay.America;

            rankingChangedEvent.ranks[0].battleTags = new List<string> {"peTer#123"};
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
        public async Task ReturnRanks_WhenPlayersHavePersonalSettingsConfigured_MustHaveCorrectPersonalSettings()
        {
            // Arrange
            var rankRepository = new RankRepository(MongoClient);
            var playerRepository = new PlayerRepository(MongoClient);
            var personalSettingsRepository = new PersonalSettingsRepository(MongoClient);
            var clanRepository = new ClanRepository(MongoClient);
            var queryHandler = new RankQueryHandler(rankRepository, playerRepository, clanRepository);
           
            var ranks = new List<Rank> { new Rank("1_peter#123@10_GM_1v1", 1, 12, 1456, GateWay.America, GameMode.GM_1v1, 1) };
            await rankRepository.InsertRanks(ranks);

            var player = PlayerOverview.Create(new List<PlayerId> { PlayerId.Create("peter#123") }, GateWay.America, GameMode.GM_1v1, 1);
            player.RecordWin(true, 1234);
            await playerRepository.UpsertPlayerOverview(player);

            var playerStats = new PlayerOverallStats()
            {
                BattleTag = "peter#123",
            };
            await playerRepository.UpsertPlayer(playerStats);

            var settings = new PersonalSetting("peter#123")
            {
                ProfilePicture = new ProfilePicture(Race.HU, 5),
                Country = "BG"
            };
            await personalSettingsRepository.Save(settings);

            // Act
            var playerLoaded = await queryHandler.LoadPlayersOfLeague(1, 1, GateWay.America, GameMode.GM_1v1);

            // Assert
            Assert.AreEqual(1, playerLoaded.Count);

            var playerRank = playerLoaded[0];
            Assert.AreEqual("1_peter#123@10_GM_1v1", playerRank.Players.First().Id);
            Assert.AreEqual(playerRank.PlayersInfo[0].SelectedRace, Race.HU);
            Assert.AreEqual(playerRank.PlayersInfo[0].PictureId, 5);
            Assert.AreEqual(playerRank.PlayersInfo[0].Country, "BG");
        }

        [Test]
        public async Task ReturnRanks_WhenPlayersDoNotHavePersonalSettingsConfigured_MustHaveNotThrowError()
        {
            // Arrange
            var rankRepository = new RankRepository(MongoClient);
            var playerRepository = new PlayerRepository(MongoClient);
            var clanRepository = new ClanRepository(MongoClient);
            var queryHandler = new RankQueryHandler(rankRepository, playerRepository, clanRepository);

            var ranks = new List<Rank> { new Rank("1_peter#123@10_GM_1v1", 1, 12, 1456, GateWay.America, GameMode.GM_1v1, 1) };
            await rankRepository.InsertRanks(ranks);

            var player = PlayerOverview.Create(new List<PlayerId> { PlayerId.Create("peter#123") }, GateWay.America, GameMode.GM_1v1, 1);
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
            var rankRepository = new RankRepository(MongoClient);
            var playerRepository = new PlayerRepository(MongoClient);
            var clanRepository = new ClanRepository(MongoClient);
            var queryHandler = new RankQueryHandler(rankRepository, playerRepository, clanRepository);

            var ranks = new List<Rank> { new Rank("1_peter#123@10_GM_1v1", 1, 12, 1456, GateWay.America, GameMode.GM_1v1, 1) };
            await rankRepository.InsertRanks(ranks);

            var player = PlayerOverview.Create(new List<PlayerId> { PlayerId.Create("peter#123") }, GateWay.America, GameMode.GM_1v1, 1);
            player.RecordWin(true, 1234);
            await playerRepository.UpsertPlayerOverview(player);

            var playerStats = new PlayerOverallStats()
            {
                BattleTag = "peter#123",
            };
            await playerRepository.UpsertPlayer(playerStats);
            await clanRepository.UpsertMemberShip(new ClanMembership { BattleTag = "peter#123", ClanAbbrevation = "W3C"} );

            // Act
            var playerLoaded = await queryHandler.LoadPlayersOfLeague(1, 1, GateWay.America, GameMode.GM_1v1);

            // Assert
            Assert.AreEqual(1, playerLoaded.Count);

            var playerRank = playerLoaded[0];
            Assert.AreEqual("W3C", playerRank.PlayersInfo.Single().ClanAbbrevation);
        }
    }
}