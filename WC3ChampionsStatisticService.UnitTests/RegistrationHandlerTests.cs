using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using W3ChampionsStatisticService.Authorization;
using W3ChampionsStatisticService.PlayerProfiles;
using W3ChampionsStatisticService.Ports;

namespace WC3ChampionsStatisticService.UnitTests
{
    [TestFixture]
    public class RegistrationHandlerTests : IntegrationTestBase
    {
        private string _notfounduser = "NotFoundUser#123";
        private Mock<IBlizzardAuthenticationService> _authMock;

        [SetUp]
        public void SetUp()
        {
            _authMock = new Mock<IBlizzardAuthenticationService>();
            _authMock.Setup(m => m.GetUser(_notfounduser))
                .ReturnsAsync(new BlizzardUserInfo {battletag = _notfounduser});
        }

        [Test]
        public async Task OverviewGetsCreatedForClanSearch()
        {
            var playerRepository = new PlayerRepository(MongoClient);
            var registrationHandler = new RegistrationHandler(_authMock.Object, playerRepository);

            await registrationHandler.GetUserOrRegister(_notfounduser);

            var playerOverview = await playerRepository.LoadPlayerProfile(_notfounduser);

            Assert.AreEqual(_notfounduser, playerOverview.BattleTag);
            Assert.AreEqual(0, playerOverview.WinLosses[0].Games);
            Assert.AreEqual(0, playerOverview.WinLosses[1].Games);
            Assert.AreEqual(0, playerOverview.WinLosses[2].Games);
            Assert.AreEqual(0, playerOverview.WinLosses[3].Games);
            Assert.AreEqual(0, playerOverview.WinLosses[4].Games);
        }
    }
}