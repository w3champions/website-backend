using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using W3ChampionsStatisticService.Ladder;
using W3ChampionsStatisticService.PadEvents;
using W3ChampionsStatisticService.PlayerProfiles;

namespace WC3ChampionsStatisticService.UnitTests
{
    [TestFixture]
    public class RankHandlerTests : IntegrationTestBase
    {
        [Test]
        public async Task RanksWorking()
        {
            var rankRepository = new RankRepository(MongoClient);
            var matchEventRepository = new MatchEventRepository(MongoClient);

            var rankingChangedEvent1 = new RankingChangedEvent
            {
                gateway = 10,
                league = 1,
                ranks = new []
                {
                    new RankRaw { rp = 1200, tagId = "tag#1@10"},
                    new RankRaw { rp = 1300, tagId = "tag#2@10"},
                    new RankRaw { rp = 1500, tagId = "tag#3@10"},
                }
            };

            var rankingChangedEvent2 = new RankingChangedEvent
            {
                gateway = 10,
                league = 2,
                ranks = new []
                {
                    new RankRaw { rp = 1100, tagId = "second#1@10"},
                    new RankRaw { rp = 900, tagId = "second#3@10"},
                }
            };

            await InsertRankChangedEvent(rankingChangedEvent1);
            await InsertRankChangedEvent(rankingChangedEvent2);

            var rankHandler = new RankHandler(rankRepository, matchEventRepository);

            await rankHandler.Update();

            var ranksParsed1 = await rankRepository.LoadPlayerOfLeague(1, 10);
            var ranksParsed2 = await rankRepository.LoadPlayerOfLeague(2, 10);
            var ranksInPipe = await matchEventRepository.LoadLatestRanks();
            Assert.AreEqual(3, ranksParsed1.Count);
            Assert.AreEqual(2, ranksParsed2.Count);
            Assert.AreEqual(0, ranksInPipe.Count);
        }
    }
}