using System.Threading.Tasks;
using MongoDB.Bson;
using NUnit.Framework;
using W3ChampionsStatisticService.Matches;
using W3C.Domain.CommonValueObjects;

namespace WC3ChampionsStatisticService.Tests.Matchups
{
    [TestFixture]
    public class MatchupDetailTests : IntegrationTestBase
    {
        [Test]
        public async Task LoadDetails_NotDetailsAvailable()
        {
            var matchFinishedEvent = TestDtoHelper.CreateFakeEvent();
            matchFinishedEvent.match.id = "nmhcCLaRc7";
            matchFinishedEvent.Id = ObjectId.GenerateNewId();
            var matchRepository = new MatchRepository(MongoClient, matchesProvider, new OngoingMatchesCache(MongoClient));

            await matchRepository.Insert(Matchup.Create(matchFinishedEvent));

            var result = await matchRepository.LoadDetails(matchFinishedEvent.Id);
            Assert.AreEqual("nmhcCLaRc7", result.Match.MatchId);
        }

        [Test]
        public async Task LoadDetails()
        {
            var matchFinishedEvent = TestDtoHelper.CreateFakeEvent();
            matchFinishedEvent.match.id = "nmhcCLaRc7";
            matchFinishedEvent.Id = ObjectId.GenerateNewId();
            matchFinishedEvent.result.players[0].heroes[0].icon = "Archmage";
            matchFinishedEvent.result.players[1].heroes[0].icon = "Warden";

            await InsertMatchEvent(matchFinishedEvent);

            var matchRepository = new MatchRepository(MongoClient, matchesProvider, new OngoingMatchesCache(MongoClient));

            await matchRepository.Insert(Matchup.Create(matchFinishedEvent));

            var result = await matchRepository.LoadDetails(matchFinishedEvent.Id);

            Assert.AreEqual("nmhcCLaRc7", result.Match.MatchId);
            Assert.AreEqual("Archmage", result.PlayerScores[0].Heroes[0].icon);
            Assert.AreEqual("Warden", result.PlayerScores[1].Heroes[0].icon);
        }

        [Test]
        public async Task LoadDetails_DetermineRandomRace()
        {
            var matchFinishedEvent = TestDtoHelper.CreateFakeEvent();
            matchFinishedEvent.match.id = "nmhcCLaRc7";
            matchFinishedEvent.Id = ObjectId.GenerateNewId();

            // Set race to random.
            matchFinishedEvent.result.players[0].raceId = (int)RaceId.HU;
            matchFinishedEvent.match.players.Find(p => p.battleTag == matchFinishedEvent.result.players[0].battleTag).race = Race.RnD;

            await InsertMatchEvent(matchFinishedEvent);

            var matchRepository = new MatchRepository(MongoClient, matchesProvider, new OngoingMatchesCache(MongoClient));

            await matchRepository.Insert(Matchup.Create(matchFinishedEvent));

            var result = await matchRepository.LoadDetails(matchFinishedEvent.Id);

            Assert.AreEqual("nmhcCLaRc7", result.Match.MatchId);
            Assert.AreEqual(Race.RnD, result.Match.Teams[0].Players[0].Race);
            Assert.AreEqual(Race.HU, result.Match.Teams[0].Players[0].RndRace);
        }

        [Test]
        public async Task LoadDetails_RandomRaceNotSetForNonRandomSelection()
        {
            var matchFinishedEvent = TestDtoHelper.CreateFakeEvent();
            matchFinishedEvent.match.id = "nmhcCLaRc7";
            matchFinishedEvent.Id = ObjectId.GenerateNewId();

            // Set race to non-random.
            matchFinishedEvent.match.players[0].race = Race.HU;
            matchFinishedEvent.match.players[0].rndRace = null;
            matchFinishedEvent.result.players[0].raceId = (int)RaceId.HU;
            matchFinishedEvent.match.players.Find(p => p.battleTag == matchFinishedEvent.result.players[0].battleTag).race = Race.HU;

            await InsertMatchEvent(matchFinishedEvent);

            var matchRepository = new MatchRepository(MongoClient, matchesProvider, new OngoingMatchesCache(MongoClient));

            await matchRepository.Insert(Matchup.Create(matchFinishedEvent));

            var result = await matchRepository.LoadDetails(matchFinishedEvent.Id);

            Assert.AreEqual("nmhcCLaRc7", result.Match.MatchId);
            Assert.AreEqual(Race.HU, result.Match.Teams[0].Players[0].Race);
            Assert.AreEqual(null, result.Match.Teams[0].Players[0].RndRace);
        }

        [Test]
        public async Task LoadDetails_RandomRaceSetToRandomWhenNullResult()
        {
            var matchFinishedEvent = TestDtoHelper.CreateFakeEvent();
            matchFinishedEvent.match.id = "nmhcCLaRc7";
            matchFinishedEvent.Id = ObjectId.GenerateNewId();
            matchFinishedEvent.result = null;

            var player = matchFinishedEvent.match.players[0];

            // Set race to random.
            matchFinishedEvent.match.players[0].race = Race.RnD;

            await InsertMatchEvent(matchFinishedEvent);

            var matchRepository = new MatchRepository(MongoClient, matchesProvider, new OngoingMatchesCache(MongoClient));

            await matchRepository.Insert(Matchup.Create(matchFinishedEvent));

            var result = await matchRepository.LoadDetails(matchFinishedEvent.Id);

            Assert.AreEqual("nmhcCLaRc7", result.Match.MatchId);
            Assert.AreEqual(Race.RnD, result.Match.Teams[0].Players.Find(p => p.BattleTag == player.battleTag).Race);
            Assert.AreEqual(Race.RnD, result.Match.Teams[0].Players.Find(p => p.BattleTag == player.battleTag).RndRace);
        }
    }
}