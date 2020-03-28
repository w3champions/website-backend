using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MongoDB.Driver;
using NUnit.Framework;
using W3ChampionsStatisticService;
using W3ChampionsStatisticService.MatchEvents;
using W3ChampionsStatisticService.MongoDb;

namespace WC3ChampionsStatisticService.UnitTests
{
    public class MatchCommandHandlerTests
    {
        private readonly DbConnctionInfo _dbConnctionInfo = new DbConnctionInfo("mongodb://176.28.16.249:3510/");

        [SetUp]
        public async Task Setup()
        {
            var client = new MongoClient(_dbConnctionInfo.ConnectionString);
            await client.DropDatabaseAsync("W3Champions-Statistic-Service");
        }

        [Test]
        public async Task InsertEmptyListAndRead()
        {
            var eventRepository = new MatchEventRepository(_dbConnctionInfo);
            var handler = new InsertMatchEventsCommandHandler(eventRepository);

            await handler.Insert(new List<MatchFinishedEvent>());
            var events = await eventRepository.Load();

            Assert.IsEmpty(events);
        }

        [Test]
        public async Task InsertAndRead()
        {
            var eventRepository = new MatchEventRepository(_dbConnctionInfo);
            var handler = new InsertMatchEventsCommandHandler(eventRepository);

            await handler.Insert(new List<MatchFinishedEvent> { new MatchFinishedEvent { type = "test"} });
            var events = await eventRepository.Load();

            Assert.AreEqual("test", events.Single().type);
            Assert.IsNotNull(events.Single().Id);
            Assert.IsNotNull(events.Single().CreatedDate);
        }

        [Test]
        public async Task InsertAndRead_TimeOffset()
        {
            var eventRepository = new MatchEventRepository(_dbConnctionInfo);
            var handler = new InsertMatchEventsCommandHandler(eventRepository);

            await handler.Insert(new List<MatchFinishedEvent> { new MatchFinishedEvent { type = "test"} });
            await Task.Delay(1000);
            var now = DateTimeOffset.UtcNow;
            await handler.Insert(new List<MatchFinishedEvent> { new MatchFinishedEvent { type = "test2"} });

            var events = await eventRepository.Load(now);

            Assert.AreEqual("test2", events.Single().type);
        }
    }
}