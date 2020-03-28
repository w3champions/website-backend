using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using W3ChampionsStatisticService;
using W3ChampionsStatisticService.MatchEvents;
using W3ChampionsStatisticService.MongoDb;

namespace WC3ChampionsStatisticService.UnitTests
{
    public class MatchEventRepositoryTests
    {
        [Test]
        public async Task InsertEmptyListAndRead()
        {
            var matchEventRepository = new MatchEventRepository(new DbConnctionInfo(""));

            await matchEventRepository.Insert(new List<MatchFinishedEvent>());
            var events = await matchEventRepository.Load();

            Assert.IsEmpty(events);
        }
    }
}