using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using MongoDB.Bson;
using NUnit.Framework;
using W3ChampionsStatisticService.Clans;
using W3ChampionsStatisticService.CommonValueObjects;
using W3ChampionsStatisticService.Ladder;
using W3ChampionsStatisticService.PlayerProfiles;
using W3ChampionsStatisticService.Ports;

namespace WC3ChampionsStatisticService.UnitTests
{
    [TestFixture]
    public class ClanTests : IntegrationTestBase
    {
        private IClanRepository _clanRepository;
        private IRankRepository _rankRepository;
        private ClanCommandHandler _handler;

        [SetUp]
        public void SetUp()
        {
            _clanRepository = new ClanRepository(MongoClient);
            _rankRepository = new RankRepository(MongoClient, personalSettingsProvider);
            _handler = new ClanCommandHandler(_clanRepository, _rankRepository, null);
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

            Assert.ThrowsAsync<ValidationException>(async () => await _handler.InviteToClan("Peter#123", clan.ClanId, "Peter#123"));
        }

        [Test]
        public async Task PromoteToShaman()
        {
            var clan = await CreateFoundedClanForTest();

            await _handler.AddShamanToClan(clan.Members[2], clan.ClanId, clan.ChiefTain);

            var clanLoaded = await _clanRepository.LoadClan(clan.ClanId);

            Assert.AreEqual(clan.Members[2], clanLoaded.Shamans.Single());
        }

        [Test]
        public async Task LeaveAsChieftain()
        {
            var clan = await CreateFoundedClanForTest();

            Assert.ThrowsAsync<ValidationException>(async () =>
                await _handler.LeaveClan(clan.ClanId, clan.ChiefTain));
        }

        [Test]
        public async Task SwitchChieftain()
        {
            var clan = await CreateFoundedClanForTest();

            var newChieftain = clan.Members[1];
            await _handler.AddShamanToClan(newChieftain, clan.ClanId, clan.ChiefTain);
            await _handler.SwitchChieftain(newChieftain, clan.ClanId, clan.ChiefTain);

            var clanLoaded = await _clanRepository.LoadClan(clan.ClanId);

            Assert.AreEqual(clanLoaded.Shamans[0], clan.ChiefTain);
            Assert.AreEqual(clanLoaded.ChiefTain, newChieftain);
            Assert.IsFalse(clanLoaded.Members.Contains(newChieftain));
        }

        [Test]
        public async Task SwitchChieftain_ShamanIsRemoved()
        {
            var clan = await CreateFoundedClanForTest();

            await _handler.AddShamanToClan(clan.Members[1], clan.ClanId, clan.ChiefTain);
            await _handler.SwitchChieftain(clan.Members[1], clan.ClanId, clan.ChiefTain);

            var clanLoaded = await _clanRepository.LoadClan(clan.ClanId);

            Assert.AreEqual(clan.Members[1], clanLoaded.ChiefTain);
            Assert.IsEmpty(clan.Shamans);
        }

        [Test]
        public async Task SwitchChieftain_NotChieftainActing()
        {
            var clan = await CreateFoundedClanForTest();

            Assert.ThrowsAsync<ValidationException>(async () => await _handler.SwitchChieftain(
                clan.Members[1],
                clan.ClanId,
                clan.Members[1]));
        }

        [Test]
        public async Task SwitchChieftain_NewChieftainNotInClan()
        {
            var clan = await CreateFoundedClanForTest();

            Assert.ThrowsAsync<ValidationException>(async () => await _handler.SwitchChieftain(
                "NotInClan#123",
                clan.ClanId,
                clan.ChiefTain));
        }

        [Test]
        public async Task DemoteShaman()
        {
            var clan = await CreateFoundedClanForTest();

            await _handler.AddShamanToClan(clan.Members[2], clan.ClanId, clan.ChiefTain);
            await _handler.RemoveShamanFromClan(clan.Members[2], clan.ClanId, clan.ChiefTain);

            var clanLoaded = await _clanRepository.LoadClan(clan.ClanId);

            Assert.IsEmpty(clanLoaded.Shamans);
        }

        [Test]
        public async Task PromoteShamanThatISNotInClan_Fails()
        {
            var clan = await CreateFoundedClanForTest();

            Assert.ThrowsAsync<ValidationException>(async () => await _handler.AddShamanToClan("NotInChal#123", clan.ClanId, clan.ChiefTain));
        }

        [Test]
        public async Task PromoteShamanThatIsChieftain_Fails()
        {
            var clan = await CreateFoundedClanForTest();

            Assert.ThrowsAsync<ValidationException>(async () => await _handler.AddShamanToClan(clan.ChiefTain, clan.ClanId, clan.ChiefTain));
        }

        [Test]
        public async Task InvitePlayer_PlayerRejects_IsNotAddedToFoundingFathers()
        {
            var clan = await _handler.CreateClan("egal", "CS", "Peter#123");
            await _handler.InviteToClan("NewGUY#123", clan.ClanId, "Peter#123");
            await _handler.RevokeInvitationToClan("NewGUY#123", clan.ClanId, "Peter#123");

            var clanLoaded = await _clanRepository.LoadClan(clan.ClanId);

            Assert.AreEqual(1, clanLoaded.FoundingFathers.Count);
            Assert.AreEqual("Peter#123", clanLoaded.FoundingFathers[0]);
        }

        [Test]
        public async Task InvitePlayer_ThatHasAlreadySigned()
        {
            var clan = await _handler.CreateClan("egal", "CS", "Peter#123");
            await _handler.InviteToClan("NewGUY#123", clan.ClanId, "Peter#123");
            await _handler.AcceptInvite("NewGUY#123", clan.ClanId);

            Assert.ThrowsAsync<ValidationException>(async () => await _handler.InviteToClan("NewGUY#123", clan.ClanId, "Peter#123"));
        }

        [Test]
        public async Task InvitePlayer()
        {
            var clan = await CreateFoundedClanForTest();
            await _handler.InviteToClan("peter#123", clan.ClanId, clan.ChiefTain);

            var member = await _clanRepository.LoadMemberShip("peter#123");
            var clanLoaded = await _clanRepository.LoadClan(clan.ClanId);

            Assert.AreEqual("peter#123", member.BattleTag);
            Assert.AreEqual(clanLoaded.ClanId, member.PendingInviteFromClan);
            Assert.AreEqual(clanLoaded.PendingInvites.Single(), member.BattleTag);
        }

        [Test]
        public async Task RevokeInvite()
        {
            var clan = await CreateFoundedClanForTest();

            await _handler.InviteToClan("peter#123", clan.ClanId, clan.ChiefTain);
            await _handler.RevokeInvitationToClan("peter#123", clan.ClanId, clan.ChiefTain);

            var member = await _clanRepository.LoadMemberShip("peter#123");
            var clanLoaded = await _clanRepository.LoadClan(clan.ClanId);

            Assert.AreEqual("peter#123", member.BattleTag);
            Assert.IsNull(member.PendingInviteFromClan);
            Assert.IsEmpty(clanLoaded.PendingInvites);
        }

        [Test]
        public async Task SignPetition()
        {
            var clanNameExpected = "Cool Shit";
            var clan = await _handler.CreateClan(clanNameExpected, "CS", "Peter#123");

            await _handler.InviteToClan("peter#123", clan.ClanId, "Peter#123");
            await _handler.AcceptInvite("peter#123", clan.ClanId);

            var member = await _clanRepository.LoadMemberShip("peter#123");
            var clanLoaded = await _clanRepository.LoadClan(clan.ClanId);

            Assert.AreEqual("peter#123", member.BattleTag);
            Assert.IsNull(member.PendingInviteFromClan);
            Assert.AreEqual(clanLoaded.ClanId, member.ClanId);
            Assert.IsEmpty(clanLoaded.PendingInvites);
            Assert.AreEqual("peter#123", clanLoaded.FoundingFathers[1]);
        }

        [Test]
        public async Task CreateClan()
        {
            var clanNameExpected = "Cool Shit";
            var clan = await _handler.CreateClan(clanNameExpected, "CS", "Peter#123");

            var clanLoaded = await _clanRepository.LoadClan(clan.ClanId);

            Assert.AreEqual(clan.ClanName, clanNameExpected);
            Assert.AreEqual(clan.ClanName, clanNameExpected);
            Assert.AreNotEqual(clanLoaded.ClanId, ObjectId.Empty);
        }

        [Test]
        public async Task KickMember_NotInClan()
        {
            var clan = await CreateFoundedClanForTest();

            Assert.ThrowsAsync<ValidationException>(async () => await _handler.KickPlayer(
                "NotInClan#123",
                clan.ClanId,
                clan.ChiefTain));
        }

        [Test]
        public async Task KickMember_NotChieftain()
        {
            var clan = await CreateFoundedClanForTest();

            Assert.ThrowsAsync<ValidationException>(async () => await _handler.KickPlayer(
                clan.Members[3],
                clan.ClanId,
                clan.Members[2]));
        }

        [Test]
        public async Task KickMember()
        {
            var clan = await CreateFoundedClanForTest();

            await _handler.KickPlayer(
                clan.Members[1],
                clan.ClanId,
                clan.ChiefTain);

            var clanLoaded = await _clanRepository.LoadClan(clan.ClanId);

            Assert.AreEqual(5, clanLoaded.Members.Count);
            Assert.IsFalse(clanLoaded.Members.Contains(clan.Members[1]));
        }

        [Test]
        public async Task KickMember_Cheiftain()
        {
            var clan = await CreateFoundedClanForTest();

            Assert.ThrowsAsync<ValidationException>(async () => await _handler.KickPlayer(
                clan.ChiefTain,
                clan.ClanId,
                clan.ChiefTain));
        }

        [Test]
        public async Task KickMember_Shaman()
        {
            var clan = await CreateFoundedClanForTest();

            await _handler.AddShamanToClan(
                clan.Members[1],
                clan.ClanId,
                clan.ChiefTain);

            await _handler.KickPlayer(
                clan.Members[1],
                clan.ClanId,
                clan.ChiefTain);

            var clanLoaded = await _clanRepository.LoadClan(clan.ClanId);

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

            Assert.AreEqual(founder.ClanId, clan.ClanId);
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

            await _handler.DeleteClan(clan.ClanId, clan.ChiefTain);

            var clanLoaded = await _clanRepository.LoadClan(clan.ClanId);
            Assert.IsNull(clanLoaded);


            var chieftain = await _clanRepository.LoadMemberShip(clan.ChiefTain);
            Assert.IsNull(chieftain.ClanId);

            foreach (var clanMember in clan.Members)
            {
                var member = await _clanRepository.LoadMemberShip(clanMember);
                Assert.IsNull(member.ClanId);
            }
        }

        [Test]
        public async Task ChieftainDeletesClanBeforeFinishing()
        {
            await _handler.CreateClan("Cool Shit", "CS", "Peter#123");

            await _handler.InviteToClan("Wolf#456", "CS", "Peter#123");
            await _handler.AcceptInvite("Wolf#456", "CS");
            await _handler.DeleteClan("CS", "Peter#123");

            var clan2 = await _handler.CreateClan("Cool Shit", "CS", "Wolf#456");

            Assert.AreEqual("CS", clan2.ClanId);
            Assert.AreEqual("Wolf#456", clan2.ChiefTain);
        }

        [Test]
        public async Task ChieftainDeletesClanBeforeFinishing_FoundingFatherDidNotAccept()
        {
            await _handler.CreateClan("Cool Shit", "CS", "Peter#123");

            await _handler.InviteToClan("Wolf#456", "CS", "Peter#123");
            await _handler.DeleteClan("CS", "Peter#123");

            var clan2 = await _handler.CreateClan("Cool Shit", "CS", "Wolf#456");

            Assert.AreEqual("CS", clan2.ClanId);
            Assert.AreEqual("Wolf#456", clan2.ChiefTain);
        }

        [Test]
        public async Task UserLeavesClanAndGetsInvitedAgain()
        {
            await CreateFoundedClanForTest();

            await _handler.InviteToClan("Wolf#456", "CS", "Peter#123");
            await _handler.AcceptInvite("Wolf#456", "CS");
            await _handler.LeaveClan("CS", "Wolf#456");

            await _handler.InviteToClan("Wolf#456", "CS", "Peter#123");

            var member1 = await _clanRepository.LoadMemberShip("Wolf#456");
            
            Assert.AreEqual(null, member1.ClanId);
            Assert.AreEqual("CS", member1.PendingInviteFromClan);

            await _handler.AcceptInvite("Wolf#456", "CS");

            var member = await _clanRepository.LoadMemberShip("Wolf#456");

            Assert.AreEqual("CS", member.ClanId);
            Assert.AreEqual(null, member.PendingInviteFromClan);
        }

        [Test]
        public async Task UserGetsKickedAndGetsInvitedAgain()
        {
            await CreateFoundedClanForTest();

            await _handler.InviteToClan("Wolf#456", "CS", "Peter#123");
            await _handler.AcceptInvite("Wolf#456", "CS");
            await _handler.KickPlayer("Wolf#456", "CS", "Peter#123");

            await _handler.InviteToClan("Wolf#456", "CS", "Peter#123");

            var member1 = await _clanRepository.LoadMemberShip("Wolf#456");
            
            Assert.AreEqual(null, member1.ClanId);
            Assert.AreEqual("CS", member1.PendingInviteFromClan);

            await _handler.AcceptInvite("Wolf#456", "CS");

            var member = await _clanRepository.LoadMemberShip("Wolf#456");

            Assert.AreEqual("CS", member.ClanId);
            Assert.AreEqual(null, member.PendingInviteFromClan);
        }

        [Test]
        public async Task UserWithPendingInviteGetsNoInvite()
        {
            await CreateFoundedClanForTest("AB", "Crank#123");
            await CreateFoundedClanForTest("CD", "Wolf#456");

            await _handler.InviteToClan("Merlin#123", "AB", "Crank#123");
            var exception = Assert.ThrowsAsync<ValidationException>(async () =>
                await _handler.InviteToClan(
                    "Merlin#123",
                    "CD",
                    "Wolf#456"));

            Assert.AreEqual("Player already invited to different clan", exception.Message);

            var loadMemberShip = await _clanRepository.LoadMemberShip("Merlin#123");
            var clanWithMember = await _clanRepository.LoadClan("AB");
            var clanNotWithMember = await _clanRepository.LoadClan("CD");

            Assert.AreEqual("AB", loadMemberShip.PendingInviteFromClan);
            Assert.AreEqual("Merlin#123", clanWithMember.PendingInvites.Single());
            Assert.AreEqual(0, clanNotWithMember.PendingInvites.Count);
        }

        [Test]
        public async Task UserInActiveClanGetsNoInvite()
        {
            await CreateFoundedClanForTest("AB", "Crank#123");
            await CreateFoundedClanForTest("CD", "Wolf#456");

            await _handler.InviteToClan("Merlin#123", "AB", "Crank#123");
            await _handler.AcceptInvite("Merlin#123", "AB");
            var exception = Assert.ThrowsAsync<ValidationException>(async () =>
                await _handler.InviteToClan(
                    "Merlin#123",
                    "CD",
                    "Wolf#456"));

            Assert.AreEqual("Player already part of a different clan", exception.Message);

            var loadMemberShip = await _clanRepository.LoadMemberShip("Merlin#123");
            var clanWithMember = await _clanRepository.LoadClan("AB");
            var clanNotWithMember = await _clanRepository.LoadClan("CD");

            Assert.IsNull(loadMemberShip.PendingInviteFromClan);
            Assert.AreEqual(0, clanWithMember.PendingInvites.Count);
            Assert.AreEqual(0, clanNotWithMember.PendingInvites.Count);
        }

        [Test]
        public async Task LoadClan_PopulateRanks()
        {
            var clan = await CreateFoundedClanForTest();
            await _rankRepository.UpsertSeason(new Season(0));
            await _rankRepository.UpsertSeason(new Season(1));
            await _rankRepository.InsertRanks(new List<Rank>
            {
                new Rank(new List<string> { clan.Members[0] }, 1, 5, 1500, null, GateWay.Europe, GameMode.GM_1v1, 1)
            });

            await _rankRepository.InsertLeagues(new List<LeagueConstellation>
            {
                new LeagueConstellation(1, GateWay.Europe, GameMode.GM_1v1, new List<League>
                {
                    new League(1, 2, "Wood", 5)
                })
            });

            var playerRepository = new PlayerRepository(MongoClient);
            await playerRepository.UpsertPlayerOverview(PlayerOverview.Create(new List<PlayerId>
                {
                    PlayerId.Create(clan.Members[0])
                },
                GateWay.Europe,
                GameMode.GM_1v1,
                1,
                null));

            var clanLoaded = await _handler.LoadClan(clan.ClanId);

            Assert.AreEqual(1, clanLoaded.Ranks.First().League);
            Assert.AreEqual(2, clanLoaded.Ranks.First().LeagueOrder);
            Assert.AreEqual("Wood", clanLoaded.Ranks.First().LeagueName);
            Assert.AreEqual(5, clanLoaded.Ranks.First().LeagueDivision);
        }

        private async Task<Clan> CreateFoundedClanForTest(string clanId = "CS", string warchief = "Peter#123")
        {
            var clan = await _handler.CreateClan("Cool Stuff", clanId, warchief);
            for (int i = 0; i < 6; i++)
            {
                var newGuid = Guid.NewGuid();
                await _handler.InviteToClan($"btag#{newGuid}", clan.ClanId, warchief);
                await _handler.AcceptInvite($"btag#{newGuid}", clan.ClanId);
            }

            return await _clanRepository.LoadClan(clan.ClanId);
        }
    }
}