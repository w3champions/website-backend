using System.Threading.Tasks;
using MongoDB.Driver;
using Moq;
using NUnit.Framework;
using W3ChampionsStatisticService.PadEvents;
using W3ChampionsStatisticService.PadEvents.FakeEventSync;
using W3ChampionsStatisticService.PadEvents.PadSync;
using W3ChampionsStatisticService.PlayerProfiles;
using W3ChampionsStatisticService.ReadModelBase;

namespace WC3ChampionsStatisticService.UnitTests
{
    [TestFixture]
    public class FakeHandlerTests : IntegrationTestBase
    {
        private Mock<IPadServiceRepo> _padServiceMock;
        private MatchEventRepository _matchEventRepository;
        private PlayerRepository _playerRepository;
        private TempLossesRepo _tempLossesRepo;

        [Test]
        public async Task GetGames()
        {
            var fakeEventSyncHandler = CreateSUT();
            var playerProfile = PlayerProfile.Create("peter#123@10", "peter#123");
            await _playerRepository.UpsertPlayer(playerProfile);
            _padServiceMock.Setup(p => p.GetPlayer("peter#123")).ReturnsAsync(TestDtoHelper.CreateFakePadPlayer());
            await fakeEventSyncHandler.Update();

            var events = await _matchEventRepository.Load();

            Assert.AreEqual(3, events.Count);
            Assert.AreEqual(true, events[0].match.players[0].won);
            Assert.AreEqual(Race.HU, events[0].match.players[0].race);
            Assert.AreEqual(true, events[1].match.players[0].won);
            Assert.AreEqual(Race.NE, events[1].match.players[0].race);
            Assert.AreEqual(false, events[2].match.players[0].won);
            Assert.AreEqual(Race.HU, events[2].match.players[0].race);
        }

        private FakeEventSyncHandler CreateSUT()
        {
            _padServiceMock = new Mock<IPadServiceRepo>();
            _matchEventRepository = new MatchEventRepository(MongoClient);
            _playerRepository = new PlayerRepository(MongoClient);
            _tempLossesRepo = new TempLossesRepo(MongoClient);
            return new FakeEventSyncHandler(
                _padServiceMock.Object,
                new VersionRepository(MongoClient),
                _matchEventRepository,
                _playerRepository,
                new FakeEventCreator(_tempLossesRepo));
        }
    }
}