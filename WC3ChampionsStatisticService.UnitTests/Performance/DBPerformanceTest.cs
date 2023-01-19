using NUnit.Framework;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using W3C.Contracts.GameObjects;
using W3C.Contracts.Matchmaking;
using W3ChampionsStatisticService.Matches;
using W3ChampionsStatisticService.PlayerProfiles;
using W3ChampionsStatisticService.W3ChampionsStats;

namespace WC3ChampionsStatisticService.Tests.Matches
{
    [TestFixture]
    [Ignore("Use only when performance testing DB")]
    public class DBPerformanceTest : IntegrationTestBase
    {
        [Test]
        public async Task PlayTimesPerDay_Average_TimeIsSetCorrectly_afterLoad()
        {


            var w3StatsRepo = new W3StatsRepo(MongoClient);
            var hourOfPlayStatsLoaded = await w3StatsRepo.LoadHourOfPlay();

            Assert.AreEqual(0, hourOfPlayStatsLoaded.PlayTimesPerMode[0].PlayTimePerHour[0].Minutes);
            Assert.AreEqual(0, hourOfPlayStatsLoaded.PlayTimesPerMode[0].PlayTimePerHour[0].Hours);

            Assert.AreEqual(15, hourOfPlayStatsLoaded.PlayTimesPerMode[0].PlayTimePerHour[1].Minutes);
            Assert.AreEqual(0, hourOfPlayStatsLoaded.PlayTimesPerMode[0].PlayTimePerHour[1].Hours);

            Assert.AreEqual(0, hourOfPlayStatsLoaded.PlayTimesPerMode[0].PlayTimePerHour[4].Minutes);
            Assert.AreEqual(1, hourOfPlayStatsLoaded.PlayTimesPerMode[0].PlayTimePerHour[4].Hours);
        }
        [Test]
        public async Task LoadMatchesColorful()
        {
            var matchesRepository = new MatchRepository(MongoClient, new OngoingMatchesCache(MongoClient));
            Stopwatch sw = new Stopwatch();
            sw.Start();
            string playerId = "COLORFUL#5214";
            int season = 11;
            string opponentId = null;
            GameMode gameMode = GameMode.Undefined;
            GateWay gateWay = GateWay.Undefined;
            Race playerRace = Race.Total;
            Race opponentRace = Race.Total;
            int offset = 0;
            int pageSize = 100;
            var result = await matchesRepository.LoadFor(playerId, opponentId, gateWay, gameMode, playerRace, opponentRace, pageSize, offset, season);
            for (int i = 0; i < 1000; i++)
            {
                result = await matchesRepository.LoadFor(playerId, opponentId, gateWay, gameMode, playerRace, opponentRace, pageSize, offset, season);
            }
            sw.Stop();
            Console.WriteLine("Call took " + sw.ElapsedMilliseconds + " ms - Collection size " + result.Count);
            Assert.IsTrue(result.Count > 0);
        }
        [Test]
        public async Task LoadCountColorful()
        {
            var matchesRepository = new MatchRepository(MongoClient, new OngoingMatchesCache(MongoClient));
            Stopwatch sw = new Stopwatch();
            sw.Start();
            string playerId = "COLORFUL#5214";
            int season = 11;
            string opponentId = null;
            GameMode gameMode = GameMode.Undefined;
            GateWay gateWay = GateWay.Undefined;
            Race playerRace = Race.Total;
            Race opponentRace = Race.Total;
            int offset = 0;
            int pageSize = 100;
            long result = 0;
            for (int i = 0; i < 1000; i++)
            {
                result = await matchesRepository.CountFor(playerId, opponentId, gateWay, gameMode, playerRace, opponentRace, season);
            }
            sw.Stop();
            Console.WriteLine("Call took " + sw.ElapsedMilliseconds + " ms - Collection size " + result);
            Assert.IsTrue(result > 0);
        }
        [Test]
        public async Task LoadMatchesShaDe()
        {
            var matchesRepository = new MatchRepository(MongoClient, new OngoingMatchesCache(MongoClient));
            Stopwatch sw = new Stopwatch();
            sw.Start();
            string playerId = "ShaDeFaDe#2441";
            int season = 11;
            string opponentId = null;
            GameMode gameMode = GameMode.Undefined;
            GateWay gateWay = GateWay.Undefined;
            Race playerRace = Race.Total;
            Race opponentRace = Race.Total;
            int offset = 0;
            int pageSize = 100;
            var result = await matchesRepository.LoadFor(playerId, opponentId, gateWay, gameMode, playerRace, opponentRace, pageSize, offset, season);
            for (int i = 0; i < 1000; i++)
            {
                result = await matchesRepository.LoadFor(playerId, opponentId, gateWay, gameMode, playerRace, opponentRace, pageSize, offset, season);
            }
            sw.Stop();
            Console.WriteLine("Call took " + sw.ElapsedMilliseconds + " ms - Collection size " + result.Count);
            Assert.IsTrue(result.Count > 0);
        }
        [Test]
        public async Task LoadCountShaDe()
        {
            var matchesRepository = new MatchRepository(MongoClient, new OngoingMatchesCache(MongoClient));
            Stopwatch sw = new Stopwatch();
            sw.Start();
            string playerId = "ShaDeFaDe#2441";
            int season = 11;
            string opponentId = null;
            GameMode gameMode = GameMode.Undefined;
            GateWay gateWay = GateWay.Undefined;
            Race playerRace = Race.Total;
            Race opponentRace = Race.Total;
            int offset = 0;
            int pageSize = 100;
            long result = 0;
            for (int i = 0; i < 1000; i++)
            {
                result = await matchesRepository.CountFor(playerId, opponentId, gateWay, gameMode, playerRace, opponentRace, season);
            }
            sw.Stop();
            Console.WriteLine("Call took " + sw.ElapsedMilliseconds + " ms - Collection size " + result);
            Assert.IsTrue(result > 0);
        }

        [Test]
        public async Task LoadRaceStatsShaDe()
        {
            Stopwatch sw = new Stopwatch();
            sw.Start();
            var playerRepository = new PlayerRepository(MongoClient);
            var playerLoadedAgain = await playerRepository.LoadRaceStatPerGateway("ShaDeFaDe#2441", GateWay.Europe, 1);
            for (int i = 0; i < 1000; i++)
            {
                playerLoadedAgain = await playerRepository.LoadRaceStatPerGateway("ShaDeFaDe#2441", GateWay.Europe, 1);
            }
            sw.Stop();
            Console.WriteLine("Call took " + sw.ElapsedMilliseconds + " ms - Collection size " + playerLoadedAgain.Count);
        }
        [Test]
        public async Task LoadGatewayStatsShade()
        {
            Stopwatch sw = new Stopwatch();
            sw.Start();
            var playerRepository = new PlayerRepository(MongoClient);
            var playerLoadedAgain = await playerRepository.LoadGameModeStatPerGateway("ShaDeFaDe#2441", GateWay.Europe, 1);
            for (int i = 0; i < 1000; i++)
            {
                playerLoadedAgain = await playerRepository.LoadGameModeStatPerGateway("ShaDeFaDe#2441", GateWay.Europe, 1);
            }
            sw.Stop();
            Console.WriteLine("Call took " + sw.ElapsedMilliseconds + " ms - Collection size " + playerLoadedAgain.Count);
        }
    }
}
