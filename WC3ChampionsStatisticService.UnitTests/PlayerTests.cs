using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using W3ChampionsStatisticService.CommonValueObjects;
using W3ChampionsStatisticService.PlayerProfiles;
using W3ChampionsStatisticService.PlayerProfiles.GameModeStats;
using W3ChampionsStatisticService.PlayerProfiles.RaceStats;

namespace WC3ChampionsStatisticService.UnitTests
{
    [TestFixture]
    public class PlayerTests : IntegrationTestBase
    {
        [Test]
        public async Task LoadAndSave()
        {
            var playerRepository = new PlayerRepository(MongoClient);

            var player = PlayerProfileVnext.Create("peter#123");
            await playerRepository.UpsertPlayer(player);
            var playerLoaded = await playerRepository.LoadPlayerProfile(player.BattleTag);

            Assert.AreEqual(player.BattleTag, playerLoaded.BattleTag);
        }

        [Test]
        public async Task PlayerMapping()
        {
            var playerRepository = new PlayerRepository(MongoClient);

            var player = PlayerProfileVnext.Create("peter#123");
            player.RecordWin(Race.HU, 0, true);
            await playerRepository.UpsertPlayer(player);
            var playerLoaded = await playerRepository.LoadPlayerProfile(player.BattleTag);
            playerLoaded.RecordWin(Race.UD, 0, false);
            await playerRepository.UpsertPlayer(playerLoaded);

            var playerLoadedAgain = await playerRepository.LoadPlayerProfile(player.BattleTag);

            Assert.AreEqual(player.BattleTag, playerLoaded.BattleTag);
            Assert.AreEqual(player.BattleTag, playerLoadedAgain.BattleTag);
        }

        [Test]
        public async Task PlayerStatsMapping()
        {
            var playerRepository = new PlayerRepository(MongoClient);

            var battleTagIdCombined = new BattleTagIdCombined(new List<PlayerId>
                {
                    PlayerId.Create("peter#123")
                },
                GateWay.Europe,
                GameMode.GM_1v1,
                1);
            var player = PlayerGameModeStatPerGateway.Create(battleTagIdCombined);
            player.RecordRanking(234, 123);

            await playerRepository.UpsertPlayerGameModeStatPerGateway(player);

            var playerLoadedAgain = await playerRepository.LoadGameModeStatPerGateway("peter#123", GateWay.Europe, 1);

            Assert.AreEqual(234, playerLoadedAgain.Single().MMR);
            Assert.AreEqual(123, playerLoadedAgain.Single().RankingPoints);
        }

        [Test]
        public async Task PlayerIdMappedRight()
        {
            var playerRepository = new PlayerRepository(MongoClient);

            var player1 = PlayerProfileVnext.Create("peter#123");
            var player2 = PlayerProfileVnext.Create("wolf#456");

            await playerRepository.UpsertPlayer(player1);
            await playerRepository.UpsertPlayer(player2);

            var playerLoaded = await playerRepository.LoadPlayerProfile(player2.BattleTag);

            Assert.IsNotNull(playerLoaded);
            Assert.AreEqual(player2.BattleTag, playerLoaded.BattleTag);
        }

        [Test]
        public async Task PlayerMultipleWinRecords()
        {
            var playerRepository = new PlayerRepository(MongoClient);
            var handler = new PlayerProfileVnextHandler(playerRepository);
            var handler2 = new PlayerGameModeStatPerGatewayHandler(playerRepository);
            var handler3 = new PlayerRaceStatPerGatewayHandler(playerRepository);

            var ev = TestDtoHelper.CreateFakeEvent();
            ev.match.gameMode = GameMode.GM_1v1;
            ev.match.gateway = GateWay.Europe;
            ev.match.season = 1;
            ev.match.players[0].battleTag = "peter#123";
            ev.match.players[0].battleTag = "peter#123";
            ev.match.players[0].race = Race.HU;
            ev.match.players[1].race = Race.OC;

            for (int i = 0; i < 100; i++)
            {
                await handler.Update(ev);
                await handler2.Update(ev);
                await handler3.Update(ev);
            }

            var playerLoaded = await playerRepository.LoadRaceStatPerGateway("peter#123", Race.HU, GateWay.Europe, 1);
            var playerLoadedStats = await playerRepository.LoadGameModeStatPerGateway("peter#123", GateWay.Europe, 1);

            Assert.AreEqual(100, playerLoadedStats.Single().Wins);
            Assert.AreEqual(100, playerLoaded.Wins);
        }
    }
}