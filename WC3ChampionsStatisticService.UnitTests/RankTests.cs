using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using W3ChampionsStatisticService.Ladder;
using W3ChampionsStatisticService.Matches;
using W3ChampionsStatisticService.PadEvents;
using W3ChampionsStatisticService.PlayerProfiles;

namespace WC3ChampionsStatisticService.UnitTests
{
    [TestFixture]
    public class RankTests : IntegrationTestBase
    {
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

            var rankHandler = new RankHandler(rankRepository, matchEventRepository);

            await playOverviewHandler.Update(matchFinishedEvent);
            await rankHandler.Update();

            var rank = await rankRepository.SearchPlayerOfLeague("peT", GateWay.America, GameMode.GM_1v1);

            Assert.AreEqual(1, rank.Count);
        }
    }
}