using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MongoDB.Bson;
using NUnit.Framework;
using W3ChampionsStatisticService.MatchEvents;

namespace WC3ChampionsStatisticService.UnitTests
{
    [TestFixture]
    public class MatchEventCommandHandlerTests : IntegrationTestBase
    {
        [Test]
        public async Task InsertNewLeague()
        {
            var eventRepository = new MatchEventRepository(DbConnctionInfo);
            var ev1 = TestDtoHelper.CreateFakeLeague();
            var ev2 = TestDtoHelper.CreateFakeLeague();
            ev1.gateway = 10;
            ev2.gateway = 20;
            var events = new List<LeagueConstellationChangedEvent> {ev1, ev2};

            await eventRepository.Insert(events);
            await eventRepository.Insert(events);

            var leagues = await eventRepository.LoadLeagues();
            Assert.AreEqual(2, leagues.Count);
            Assert.AreEqual(10, leagues[0].id);
            Assert.AreEqual(10, leagues[0].gateway);
            Assert.AreEqual(20, leagues[1].id);
            Assert.AreEqual(20, leagues[1].gateway);
        }

        [Test]
        public async Task InsertNewRanking()
        {
            var eventRepository = new MatchEventRepository(DbConnctionInfo);
            var ev1 = TestDtoHelper.CreateFakeRankingUpdate();
            var ev2 = TestDtoHelper.CreateFakeRankingUpdate();
            ev1.gateway = 20;
            ev1.league = 2;
            ev2.gateway = 20;
            ev2.league = 3;
            var events = new List<RankingChangedEvent> {ev1, ev2};

            await eventRepository.Insert(events);
            await eventRepository.Insert(events);

            var loadedRanks = await eventRepository.LoadRanks();
            Assert.AreEqual(2, loadedRanks.Count);

            Assert.AreEqual(20, loadedRanks[0].gateway);
            Assert.AreEqual(20, loadedRanks[1].gateway);
            Assert.AreEqual(2, loadedRanks[0].league);
            Assert.AreEqual(3, loadedRanks[1].league);
        }

        [Test]
        public async Task InsertNewRanking_DuplicateBug()
        {
            var eventRepository = new MatchEventRepository(DbConnctionInfo);
            var ev1 = TestDtoHelper.CreateFakeRankingUpdate();
            var ev2 = TestDtoHelper.CreateFakeRankingUpdate();
            ev1.gateway = 20;
            ev1.league = 2;
            ev2.gateway = 10;
            ev2.league = 2;
            var events = new List<RankingChangedEvent> {ev1, ev2};

            await eventRepository.Insert(events);
            await eventRepository.Insert(events);

            var loadedRanks = await eventRepository.LoadRanks();
            Assert.AreEqual(2, loadedRanks.Count);

            Assert.AreEqual(20, loadedRanks[0].gateway);
            Assert.AreEqual(10, loadedRanks[1].gateway);
            Assert.AreEqual(2, loadedRanks[0].league);
            Assert.AreEqual(2, loadedRanks[1].league);
        }

        [Test]
        public async Task InsertEmptyListAndRead()
        {
            var eventRepository = new MatchEventRepository(DbConnctionInfo);
            var handler = new InsertMatchEventsCommandHandler(eventRepository);

            await handler.Insert(new List<MatchFinishedEvent>());
            var events = await eventRepository.Load(ObjectId.Empty.ToString());

            Assert.IsEmpty(events);
        }

        [Test]
        public async Task InsertAndRead()
        {
            var eventRepository = new MatchEventRepository(DbConnctionInfo);
            var handler = new InsertMatchEventsCommandHandler(eventRepository);

            await handler.Insert(new List<MatchFinishedEvent> { new MatchFinishedEvent { match = new Match { map = "test"}}});
            var events = await eventRepository.Load();

            Assert.AreEqual("test", events.Single().match.map);
        }

        [Test]
        public async Task InsertAndRead_TimeOffset()
        {
            var eventRepository = new MatchEventRepository(DbConnctionInfo);
            var handler = new InsertMatchEventsCommandHandler(eventRepository);

            var generateNewId = ObjectId.GenerateNewId();
            await handler.Insert(new List<MatchFinishedEvent>
            {
                new MatchFinishedEvent
                {
                    Id = generateNewId,
                    match = new Match
                    {
                        map = "test"
                    }
                }
            });
            await Task.Delay(1000);
            await handler.Insert(new List<MatchFinishedEvent> { new MatchFinishedEvent { match = new Match { map = "test2"}} });

            var events = await eventRepository.Load(generateNewId.ToString());

            Assert.AreEqual("test2", events.Single().match.map);
        }

        [Test]
        public async Task InsertAndRead_Limit()
        {
            var eventRepository = new MatchEventRepository(DbConnctionInfo);
            var handler = new InsertMatchEventsCommandHandler(eventRepository);

            var generateNewId = ObjectId.GenerateNewId();
            await handler.Insert(new List<MatchFinishedEvent> { new MatchFinishedEvent {
                Id = generateNewId, match = new Match { map = "test"}} });
            await handler.Insert(new List<MatchFinishedEvent> { new MatchFinishedEvent { match = new Match { map = "test2"}} });
            await handler.Insert(new List<MatchFinishedEvent> { new MatchFinishedEvent { match = new Match { map = "test3"}} });

            var events = await eventRepository.Load(generateNewId.ToString(), 1);

            Assert.AreEqual("test2", events.Single().match.map);
        }
    }
}