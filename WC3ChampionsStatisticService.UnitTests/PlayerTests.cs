using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using W3ChampionsStatisticService.CommonValueObjects;
using W3ChampionsStatisticService.Ladder;
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
            var playerLoaded = await playerRepository.LoadPlayer(player.BattleTag);

            Assert.AreEqual(player.BattleTag, playerLoaded.BattleTag);
        }

        [Test]
        public async Task PlayerMapping()
        {
            var playerRepository = new PlayerRepository(MongoClient);

            var player = PlayerProfile.Create("peter#123");
            player.RecordWin(Race.HU, GameMode.GM_1v1, GateWay.Europe, 0, true);
            await playerRepository.UpsertPlayer(player);
            var playerLoaded = await playerRepository.LoadPlayer(player.BattleTag);
            playerLoaded.RecordWin(Race.UD, GameMode.GM_1v1, GateWay.Europe, 0, false);
            playerLoaded.UpdateRank(GameMode.GM_1v1, GateWay.Europe, 234, 123, 0);
            await playerRepository.UpsertPlayer(playerLoaded);

            var playerLoadedAgain = await playerRepository.LoadPlayer(player.BattleTag);

            Assert.AreEqual(player.BattleTag, playerLoaded.BattleTag);
            Assert.AreEqual(player.BattleTag, playerLoadedAgain.BattleTag);
            Assert.AreEqual(234, playerLoadedAgain.GetStatForGateway(GateWay.Europe).GameModeStats[0].MMR);
            Assert.AreEqual(123, playerLoadedAgain.GetStatForGateway(GateWay.Europe).GameModeStats[0].RankingPoints);
        }

        [Test]
        public async Task PlayerIdMappedRight()
        {
            var playerRepository = new PlayerRepository(MongoClient);

            var player1 = PlayerProfile.Create("peter#123");
            var player2 = PlayerProfile.Create("wolf#456");

            await playerRepository.UpsertPlayer(player1);
            await playerRepository.UpsertPlayer(player2);

            var playerLoaded = await playerRepository.LoadPlayer(player2.BattleTag);

            Assert.IsNotNull(playerLoaded);
            Assert.AreEqual(player2.BattleTag, playerLoaded.BattleTag);
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

            ev.match.gateway = GateWay.America;

            for (int i = 0; i < 100; i++)
            {
                await handler.Update(ev);
            }

            var playerLoaded = await playerRepository.LoadPlayer("peter#123");

            Assert.AreEqual(100, playerLoaded.TotalWins);
            Assert.AreEqual(100, playerLoaded.GateWayStats[0].GameModeStats[0].Wins);
            Assert.AreEqual(100, playerLoaded.RaceStats[0].Wins);
        }

        [Test]
        public async Task QueryHandler_ATRankingPicksAlwaysTheBest()
        {
            var rankRepository = new RankRepository(MongoClient);
            var playerRepository = new PlayerRepository(MongoClient);
            var playerQueryHandler = new PlayerQueryHandler(playerRepository, rankRepository);

            await rankRepository.InsertLeagues(new List<LeagueConstellation>
            {
                new LeagueConstellation(0, GateWay.Europe, GameMode.GM_2v2_AT, new List<League>
                {
                    new League(1, 0, "GrandMaster", 0),
                    new League(1, 1, "Master", 0),
                    new League(1, 2, "Diamond", 1),
                    new League(1, 2, "Diamond", 2),
                })
            });

            await rankRepository.InsertRanks(new List<Rank>
            {
                new Rank("0_hans#123@20_wurst#456@20_GM_2v2_AT", 3, 10, 3000, GateWay.Europe, GameMode.GM_2v2_AT, 0),
                new Rank("0_hans#123@20_peter#456@20_GM_2v2_AT", 2, 10, 3000, GateWay.Europe, GameMode.GM_2v2_AT, 0),
            });

            var playerProfile = PlayerProfile.Create("hans#123");
            playerProfile.RecordWin(Race.HU, GameMode.GM_2v2_AT, GateWay.Europe, 1, true);
            var playerOverview1 = PlayerOverview.Create(new List<PlayerId> { PlayerId.Create("hans#123"), PlayerId.Create("wurst#456") }, GateWay.Europe, GameMode.GM_2v2_AT, 0);
            var playerOverview2 = PlayerOverview.Create(new List<PlayerId> { PlayerId.Create("hans#123"), PlayerId.Create("peter#456") }, GateWay.Europe, GameMode.GM_2v2_AT, 0);

            await playerRepository.UpsertPlayer(playerProfile);
            await playerRepository.UpsertPlayerOverview(playerOverview2);
            await playerRepository.UpsertPlayerOverview(playerOverview1);

            var playerLoaded = await playerQueryHandler.LoadPlayerWithRanks("hans#123", 0);

            Assert.AreEqual(2, playerLoaded.GetStatForGateway(GateWay.Europe).GameModeStats[1].LeagueId);
        }
    }
}