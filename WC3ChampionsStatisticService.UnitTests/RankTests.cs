using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using W3ChampionsStatisticService.Ladder;
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

            var ranks = new List<Rank> { new Rank(10, 1, 12, 1456, "peter#123@10")};
            await rankRepository.Insert(ranks);
            var player = new PlayerOverview("peter#123@10", "peter#123", 10);
            player.RecordWin(true, 1234);
            await playerRepository.UpsertPlayer(player);
            await playerRepository.UpsertPlayer(player);
            await playerRepository.UpsertPlayer(player);
            var playerLoaded = await rankRepository.LoadPlayerOfLeague(1, 10);

            Assert.AreEqual(1, playerLoaded.Count);
            Assert.AreEqual("peter#123@10", playerLoaded[0].Players.First().Id);
            Assert.AreEqual(1, playerLoaded[0].Players.First().TotalWins);
            Assert.AreEqual(12, playerLoaded[0].RankNumber);
            Assert.AreEqual(1456, playerLoaded[0].RankingPoints);
            Assert.AreEqual(0, playerLoaded[0].Players.First().TotalLosses);
        }

        [Test]
        public async Task LoadAndSave_NotFound()
        {
            var rankRepository = new RankRepository(MongoClient);
            var playerRepository = new PlayerRepository(MongoClient);

            var ranks = new List<Rank> { new Rank(20, 1, 12, 1456, "peter#123@10")};
            await rankRepository.Insert(ranks);
            var player = new PlayerOverview("peter#123@10", "peter#123", 20);
            await playerRepository.UpsertPlayer(player);
            var playerLoaded = await rankRepository.LoadPlayerOfLeague(1, 10);

            Assert.IsEmpty(playerLoaded);
        }
    }
}