using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using W3ChampionsStatisticService.CommonValueObjects;
using W3ChampionsStatisticService.Matches;
using W3ChampionsStatisticService.PersonalSettings;
using W3ChampionsStatisticService.PlayerProfiles;
using W3ChampionsStatisticService.PlayerProfiles.GameModeStats;
using W3ChampionsStatisticService.PlayerProfiles.MmrRankingStats;
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

            var player = PlayerOverallStats.Create("peter#123");
            await playerRepository.UpsertPlayer(player);
            var playerLoaded = await playerRepository.LoadPlayerProfile(player.BattleTag);

            Assert.AreEqual(player.BattleTag, playerLoaded.BattleTag);
        }

        [Test]
        public async Task Search()
        {
            var playerRepository = new PlayerRepository(MongoClient);

            var player = PlayerOverallStats.Create("Peter#123");
            var player2 = PlayerOverallStats.Create("Anderer#123");
            await playerRepository.UpsertPlayer(player);
            await playerRepository.UpsertPlayer(player2);
            var playerLoaded = await playerRepository.SearchForPlayer("ete");

            Assert.AreEqual(player.BattleTag, playerLoaded[0].BattleTag);
            Assert.AreEqual(1, playerLoaded.Count);
        }

        [Test]
        public async Task Search_DuplicateNameBug()
        {
            var playerRepository = new PlayerRepository(MongoClient);

            var player = PlayerOverallStats.Create("ThunderHorn#2481");
            var player2 = PlayerOverallStats.Create("ThunderHorn#21132");
            await playerRepository.UpsertPlayer(player);
            await playerRepository.UpsertPlayer(player2);
            var playerLoaded = await playerRepository.SearchForPlayer("thunder");

            Assert.AreEqual(player.BattleTag, playerLoaded[1].BattleTag);
            Assert.AreEqual(player2.BattleTag, playerLoaded[0].BattleTag);
            Assert.AreEqual(2, playerLoaded.Count);
        }

        [Test]
        public async Task Search_DuplicateNameBug_RefineWithBtag()
        {
            var playerRepository = new PlayerRepository(MongoClient);

            var player = PlayerOverallStats.Create("ThunderHorn#2481");
            var player2 = PlayerOverallStats.Create("ThunderHorn#21132");
            await playerRepository.UpsertPlayer(player);
            await playerRepository.UpsertPlayer(player2);
            var playerLoaded = await playerRepository.SearchForPlayer("thunderhorn#2481");

            Assert.AreEqual(player.BattleTag, playerLoaded[0].BattleTag);
            Assert.AreEqual(1, playerLoaded.Count);
        }

        [Test]
        public async Task PlayerMapping()
        {
            var playerRepository = new PlayerRepository(MongoClient);

            var player = PlayerOverallStats.Create("peter#123");
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
                1,
                null);
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

            var player1 = PlayerOverallStats.Create("peter#123");
            var player2 = PlayerOverallStats.Create("wolf#456");

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
            var personalSettingsRepo = new PersonalSettingsRepository(MongoClient);

            var handler = new PlayerOverallStatsHandler(playerRepository, personalSettingsRepo);
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


        [Test]
        public async Task PlayersFromAn1v1Match_GameModeHandler_Test()
        {
            var playerRepository = new PlayerRepository(MongoClient);
            var handler = new PlayerGameModeStatPerGatewayHandler(playerRepository);

            var ev = TestDtoHelper.CreateFakeEvent();
            ev.match.season = 1;

            ev.match.players[0].battleTag = "peter#123";
            ev.match.players[0].won = true;

            ev.match.players[1].battleTag = "wolf#456";
            ev.match.players[1].won = false;

            await handler.Update(ev);

            var winnerStatGateWay = await playerRepository.LoadGameModeStatPerGateway("peter#123", GateWay.Europe, 1);
            var loser = await playerRepository.LoadGameModeStatPerGateway("wolf#456", GateWay.Europe, 1);

            Assert.AreEqual(1, winnerStatGateWay.First(x => x.GameMode == GameMode.GM_1v1).Wins);

            Assert.AreEqual(1, loser.First(x => x.GameMode == GameMode.GM_1v1).Losses);
            Assert.AreEqual(0, loser.First(x => x.GameMode == GameMode.GM_1v1).Wins);
        }

        [Test]
        public async Task PlayersFromAn2v2ATMatch_GameModeHandler_Test()
        {
            var playerRepository = new PlayerRepository(MongoClient);
            var handler = new PlayerGameModeStatPerGatewayHandler(playerRepository);

            var ev = TestDtoHelper.CreateFake2v2AtEvent();
            ev.match.season = 1;

            ev.match.players[0].battleTag = "peter#123";
            ev.match.players[0].won = true;

            ev.match.players[1].battleTag = "wolf#456";
            ev.match.players[1].won = true;

            ev.match.players[2].battleTag = "TEAM2#123";
            ev.match.players[2].won = false;

            ev.match.players[3].battleTag = "TEAM2#456";
            ev.match.players[3].won = false;

            await handler.Update(ev);

            var winners = await playerRepository.LoadGameModeStatPerGateway("1_peter#123@10_wolf#456@10_GM_2v2_AT", GateWay.America, 1);
            var losers = await playerRepository.LoadGameModeStatPerGateway("1_TEAM2#123@10_TEAM2#456@10_GM_2v2_AT", GateWay.America, 1);

            Assert.AreEqual(1, winners.First(x => x.GameMode == GameMode.GM_2v2_AT).Wins);
            Assert.AreEqual(0, winners.First(x => x.GameMode == GameMode.GM_2v2_AT).Losses);

            Assert.AreEqual(1, losers.First(x => x.GameMode == GameMode.GM_2v2_AT).Losses);
            Assert.AreEqual(0, losers.First(x => x.GameMode == GameMode.GM_2v2_AT).Wins);
        }

        [Test]
        public async Task PlayersFromAn2v2AT_vs_RTMatch_GameModeHandler_Test()
        {
            var playerRepository = new PlayerRepository(MongoClient);
            var handler = new PlayerGameModeStatPerGatewayHandler(playerRepository);

            var ev = TestDtoHelper.CreateFake2v2AtEvent();
            ev.match.season = 1;

            ev.match.players[0].battleTag = "peter#123";
            ev.match.players[0].won = true;
            ev.match.players[0].atTeamId = null;

            ev.match.players[1].battleTag = "wolf#456";
            ev.match.players[1].won = true;
            ev.match.players[1].atTeamId = null;

            ev.match.players[2].battleTag = "TEAM2#123";
            ev.match.players[2].won = false;

            ev.match.players[3].battleTag = "TEAM2#456";
            ev.match.players[3].won = false;

            await handler.Update(ev);

            var winnerP1 = await playerRepository.LoadGameModeStatPerGateway($"1_peter#123@10_GM_2v2", GateWay.America, 1);
            var winnerP2 = await playerRepository.LoadGameModeStatPerGateway($"1_wolf#456@10_GM_2v2", GateWay.America, 1);
            var losers = await playerRepository.LoadGameModeStatPerGateway("1_TEAM2#123@10_TEAM2#456@10_GM_2v2_AT", GateWay.America, 1);

            Assert.AreEqual(1, winnerP1.First(x => x.GameMode == GameMode.GM_2v2).Wins);
            Assert.AreEqual(0, winnerP1.First(x => x.GameMode == GameMode.GM_2v2).Losses);

            Assert.AreEqual(1, winnerP2.First(x => x.GameMode == GameMode.GM_2v2).Wins);
            Assert.AreEqual(0, winnerP2.First(x => x.GameMode == GameMode.GM_2v2).Losses);

            Assert.AreEqual(1, losers.First(x => x.GameMode == GameMode.GM_2v2_AT).Losses);
            Assert.AreEqual(0, losers.First(x => x.GameMode == GameMode.GM_2v2_AT).Wins);
        }

        [Test]
        public async Task PlayersFromAnFFAMatch_GameModeHandler_Test()
        {
            var playerRepository = new PlayerRepository(MongoClient);
            var handler = new PlayerGameModeStatPerGatewayHandler(playerRepository);

            var ev = TestDtoHelper.CreateFakeFFAEvent();
            ev.match.season = 1;

            ev.match.players[0].battleTag = "peter#123";
            ev.match.players[0].won = true;

            ev.match.players[1].battleTag = "wolf#456";
            ev.match.players[1].won = false;

            ev.match.players[2].battleTag = "TEAM3#123";
            ev.match.players[2].won = false;

            ev.match.players[3].battleTag = "TEAM4#456";
            ev.match.players[3].won = false;

            await handler.Update(ev);

            var winnerStatGateWay = await playerRepository.LoadGameModeStatPerGateway("peter#123", GateWay.Europe, 1);
            var loser1 = await playerRepository.LoadGameModeStatPerGateway("wolf#456", GateWay.Europe, 1);
            var loser2 = await playerRepository.LoadGameModeStatPerGateway("TEAM3#123", GateWay.Europe, 1);
            var loser3 = await playerRepository.LoadGameModeStatPerGateway("TEAM4#456", GateWay.Europe, 1);

            Assert.AreEqual(1, winnerStatGateWay.First(x => x.GameMode == GameMode.FFA).Wins);

            Assert.AreEqual(1, loser1.First(x => x.GameMode == GameMode.FFA).Losses);
            Assert.AreEqual(0, loser1.First(x => x.GameMode == GameMode.FFA).Wins);

            Assert.AreEqual(1, loser2.First(x => x.GameMode == GameMode.FFA).Losses);
            Assert.AreEqual(0, loser2.First(x => x.GameMode == GameMode.FFA).Wins);

            Assert.AreEqual(1, loser3.First(x => x.GameMode == GameMode.FFA).Losses);
            Assert.AreEqual(0, loser3.First(x => x.GameMode == GameMode.FFA).Wins);
        }

        [Test]
        public async Task Player_UpdateMmrTimeline()
        {
            var playerRepository = new PlayerRepository(MongoClient);
            var matchRepository = new MatchRepository(MongoClient);
            await matchRepository.EnsureIndices();
            var handler = new PlayerMmrTimelineHandler(playerRepository, matchRepository);
            var matchFinishedEvent1 = TestDtoHelper.CreateFakeEvent();
            var matchFinishedEvent2 = TestDtoHelper.CreateFakeEvent();

            matchFinishedEvent1.match.endTime = 1585701559200; 
            matchFinishedEvent1.match.players[0].race = Race.OC;
            matchFinishedEvent1.match.players[1].race = Race.NE;

            matchFinishedEvent2.match.endTime = 1585692047363;
            matchFinishedEvent2.match.players[0].won = false;
            matchFinishedEvent2.match.players[0].race = Race.OC;
            matchFinishedEvent2.match.players[1].won = true;
            matchFinishedEvent2.match.players[1].race = Race.NE;

            await matchRepository.Insert(Matchup.Create(matchFinishedEvent1));
            await matchRepository.Insert(Matchup.Create(matchFinishedEvent2));

            var ev = TestDtoHelper.CreateFakeEvent();
            ev.match.endTime = 1604612998269;
            ev.match.players[0].race = Race.OC;
            ev.match.players[1].race = Race.NE;

            await handler.Update(ev);

            var playerMmrTimeline = await playerRepository.LoadPlayerMmrTimeline("peter#123", Race.OC, GateWay.Europe, 0);
            // Todo: add some more content based asserts for the timeline
            Assert.IsNotNull(playerMmrTimeline);
        }
    }
}