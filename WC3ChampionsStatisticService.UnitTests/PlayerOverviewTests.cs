using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using W3ChampionsStatisticService.CommonValueObjects;
using W3ChampionsStatisticService.Ladder;
using W3ChampionsStatisticService.PlayerProfiles;
using W3ChampionsStatisticService.W3ChampionsStats.MmrDistribution;

namespace WC3ChampionsStatisticService.UnitTests
{
    [TestFixture]
    public class PlayerOverviewTests : IntegrationTestBase
    {
        [Test]
        public async Task LoadAndSave()
        {
            var playerRepository = new PlayerRepository(MongoClient);

            var player = PlayerOverview.Create(new List<PlayerId> { PlayerId.Create("peter#123")}, GateWay.Europe, GameMode.GM_1v1, 0, null);
            await playerRepository.UpsertPlayerOverview(player);
            var playerLoaded = await playerRepository.LoadOverview(player.Id);

            Assert.AreEqual(player.Id, playerLoaded.Id);
            Assert.AreEqual(GateWay.Europe, playerLoaded.GateWay);
        }

        [Test]
        public void UpdateOverview()
        {
            var player = PlayerOverview.Create(new List<PlayerId> { PlayerId.Create("peter#123")}, GateWay.Europe, GameMode.GM_1v1, 0, null);
            player.RecordWin(true, 1230);
            player.RecordWin(false, 1240);
            player.RecordWin(false, 1250);

            Assert.AreEqual(3, player.Games);
            Assert.AreEqual(1, player.Wins);
            Assert.AreEqual(2, player.Losses);
            Assert.AreEqual("peter#123", player.PlayerIds[0].BattleTag);
            Assert.AreEqual("peter", player.PlayerIds[0].Name);
            Assert.AreEqual("0_peter#123@20_GM_1v1", player.Id);
            Assert.AreEqual(1250, player.MMR);
        }

        [Test]
        public void UpdateOverview_2v2AT()
        {
            var player = PlayerOverview.Create(new List<PlayerId> { PlayerId.Create("peter#123"), PlayerId.Create("wolf#123")}, GateWay.Europe, GameMode.GM_2v2_AT, 0, null);
            player.RecordWin(true, 1230);

            Assert.AreEqual(1, player.Games);
            Assert.AreEqual(1, player.Wins);

            Assert.AreEqual(GameMode.GM_2v2_AT, player.GameMode);
            Assert.AreEqual(1, player.Games);
            Assert.AreEqual(1, player.Wins);
            Assert.AreEqual(0, player.Losses);
        }

        [Test]
        public async Task UpdateOverview_HandlerUpdate_1v1()
        {
            var matchFinishedEvent = TestDtoHelper.CreateFakeEvent();
            var playerRepository = new PlayerRepository(MongoClient);
            var playOverviewHandler = new PlayOverviewHandler(playerRepository);

            matchFinishedEvent.match.players[0].won = true;
            matchFinishedEvent.match.players[1].won = false;
            matchFinishedEvent.match.players[0].battleTag = "peter#123";
            matchFinishedEvent.match.gateway = GateWay.America;
            matchFinishedEvent.match.gameMode = GameMode.GM_1v1;

            await playOverviewHandler.Update(matchFinishedEvent);

            var playerProfile = await playerRepository.LoadOverview("0_peter#123@10_GM_1v1");

            Assert.AreEqual(1, playerProfile.Wins);
            Assert.AreEqual(0, playerProfile.Losses);
            Assert.AreEqual(GameMode.GM_1v1, playerProfile.GameMode);
        }

        [Test]
        public async Task UpdateOverview_HandlerUpdate_2v2AT()
        {
            var matchFinishedEvent = TestDtoHelper.CreateFake2v2AtEvent();
            var playerRepository = new PlayerRepository(MongoClient);
            var playOverviewHandler = new PlayOverviewHandler(playerRepository);

            matchFinishedEvent.match.players[0].battleTag = "peter#123";
            matchFinishedEvent.match.players[0].atTeamId = "t1";

            matchFinishedEvent.match.players[1].battleTag = "wolf#123";
            matchFinishedEvent.match.players[1].atTeamId = "t1";
            matchFinishedEvent.match.gateway = GateWay.America;
            matchFinishedEvent.match.gameMode = GameMode.GM_2v2_AT;

            await playOverviewHandler.Update(matchFinishedEvent);

            var playerProfile = await playerRepository.LoadOverview("0_peter#123@10_wolf#123@10_GM_2v2_AT");

            Assert.AreEqual(1, playerProfile.Wins);
            Assert.AreEqual(0, playerProfile.Losses);
            Assert.AreEqual(GameMode.GM_2v2_AT, playerProfile.GameMode);
        }

        [Test]
        public async Task UpdateOverview_HandlerUpdate_FFA()
        {
            var matchFinishedEvent = TestDtoHelper.CreateFakeFFAEvent();
            var playerRepository = new PlayerRepository(MongoClient);
            var playOverviewHandler = new PlayOverviewHandler(playerRepository);

            await playOverviewHandler.Update(matchFinishedEvent);

            var winners = matchFinishedEvent.match.players.Where(x => x.won);

            Assert.AreEqual(1, winners.Count());

            foreach (var player in winners)
            {
                var playerProfile = await playerRepository.LoadOverview($"0_{player.battleTag}@20_FFA");

                Assert.AreEqual(1, playerProfile.Wins);
                Assert.AreEqual(0, playerProfile.Losses);
                Assert.AreEqual(GameMode.FFA, playerProfile.GameMode);
            }

            var losers = matchFinishedEvent.match.players.Where(x => !x.won);

            Assert.AreEqual(3, losers.Count());

            foreach (var player in losers)
            {
                var playerProfile = await playerRepository.LoadOverview($"0_{player.battleTag}@20_FFA");

                Assert.AreEqual(0, playerProfile.Wins);
                Assert.AreEqual(1, playerProfile.Losses);
                Assert.AreEqual(GameMode.FFA, playerProfile.GameMode);
            }
        }

        [Test]
        public async Task UpdateOverview_HandlerUpdate_2v2RT()
        {
            var matchFinishedEvent = TestDtoHelper.CreateFake2v2RTEvent();
            var playerRepository = new PlayerRepository(MongoClient);
            var playOverviewHandler = new PlayOverviewHandler(playerRepository);

            await playOverviewHandler.Update(matchFinishedEvent);

            var winners = matchFinishedEvent.match.players.Where(x => x.won);
            Assert.AreEqual(2, winners.Count());

            foreach (var player in winners)
            {
                var playerProfile = await playerRepository.LoadOverview($"0_{player.battleTag}@20_GM_2v2");

                Assert.AreEqual(1, playerProfile.Wins);
                Assert.AreEqual(0, playerProfile.Losses);
                Assert.AreEqual(GameMode.GM_2v2, playerProfile.GameMode);
            }

            var losers = matchFinishedEvent.match.players.Where(x => !x.won);

            Assert.AreEqual(2, losers.Count());

            foreach (var player in losers)
            {
                var playerProfile = await playerRepository.LoadOverview($"0_{player.battleTag}@20_GM_2v2");

                Assert.AreEqual(0, playerProfile.Wins);
                Assert.AreEqual(1, playerProfile.Losses);
                Assert.AreEqual(GameMode.GM_2v2, playerProfile.GameMode);
            }
        }

        [Test]
        public async Task UpdateOverview_HandlerUpdate_2v2RTvsAT()
        {
            var matchFinishedEvent = TestDtoHelper.CreateFake2v2RTEvent();
            var playerRepository = new PlayerRepository(MongoClient);
            var playOverviewHandler = new PlayOverviewHandler(playerRepository);

            matchFinishedEvent.match.players[2].atTeamId = "t2";
            matchFinishedEvent.match.players[3].atTeamId = "t2";

            await playOverviewHandler.Update(matchFinishedEvent);

            var winners = matchFinishedEvent.match.players.Where(x => x.won);
            Assert.AreEqual(2, winners.Count());

            foreach (var player in winners)
            {
                var playerProfile = await playerRepository.LoadOverview($"0_{player.battleTag}@20_GM_2v2");

                Assert.AreEqual(1, playerProfile.Wins);
                Assert.AreEqual(0, playerProfile.Losses);
                Assert.AreEqual(GameMode.GM_2v2, playerProfile.GameMode);
            }

            var losers = matchFinishedEvent.match.players.Where(x => !x.won);

            Assert.AreEqual(2, losers.Count());

            var playerProfileTeam = await playerRepository.LoadOverview($"0_{matchFinishedEvent.match.players[2].battleTag}@20_{matchFinishedEvent.match.players[3].battleTag}@20_GM_2v2_AT");

            Assert.AreEqual(0, playerProfileTeam.Wins);
            Assert.AreEqual(1, playerProfileTeam.Losses);
            Assert.AreEqual(GameMode.GM_2v2_AT, playerProfileTeam.GameMode);
        }


        [Test]
        public async Task UpdateOverview_HandlerUpdate_1v1_doubleWins()
        {
            var matchFinishedEvent = TestDtoHelper.CreateFakeEvent();
            var playerRepository = new PlayerRepository(MongoClient);
            var playOverviewHandler = new PlayOverviewHandler(playerRepository);

            matchFinishedEvent.match.players[0].battleTag = "peter#123";
            matchFinishedEvent.match.gateway = GateWay.America;
            matchFinishedEvent.match.gameMode = GameMode.GM_1v1;

            await playOverviewHandler.Update(matchFinishedEvent);
            await playOverviewHandler.Update(matchFinishedEvent);
            await playOverviewHandler.Update(matchFinishedEvent);

            var playerProfile = await playerRepository.LoadOverview("0_peter#123@10_GM_1v1");

            Assert.AreEqual(3, playerProfile.Wins);
            Assert.AreEqual(0, playerProfile.Losses);
            Assert.AreEqual(GameMode.GM_1v1, playerProfile.GameMode);
        }

        [Test]
        public async Task UpdateOverview_HandlerUpdate_RaceBasedMMR()
        {
            var playerRepository = new PlayerRepository(MongoClient);
            var playOverviewHandler = new PlayOverviewHandler(playerRepository);


            var matchFinishedEvent1 = TestDtoHelper.CreateFakeEvent();
            matchFinishedEvent1.match.players[0].battleTag = "peter#123";
            matchFinishedEvent1.match.season = 2;
            matchFinishedEvent1.match.players[0].race = Race.HU;
            matchFinishedEvent1.match.gateway = GateWay.America;
            matchFinishedEvent1.match.gameMode = GameMode.GM_1v1;

            var matchFinishedEvent2 = TestDtoHelper.CreateFakeEvent();
            matchFinishedEvent2.match.players[0].battleTag = "peter#123";
            matchFinishedEvent2.match.season = 2;
            matchFinishedEvent2.match.players[0].race = Race.NE;
            matchFinishedEvent2.match.gateway = GateWay.America;
            matchFinishedEvent2.match.gameMode = GameMode.GM_1v1;

            await playOverviewHandler.Update(matchFinishedEvent1);
            await playOverviewHandler.Update(matchFinishedEvent2);

            var playerProfile1 = await playerRepository.LoadOverview("2_peter#123@10_GM_1v1_HU");
            var playerProfile2 = await playerRepository.LoadOverview("2_peter#123@10_GM_1v1_NE");

            Assert.AreEqual(1, playerProfile1.Wins);
            Assert.AreEqual(0, playerProfile1.Losses);
            Assert.AreEqual(1, playerProfile2.Wins);
            Assert.AreEqual(0, playerProfile2.Losses);

            Assert.AreEqual(Race.HU, playerProfile1.Race);
            Assert.AreEqual(Race.NE, playerProfile2.Race);
        }

        [Test]
        public async Task PlayerStats_LoadMMRsByValidEnumValues()
        {

            var testing_season = 0;
            var playerRepository = new PlayerRepository(MongoClient);
            var playOverviewHandler = new PlayOverviewHandler(playerRepository);

            var mmrDistributionHandler = new MmrDistributionHandler(playerRepository);

            var gateWayValues = Enum.GetValues(typeof(GateWay));
            var gameModeValues = Enum.GetValues(typeof(GameMode));
        
            foreach (GateWay gateWay in gateWayValues)
            {
                //skip undefinded value of ENums
                if (gateWay == GateWay.Undefined) continue;
                foreach (GameMode gameMode in gameModeValues)
                {
                    if (gameMode == GameMode.Undefined || gameMode ==GameMode.GM_2v2 || gameMode == GameMode.GM_2v2_AT ) continue;

                    var matchFinishedEvent1 = TestDtoHelper.CreateFakeEvent();
                    matchFinishedEvent1.match.players[0].battleTag = "peter#123";
                    matchFinishedEvent1.match.season = testing_season;
                    matchFinishedEvent1.match.players[0].race = Race.HU;
                    matchFinishedEvent1.match.gateway = gateWay;
                    matchFinishedEvent1.match.gameMode = gameMode;

                    var matchFinishedEvent2 = TestDtoHelper.CreateFakeEvent();
                    matchFinishedEvent2.match.players[0].battleTag = "peter#123";
                    matchFinishedEvent2.match.season = testing_season;
                    matchFinishedEvent2.match.players[0].race = Race.NE;
                    matchFinishedEvent2.match.gateway = gateWay;
                    matchFinishedEvent2.match.gameMode = gameMode;

                    await playOverviewHandler.Update(matchFinishedEvent1);
                    await playOverviewHandler.Update(matchFinishedEvent2);


                    var distribution = await mmrDistributionHandler.GetDistributions(testing_season, gateWay, gameMode);
                    Assert.IsNotNull(distribution);
                }
            }

        }

    }
}