using System.Collections.Generic;
using System.Threading.Tasks;
using MongoDB.Bson;
using NUnit.Framework;
using W3ChampionsStatisticService.Matches;
using W3ChampionsStatisticService.MongoDb;

namespace WC3ChampionsStatisticService.UnitTests
{
    [TestFixture]
    public class MatchupRepoTests : IntegrationTestBase
    {
        [Test]
        public async Task LoadAndSave()
        {
            var matchRepository = new MatchRepository(DbConnctionInfo);

            var matchFinishedEvent = TestDtoHelper.CreateFakeEvent();

            await matchRepository.Insert(new List<Matchup> {new Matchup(matchFinishedEvent)});
            await matchRepository.Insert(new List<Matchup> {new Matchup(matchFinishedEvent)});
            var matches = await matchRepository.Load(ObjectId.Empty.ToString());

            Assert.AreEqual(2, matches.Count);
        }
    }
}