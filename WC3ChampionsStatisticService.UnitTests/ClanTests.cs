using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using MongoDB.Bson;
using NUnit.Framework;
using W3ChampionsStatisticService.Clans;

namespace WC3ChampionsStatisticService.UnitTests
{
    [TestFixture]
    public class ClanTests : IntegrationTestBase
    {
        private ClanRepository _clanRepository;
        private ClanCommandHandler _handler;

        [SetUp]
        public void SetUp()
        {
            _clanRepository = new ClanRepository(MongoClient);
            _handler = new ClanCommandHandler(_clanRepository);
        }

        [Test]
        public void InvitePlayer_ClanNotPresent()
        {
            Assert.ThrowsAsync<ValidationException>(async () =>
                await _handler.InviteToClan("peter#123", ObjectId.GenerateNewId().ToString()));
        }

        [Test]
        public async Task InvitePlayer()
        {
            var clan = await CreateClanForTest();
            await _handler.InviteToClan("peter#123", clan.Id.ToString());

            var member = await _clanRepository.LoadMemberShip("peter#123");
            var clanLoaded = await _clanRepository.LoadClan(clan.Id.ToString());

            Assert.AreEqual("peter#123", member.BattleTag);
            Assert.AreEqual(clanLoaded.Id, member.PendingInviteFromClan);
            Assert.AreEqual(clanLoaded.PendingInvites.Single(), member.BattleTag);
        }

        [Test]
        public async Task CreatClan()
        {
            var clanNameExpected = "Cool Shit";
            var clan = await _handler.CreateClan(new CreateClanDto {ClanName = clanNameExpected});

            var clanLoaded = await _clanRepository.LoadClan(clan.Id.ToString());

            Assert.AreEqual(clan.ClanName, clanNameExpected);
            Assert.AreEqual(clanLoaded.ClanName, clanNameExpected);
        }

        [Test]
        public async Task CreatClanWithSameNameNotPossible()
        {
            var clanNameExpected = "Cool Shit";
            await _handler.CreateClan(new CreateClanDto {ClanName = clanNameExpected});

            Assert.ThrowsAsync<ValidationException>(async () =>
                await _handler.CreateClan(new CreateClanDto {ClanName = clanNameExpected}));
        }

        private Task<Clan> CreateClanForTest()
        {
            var clanNameExpected = "Cool Shit";
            return _handler.CreateClan(new CreateClanDto {ClanName = clanNameExpected});
        }
    }
}