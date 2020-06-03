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
        public async Task InvitePlayer_ThatHasAlreadySigned_Founder()
        {
            var clan = await _handler.CreateClan("egal", "CS", "Peter#123");

            Assert.ThrowsAsync<ValidationException>(async () => await _handler.InviteToClan("Peter#123", clan.IdRaw, "Peter#123"));
        }

        [Test]
        public async Task PromoteToShaman()
        {
            var clan = await CreateFoundedClanForTest();

            await _handler.AddShamanToClan(clan.Members[2], clan.IdRaw, clan.ChiefTain);

            var clanLoaded = await _clanRepository.LoadClan(clan.IdRaw);

            Assert.AreEqual(clan.Members[2], clanLoaded.Shamans.Single());
        }

        [Test]
        public async Task LeaveAsChieftain()
        {
            var clan = await CreateFoundedClanForTest();

            Assert.ThrowsAsync<ValidationException>(async () =>
                await _handler.LeaveClan(clan.IdRaw, clan.ChiefTain));
        }

        [Test]
        public async Task SwitchChieftain()
        {
            var clan = await CreateFoundedClanForTest();

            await _handler.SwitchChieftain(clan.Members[1], clan.IdRaw, clan.ChiefTain);

            var clanLoaded = await _clanRepository.LoadClan(clan.IdRaw);

            Assert.AreEqual(clan.Members[1], clanLoaded.ChiefTain);
        }

        [Test]
        public async Task SwitchChieftain_ShamanIsRemoved()
        {
            var clan = await CreateFoundedClanForTest();

            await _handler.AddShamanToClan(clan.Members[1], clan.IdRaw, clan.ChiefTain);
            await _handler.SwitchChieftain(clan.Members[1], clan.IdRaw, clan.ChiefTain);

            var clanLoaded = await _clanRepository.LoadClan(clan.IdRaw);

            Assert.AreEqual(clan.Members[1], clanLoaded.ChiefTain);
            Assert.IsEmpty(clan.Shamans);
        }

        [Test]
        public async Task SwitchChieftain_NotChieftainActing()
        {
            var clan = await CreateFoundedClanForTest();

            Assert.ThrowsAsync<ValidationException>(async () => await _handler.SwitchChieftain(
                clan.Members[1],
                clan.IdRaw,
                clan.Members[1]));
        }

        [Test]
        public async Task SwitchChieftain_NewChieftainNotInClan()
        {
            var clan = await CreateFoundedClanForTest();

            Assert.ThrowsAsync<ValidationException>(async () => await _handler.SwitchChieftain(
                "NotInClan#123",
                clan.IdRaw,
                clan.ChiefTain));
        }

        [Test]
        public async Task DemoteShaman()
        {
            var clan = await CreateFoundedClanForTest();

            await _handler.AddShamanToClan(clan.Members[2], clan.IdRaw, clan.ChiefTain);
            await _handler.RemoveShamanFromClan(clan.Members[2], clan.IdRaw, clan.ChiefTain);

            var clanLoaded = await _clanRepository.LoadClan(clan.IdRaw);

            Assert.IsEmpty(clanLoaded.Shamans);
        }

        [Test]
        public async Task PromoteShamanThatISNotInClan_Fails()
        {
            var clan = await CreateFoundedClanForTest();

            Assert.ThrowsAsync<ValidationException>(async () => await _handler.AddShamanToClan("NotInChal#123", clan.IdRaw, clan.ChiefTain));
        }

        [Test]
        public async Task PromoteShamanThatIsChieftain_Fails()
        {
            var clan = await CreateFoundedClanForTest();

            Assert.ThrowsAsync<ValidationException>(async () => await _handler.AddShamanToClan(clan.ChiefTain, clan.IdRaw, clan.ChiefTain));
        }

        [Test]
        public async Task InvitePlayer_PlayerRejects_IsNotAddedToFoundingFathers()
        {
            var clan = await _handler.CreateClan("egal", "CS", "Peter#123");
            await _handler.InviteToClan("NewGUY#123", clan.IdRaw, "Peter#123");
            await _handler.RevokeInvitationToClan("NewGUY#123", clan.IdRaw, "Peter#123");

            var clanLoaded = await _clanRepository.LoadClan(clan.IdRaw);

            Assert.AreEqual(1, clanLoaded.FoundingFathers.Count);
            Assert.AreEqual("Peter#123", clanLoaded.FoundingFathers[0]);
        }

        [Test]
        public async Task InvitePlayer_ThatHasAlreadySigned()
        {
            var clan = await _handler.CreateClan("egal", "CS", "Peter#123");
            await _handler.InviteToClan("NewGUY#123", clan.IdRaw, "Peter#123");
            await _handler.AcceptInvite("NewGUY#123", clan.IdRaw);

            Assert.ThrowsAsync<ValidationException>(async () => await _handler.InviteToClan("NewGUY#123", clan.IdRaw, "Peter#123"));
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
        public async Task RevokeInvite()
        {
            var clan = await CreateFoundedClanForTest();

            await _handler.InviteToClan("peter#123", clan.Id.ToString(), clan.ChiefTain);
            await _handler.RevokeInvitationToClan("peter#123", clan.Id.ToString(), clan.ChiefTain);

            var member = await _clanRepository.LoadMemberShip("peter#123");
            var clanLoaded = await _clanRepository.LoadClan(clan.Id.ToString());

            Assert.AreEqual("peter#123", member.BattleTag);
            Assert.IsNull(member.PendingInviteFromClan);
            Assert.IsEmpty(clanLoaded.PendingInvites);
        }

        [Test]
        public async Task SignPetition()
        {
            var clanNameExpected = "Cool Shit";
            var clan = await _handler.CreateClan(clanNameExpected, "CS", "Peter#123");

            await _handler.InviteToClan("peter#123", clan.Id.ToString(), "Peter#123");
            await _handler.AcceptInvite("peter#123", clan.Id.ToString());

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
            var clan = await _handler.CreateClan(clanNameExpected, "CS", "Peter#123");

            var clanLoaded = await _clanRepository.LoadClan(clan.Id.ToString());

            Assert.AreEqual(clan.ClanName, clanNameExpected);
            Assert.AreEqual(clan.ClanName, clanNameExpected);
            Assert.AreNotEqual(clanLoaded.Id, ObjectId.Empty);
        }

        [Test]
        public async Task KickMember_NotInClan()
        {
            var clan = await CreateFoundedClanForTest();

            Assert.ThrowsAsync<ValidationException>(async () => await _handler.KickPlayer(
                "NotInClan#123",
                clan.Id.ToString(),
                clan.ChiefTain));
        }

        [Test]
        public async Task KickMember_NotChieftain()
        {
            var clan = await CreateFoundedClanForTest();

            Assert.ThrowsAsync<ValidationException>(async () => await _handler.KickPlayer(
                clan.Members[3],
                clan.Id.ToString(),
                clan.Members[2]));
        }

        [Test]
        public async Task KickMember()
        {
            var clan = await CreateFoundedClanForTest();

            await _handler.KickPlayer(
                clan.Members[1],
                clan.Id.ToString(),
                clan.ChiefTain);

            var clanLoaded = await _clanRepository.LoadClan(clan.IdRaw);

            Assert.AreEqual(5, clanLoaded.Members.Count);
            Assert.IsFalse(clanLoaded.Members.Contains(clan.Members[1]));
        }

        [Test]
        public async Task KickMember_Cheiftain()
        {
            var clan = await CreateFoundedClanForTest();

            Assert.ThrowsAsync<ValidationException>(async () => await _handler.KickPlayer(
                clan.ChiefTain,
                clan.Id.ToString(),
                clan.ChiefTain));
        }

        [Test]
        public async Task KickMember_Shaman()
        {
            var clan = await CreateFoundedClanForTest();

            await _handler.AddShamanToClan(
                clan.Members[1],
                clan.Id.ToString(),
                clan.ChiefTain);

            await _handler.KickPlayer(
                clan.Members[1],
                clan.Id.ToString(),
                clan.ChiefTain);

            var clanLoaded = await _clanRepository.LoadClan(clan.IdRaw);

            Assert.AreEqual(0, clanLoaded.Shamans.Count);
            Assert.IsFalse(clanLoaded.Shamans.Contains(clanLoaded.Members[1]));
        }

        [Test]
        public async Task CreatClanWithSameNameNotPossible()
        {
            var clanNameExpected = "Cool Shit";
            await _handler.CreateClan(clanNameExpected, "CS", "Peter#123");

            Assert.ThrowsAsync<ValidationException>(async () =>
                await _handler.CreateClan(clanNameExpected, "CS", "Peter#123"));
        }

        [Test]
        public async Task CreatClan_FounderGetsCreated()
        {
            var clanNameExpected = "Cool Shit";
            var clan = await _handler.CreateClan(clanNameExpected, "CS", "Peter#123");

            var founder = await _clanRepository.LoadMemberShip("Peter#123");

            Assert.AreEqual(founder.ClanId, clan.Id);
            Assert.AreEqual(founder.BattleTag, "Peter#123");
        }

        [Test]
        public async Task CreatClan_FoundingTwiceIsProhibitted()
        {
            await _handler.CreateClan("Cool Shit", "CS", "Peter#123");

            Assert.ThrowsAsync<ValidationException>(async () => await _handler.CreateClan("Cool Shit NEW", "CS", "Peter#123"));
        }

        [Test]
        public async Task DeleteClan()
        {
            var clan = await CreateFoundedClanForTest();

            await _handler.DeleteClan(clan.IdRaw, clan.ChiefTain);

            var clanLoaded = await _clanRepository.LoadClan(clan.IdRaw);
            Assert.IsNull(clanLoaded);


            var chieftain = await _clanRepository.LoadMemberShip(clan.ChiefTain);
            Assert.IsNull(chieftain.ClanId);

            foreach (var clanMember in clan.Members)
            {
                var member = await _clanRepository.LoadMemberShip(clanMember);
                Assert.IsNull(member.ClanId);
            }
        }

        private async Task<Clan> CreateFoundedClanForTest()
        {
            var clan = await _handler.CreateClan("Cool Stuff", "CS", "Peter#123");
            for (int i = 0; i < 6; i++)
            {
                await _handler.InviteToClan($"btag#{i}", clan.Id.ToString(), "Peter#123");
                await _handler.AcceptInvite($"btag#{i}", clan.Id.ToString());
            }

            return await _clanRepository.LoadClan(clan.IdRaw);
        }
    }
}