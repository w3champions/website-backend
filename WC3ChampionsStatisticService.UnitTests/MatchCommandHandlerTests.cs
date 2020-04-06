using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MongoDB.Bson;
using NUnit.Framework;
using W3ChampionsStatisticService.MatchEvents;

namespace WC3ChampionsStatisticService.UnitTests
{
    [TestFixture]
    public class MatchCommandHandlerTests : IntegrationTestBase
    {
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