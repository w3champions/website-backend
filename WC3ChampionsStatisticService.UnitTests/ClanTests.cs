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
                await _handler.InviteToClan("peter#123", ObjectId.GenerateNewId().ToString(), "doesNotMatter"));
        }

        [Test]
        public async Task InvitePlayer()
        {
            var clan = await CreateFoundedClanForTest();
            await _handler.InviteToClan("peter#123", clan.Id.ToString(), clan.ChiefTain);

            var member = await _clanRepository.LoadMemberShip("peter#123");
            var clanLoaded = await _clanRepository.LoadClan(clan.Id.ToString());

            Assert.AreEqual("peter#123", member.BattleTag);
            Assert.AreEqual(clanLoaded.Id, member.PendingInviteFromClan);
            Assert.AreEqual(clanLoaded.PendingInvites.Single(), member.BattleTag);
        }

        [Test]
        public async Task SignPetition()
        {
            var clan = await CreateClanForTest();
            await _handler.SignClanPetition("peter#123", clan.Id.ToString());

            var member = await _clanRepository.LoadMemberShip("peter#123");
            var clanLoaded = await _clanRepository.LoadClan(clan.Id.ToString());

            Assert.AreEqual("peter#123", member.BattleTag);
            Assert.IsNull(member.PendingInviteFromClan);
            Assert.AreEqual(clanLoaded.Id, member.ClanId);
            Assert.IsEmpty(clanLoaded.PendingInvites);
            Assert.AreEqual("peter#123", clanLoaded.FoundingFathers[1]);
        }

        [Test]
        public async Task CreateClan()
        {
            var clanNameExpected = "Cool Shit";
            var clan = await _handler.CreateClan(clanNameExpected, "Peter#123");

            var clanLoaded = await _clanRepository.LoadClan(clan.Id.ToString());

            Assert.AreEqual(clan.ClanName, clanNameExpected);
            Assert.AreEqual(clan.ClanName, clanNameExpected);
            Assert.AreNotEqual(clanLoaded.Id, ObjectId.Empty);
        }

        [Test]
        public async Task CreatClanWithSameNameNotPossible()
        {
            var clanNameExpected = "Cool Shit";
            await _handler.CreateClan(clanNameExpected, "Peter#123");

            Assert.ThrowsAsync<ValidationException>(async () =>
                await _handler.CreateClan(clanNameExpected, "Peter#123"));
        }

        [Test]
        public async Task CreatClan_FounderGetsCreated()
        {
            var clanNameExpected = "Cool Shit";
            var clan = await _handler.CreateClan(clanNameExpected, "Peter#123");

            var founder = await _clanRepository.LoadMemberShip("Peter#123");

            Assert.AreEqual(founder.ClanId, clan.Id);
            Assert.AreEqual(founder.BattleTag, "Peter#123");
        }

        [Test]
        public async Task CreatClan_FoundingTwiceIsProhibitted()
        {
            await _handler.CreateClan("Cool Shit", "Peter#123");

            Assert.ThrowsAsync<ValidationException>(async () => await _handler.CreateClan("Cool Shit NEW", "Peter#123"));
        }

        private Task<Clan> CreateClanForTest()
        {
            var clanNameExpected = "Cool Shit";
            return _handler.CreateClan(clanNameExpected, "Peter#123");
        }

        private async Task<Clan> CreateFoundedClanForTest()
        {
            var clanNameExpected = "Cool Shit";
            var clan = await _handler.CreateClan(clanNameExpected, "Peter#123");
            for (int i = 0; i < 6; i++)
            {
                await _handler.SignClanPetition($"btag#{i}", clan.Id.ToString());
            }

            return clan;
        }
    }
}