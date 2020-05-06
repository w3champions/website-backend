using System.Threading.Tasks;
using NUnit.Framework;
using W3ChampionsStatisticService.Matches;
using W3ChampionsStatisticService.PlayerProfiles;

namespace WC3ChampionsStatisticService.UnitTests
{
    [TestFixture]
    public class PlayerTests : IntegrationTestBase
    {
        [Test]
        public async Task LoadAndSave()
        {
            var playerRepository = new PlayerRepository(MongoClient);

            var player = PlayerProfile.Create("peter#123");
            await playerRepository.UpsertPlayer(player);
            var playerLoaded = await playerRepository.Load(player.Id);

            Assert.AreEqual(player.Id, playerLoaded.Id);
        }

        [Test]
        public async Task PlayerMapping()
        {
            var playerRepository = new PlayerRepository(MongoClient);

            var player = PlayerProfile.Create("peter#123");
            player.RecordWin(Race.HU, GameMode.GM_1v1, true);
            await playerRepository.UpsertPlayer(player);
            var playerLoaded = await playerRepository.Load(player.Id);
            playerLoaded.RecordWin(Race.UD, GameMode.GM_1v1, false);
            playerLoaded.UpdateRank(GameMode.GM_1v1, 234, 123);
            await playerRepository.UpsertPlayer(playerLoaded);

            var playerLoadedAgain = await playerRepository.Load(player.Id);

            Assert.AreEqual(player.Id, playerLoaded.Id);
            Assert.AreEqual(player.Id, playerLoadedAgain.Id);
            Assert.AreEqual(234, playerLoadedAgain.GameModeStats[0].MMR);
            Assert.AreEqual(123, playerLoadedAgain.GameModeStats[0].RankingPoints);
        }

        [Test]
        public async Task PlayerIdMappedRight()
        {
            var playerRepository = new PlayerRepository(MongoClient);

            var player1 = PlayerProfile.Create("peter#123");
            var player2 = PlayerProfile.Create("wolf#456");

            await playerRepository.UpsertPlayer(player1);
            await playerRepository.UpsertPlayer(player2);

            var playerLoaded = await playerRepository.Load(player2.Id);

            Assert.IsNotNull(playerLoaded);
            Assert.AreEqual(player2.Id, playerLoaded.Id);
        }

        [Test]
        public async Task PlayerMultipleWinRecords()
        {
            var playerRepository = new PlayerRepository(MongoClient);
            var handler = new PlayerModelHandler(playerRepository);

            var ev = TestDtoHelper.CreateFakeEvent();
            ev.match.players[0].battleTag = "peter#123";
            ev.match.players[0].race = Race.HU;
            ev.match.players[1].race = Race.OC;
            ev.match.players[0].battleTag = "PEteR#123";

            for (int i = 0; i < 100; i++)
            {
                await handler.Update(ev);
            }

            var playerLoaded = await playerRepository.Load("peter#123");

            Assert.AreEqual(100, playerLoaded.TotalWins);
            Assert.AreEqual(100, playerLoaded.GameModeStats[0].Wins);
            Assert.AreEqual(100, playerLoaded.RaceStats[0].Wins);
        }
    }
}