using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using W3ChampionsStatisticService.Ladder;
using W3ChampionsStatisticService.Matches;
using W3ChampionsStatisticService.PlayerProfiles;

namespace WC3ChampionsStatisticService.UnitTests
{
    [TestFixture]
    public class PlayerOverviewTests : IntegrationTestBase
    {
        [Test]
        public async Task LoadAndSave()
        {
            var playerRepository = new PlayerRepository(MongoClient);

            var player = new PlayerOverview("peter#123@10", "peter#123", 20);
            await playerRepository.UpsertPlayer(player);
            var playerLoaded = await playerRepository.LoadOverview(player.Id);

            Assert.AreEqual(player.Id, playerLoaded.Id);
            Assert.AreEqual(20, playerLoaded.GateWay);
        }


        [Test]
        public async Task LoadAndSearch()
        {
            var playerRepository = new PlayerRepository(MongoClient);

            var player = new PlayerOverview("peter#123@20", "peter#123", 20);
            await playerRepository.UpsertPlayer(player);
            var playerLoaded = (await playerRepository.LoadOverviewLike("PeT", 20)).Single();

            Assert.AreEqual(player.Id, playerLoaded.Id);
            Assert.AreEqual(20, playerLoaded.GateWay);
        }

        [Test]
        public async Task LoadAndSearch_EmptyString()
        {
            var playerRepository = new PlayerRepository(MongoClient);

            var player = new PlayerOverview("peter#123@20", "peter#123", 20);
            await playerRepository.UpsertPlayer(player);
            Assert.IsEmpty(await playerRepository.LoadOverviewLike("", 20));
        }

        [Test]
        public async Task LoadAndSearch_NulLString()
        {
            var playerRepository = new PlayerRepository(MongoClient);

            var player = new PlayerOverview("peter#123@20", "peter#123", 20);
            await playerRepository.UpsertPlayer(player);
            Assert.IsEmpty(await playerRepository.LoadOverviewLike(null, 20));
        }

        [Test]
        public void UpdateOverview()
        {
            var player = new PlayerOverview("peter#123@10", "peter#123", 1);
            player.RecordWin(true, 1230, GameMode.GM_1v1);
            player.RecordWin(false, 1240, GameMode.GM_1v1);
            player.RecordWin(false, 1250, GameMode.GM_1v1);

            Assert.AreEqual(3, player.Games);
            Assert.AreEqual(1, player.TotalWins);
            Assert.AreEqual(2, player.TotalLosses);
            Assert.AreEqual("123", player.BattleTag);
            Assert.AreEqual("peter", player.Name);
            Assert.AreEqual("peter#123@10", player.Id);
            Assert.AreEqual(1250, player.MMR);
        }

        [Test]
        public void UpdateOverview_2v2AT()
        {
            var player = new PlayerOverview("peter#123@10", "peter#123", 1);
            player.RecordWin(true, 1230, GameMode.GM_2v2_AT);

            Assert.AreEqual(1, player.Games);
            Assert.AreEqual(1, player.TotalWins);

            Assert.AreEqual(GameMode.GM_2v2_AT, player.WinsByMode[1].GameMode);
            Assert.AreEqual(1, player.WinsByMode[1].Games);
            Assert.AreEqual(1, player.WinsByMode[1].Wins);
            Assert.AreEqual(0, player.WinsByMode[1].Losses);

        }
    }
}