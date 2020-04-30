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
        private PlayerRepository _playerRepository;

        [SetUp]
        public void SetUp()
        {
            _playerRepository = new PlayerRepository(MongoClient);
        }

        [Test]
        public async Task RanksWorking()
        {
            var rankRepository = new RankRepository(MongoClient);
            var matchEventRepository = new MatchEventRepository(MongoClient);

            PlayerOverview player1 = await CreateOverview("tag#1@10", "tag#1", 10);
            PlayerOverview player2 = await CreateOverview("tag#2@10", "tag#2", 10);
            PlayerOverview player3 = await CreateOverview("tag#3@10", "tag#3", 10);

            PlayerOverview player4 = await CreateOverview("second#1@10", "second#1", 10);
            PlayerOverview player5 = await CreateOverview("second#3@10", "second#3", 10);

            var rankingChangedEvent1 = new RankingChangedEvent
            {
                gateway = 10,
                league = 1,
                ranks = new []
                {
                    new RankRaw { rp = 1500, tagId = player1.Id},
                    new RankRaw { rp = 1300, tagId = player2.Id},
                    new RankRaw { rp = 1100, tagId = player3.Id},
                }
            };

            var rankingChangedEvent2 = new RankingChangedEvent
            {
                gateway = 10,
                league = 2,
                ranks = new []
                {
                    new RankRaw { rp = 1100, tagId = player4.Id},
                    new RankRaw { rp = 900, tagId = player5.Id},
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

        [Test]
        public async Task LatestRankIsAlwaysShown()
        {
            var rankRepository = new RankRepository(MongoClient);

            PlayerOverview player1 = await CreateOverview("tag#1@10", "tag#1", 10);
            PlayerOverview player2 = await CreateOverview("tag#2@10", "tag#2", 10);
            PlayerOverview player3 = await CreateOverview("tag#3@10", "tag#3", 10);

            var rankingChangedEvent1 = new RankingChangedEvent
            {
                gateway = 10,
                league = 1,
                ranks = new[]
                {
                    new RankRaw { rp = 1500, tagId = player1.Id},
                    new RankRaw { rp = 1300, tagId = player2.Id},
                    new RankRaw { rp = 1100, tagId = player3.Id},
                }
            };

            var rankingChangedEvent2 = new RankingChangedEvent
            {
                gateway = 10,
                league = 1,
                ranks = new[]
                {
                    new RankRaw { rp = 1500, tagId = player2.Id},
                    new RankRaw { rp = 1300, tagId = player1.Id},
                    new RankRaw { rp = 1100, tagId = player3.Id},
                }
            };

            await InsertRankChangedEvent(rankingChangedEvent1);
            await InsertRankChangedEvent(rankingChangedEvent2);

            var rankHandler = new RankHandler(rankRepository, new MatchEventRepository(MongoClient));

            await rankHandler.Update();

            var ranksParsed1 = await rankRepository.LoadPlayerOfLeague(1, 10);
            Assert.AreEqual(3, ranksParsed1.Count);
            Assert.AreEqual("tag#2@10", ranksParsed1[0].Id);
            Assert.AreEqual("tag#1@10", ranksParsed1[1].Id);
            Assert.AreEqual("tag#3@10", ranksParsed1[2].Id);
        }

        private async Task<PlayerOverview> CreateOverview(string id, string name, int gateway)
        {
            var player = new PlayerOverview(id, name, gateway);
            await _playerRepository.UpsertPlayer(player);
            return player;
        }
    }
}