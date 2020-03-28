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
    public class MatchEventRepositoryTests
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
            var matchEventRepository = new MatchEventRepository(_dbConnctionInfo);

            await matchEventRepository.Insert(new List<MatchFinishedEvent>());
            var events = await matchEventRepository.Load();

            Assert.IsEmpty(events);
        }

        [Test]
        public async Task InsertAndRead()
        {
            var matchEventRepository = new MatchEventRepository(_dbConnctionInfo);

            await matchEventRepository.Insert(new List<MatchFinishedEvent>() { new MatchFinishedEvent { type = "test"} });
            var events = await matchEventRepository.Load();

            Assert.AreEqual("test", events.Single().type);
            Assert.IsNotNull(events.Single().Id);
            Assert.IsNotNull(events.Single().CreatedDate);
        }
    }
}