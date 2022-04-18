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

namespace WC3ChampionsStatisticService.Tests
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
            var exception = Assert.ThrowsAsync<ValidationException>(async () =>
                await _handler.InviteToClan("peter#123", ObjectId.GenerateNewId().ToString(), "doesNotMatter"));
            Assert.AreEqual("Clan not found", exception.Message);
        }

        [Test]
        public async Task InvitePlayer_ThatIsAlreadyAMember()
        {
            var clan = await _handler.CreateClan("egal", "CS", "Peter#123");

            var exception = Assert.ThrowsAsync<ValidationException>(async () => await _handler.InviteToClan("Peter#123", clan.ClanId, "Peter#123"));
            Assert.AreEqual("Can not invite player who is already a clan member", exception.Message);
        }

        [Test]
        public async Task PromoteToShaman()
        {
            var clan = await CreateFoundedClanForTest();

            await _handler.AddShamanToClan(clan.Members[2], clan.ClanId, clan.ChiefTain);

            var clanLoaded = await _clanRepository.LoadClan(clan.ClanId);

            Assert.IsTrue(clanLoaded.Members.Contains(clan.Members[2]));
            Assert.AreEqual(clan.Members[2], clanLoaded.Shamans.Single());
        }

        [Test]
        public async Task LeaveAsChieftain()
        {
            var clan = await CreateFoundedClanForTest();

            var exception = Assert.ThrowsAsync<ValidationException>(async () =>
                await _handler.LeaveClan(clan.ClanId, clan.ChiefTain));
            Assert.AreEqual("Chieftain can not leave clan, transfer ownership first", exception.Message);
        }

        [Test]
        public async Task LeavesAsFounder_NotFoundedClan_RemovesMemberAndFounder()
        {
            var clan = await _handler.CreateClan("Cool Shit", "CS", "Peter#123");
            var leaver = $"btag#{Guid.NewGuid()}";
            await _handler.InviteToClan(leaver, clan.ClanId, clan.ChiefTain);
            await _handler.AcceptInvite(leaver, clan.ClanId);
            var clanLoaded = await _clanRepository.LoadClan(clan.ClanId);
            Assert.IsTrue(clanLoaded.FoundingFathers.Contains(leaver));
            Assert.IsTrue(clanLoaded.Members.Contains(leaver));

            await _handler.LeaveClan(clan.ClanId, leaver);
            clanLoaded = await _clanRepository.LoadClan(clan.ClanId);

            Assert.IsFalse(clanLoaded.FoundingFathers.Contains(leaver));
            Assert.IsFalse(clanLoaded.Members.Contains(leaver));
        }

        [Test]
        public async Task LeavesAsFounder_FoundedClan_RemovesMember_StaysFounder()
        {
            var clan = await CreateFoundedClanForTest();
            var leaver = clan.Members[1];
            await _handler.LeaveClan(clan.ClanId, leaver);
            var clanLoaded = await _clanRepository.LoadClan(clan.ClanId);

            Assert.IsTrue(clanLoaded.FoundingFathers.Contains(leaver));
            Assert.IsFalse(clanLoaded.Members.Contains(leaver));
        }

        [Test]
        public async Task SwitchChieftain()
        {
            var clan = await CreateFoundedClanForTest();

            var newChieftain = clan.Members[1];
            await _handler.AddShamanToClan(newChieftain, clan.ClanId, clan.ChiefTain);
            await _handler.SwitchChieftain(newChieftain, clan.ClanId, clan.ChiefTain);

            var clanLoaded = await _clanRepository.LoadClan(clan.ClanId);

            Assert.AreEqual(clanLoaded.Shamans.Single(), clan.ChiefTain);
            Assert.AreEqual(clanLoaded.ChiefTain, newChieftain);
            Assert.IsTrue(clanLoaded.Members.Contains(clan.ChiefTain));
            Assert.IsTrue(clanLoaded.Members.Contains(newChieftain));
        }

        [Test]
        public async Task SwitchChieftain_NotChieftainActing()
        {
            var clan = await CreateFoundedClanForTest();

            var exception = Assert.ThrowsAsync<ValidationException>(async () => await _handler.SwitchChieftain(
                clan.Members[1],
                clan.ClanId,
                clan.Members[1]));
            Assert.AreEqual("Only Chieftain can switch to new Chieftain", exception.Message);
        }

        [Test]
        public async Task SwitchChieftain_NewChieftainNotInClan()
        {
            var clan = await CreateFoundedClanForTest();

            var exception = Assert.ThrowsAsync<ValidationException>(async () => await _handler.SwitchChieftain(
                "NotInClan#123",
                clan.ClanId,
                clan.ChiefTain));
            Assert.AreEqual("Only Shaman can be promoted to Chieftain", exception.Message);
        }

        [Test]
        public async Task DemoteShaman()
        {
            var clan = await CreateFoundedClanForTest();

            await _handler.AddShamanToClan(clan.Members[2], clan.ClanId, clan.ChiefTain);
            await _handler.RemoveShamanFromClan(clan.Members[2], clan.ClanId, clan.ChiefTain);

            var clanLoaded = await _clanRepository.LoadClan(clan.ClanId);

            Assert.IsEmpty(clanLoaded.Shamans);
            Assert.IsTrue(clanLoaded.Members.Contains(clan.Members[2]));
        }

        [Test]
        public async Task PromoteShamanThatISNotInClan_Fails()
        {
            var clan = await CreateFoundedClanForTest();

            var exception = Assert.ThrowsAsync<ValidationException>(async () => await _handler.AddShamanToClan("NotInChal#123", clan.ClanId, clan.ChiefTain));
            Assert.AreEqual("Shaman has to be in clan", exception.Message);
        }

        [Test]
        public async Task PromoteShamanThatIsChieftain_Fails()
        {
            var clan = await CreateFoundedClanForTest();

            var exception = Assert.ThrowsAsync<ValidationException>(async () => await _handler.AddShamanToClan(clan.ChiefTain, clan.ClanId, clan.ChiefTain));
            Assert.AreEqual("Chieftain can not be made Shaman", exception.Message);
        }

        [Test]
        public async Task InvitePlayer_NotFoundedClan_PlayerRejects_IsNotAddedToFoundingFathersNorMembers()
        {
            var clan = await _handler.CreateClan("egal", "CS", "Peter#123");
            await _handler.InviteToClan("NewGUY#123", clan.ClanId, "Peter#123");
            await _handler.RevokeInvitationToClan("NewGUY#123", clan.ClanId, "Peter#123");

            var clanLoaded = await _clanRepository.LoadClan(clan.ClanId);

            Assert.AreEqual(1, clanLoaded.FoundingFathers.Count);
            Assert.AreEqual("Peter#123", clanLoaded.FoundingFathers[0]);
            Assert.IsFalse(clanLoaded.Members.Contains("NewGUY#123"));
        }

        [Test]
        public async Task InvitePlayer_NotFoundedClan_PlayerAccepts_IsAddedToFoundingFathersAndMembers()
        {
            var clan = await _handler.CreateClan("egal", "CS", "Peter#123");
            await _handler.InviteToClan("NewGUY#123", clan.ClanId, "Peter#123");
            await _handler.AcceptInvite("NewGUY#123", clan.ClanId);

            var clanLoaded = await _clanRepository.LoadClan(clan.ClanId);

            Assert.AreEqual(2, clanLoaded.FoundingFathers.Count);
            Assert.AreEqual("NewGUY#123", clanLoaded.FoundingFathers[1]);
            Assert.IsTrue(clanLoaded.Members.Contains("NewGUY#123"));
        }

        [Test]
        public async Task InvitePlayer_FoundedClan_PlayerAccepts_IsAddedOnlyToMembers()
        {
            var clan = await CreateFoundedClanForTest();
            await _handler.InviteToClan("NewGUY#123", clan.ClanId, "Peter#123");
            await _handler.AcceptInvite("NewGUY#123", clan.ClanId);

            var clanLoaded = await _clanRepository.LoadClan(clan.ClanId);

            Assert.IsFalse(clanLoaded.FoundingFathers.Contains("NewGUY#123"));
            Assert.IsTrue(clanLoaded.Members.Contains("NewGUY#123"));
        }

        [Test]
        public async Task InvitePlayer_FoundedClan_PlayerRejects_IsNotAddedToFoundingFathersNorMembers()
        {
            var clan = await CreateFoundedClanForTest();
            await _handler.InviteToClan("NewGUY#123", clan.ClanId, "Peter#123");
            await _handler.RevokeInvitationToClan("NewGUY#123", clan.ClanId, "Peter#123");

            var clanLoaded = await _clanRepository.LoadClan(clan.ClanId);

            Assert.IsFalse(clanLoaded.FoundingFathers.Contains("NewGUY#123"));
            Assert.IsFalse(clanLoaded.Members.Contains("NewGUY#123"));
        }

        [Test]
        public async Task InvitePlayer_ThatHasAlreadySigned()
        {
            var clan = await _handler.CreateClan("egal", "CS", "Peter#123");
            await _handler.InviteToClan("NewGUY#123", clan.ClanId, "Peter#123");
            await _handler.AcceptInvite("NewGUY#123", clan.ClanId);

            var exception = Assert.ThrowsAsync<ValidationException>(async () => await _handler.InviteToClan("NewGUY#123", clan.ClanId, "Peter#123"));
            Assert.AreEqual("Can not invite player who is already a clan member", exception.Message);
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
            Assert.IsTrue(clanLoaded.Members.Contains(member.BattleTag));
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

            var exception = Assert.ThrowsAsync<ValidationException>(async () => await _handler.KickPlayer(
                 "NotInClan#123",
                 clan.ClanId,
                 clan.ChiefTain));
            Assert.AreEqual("Clan or member not found", exception.Message);
        }

        [Test]
        public async Task KickMember_InAnotherClan()
        {
            var clan = await CreateFoundedClanForTest();
            var anotherClan = await _handler.CreateClan("Another Clan", "AS", "Name#123");
            var exception = Assert.ThrowsAsync<ValidationException>(async () => await _handler.KickPlayer(
                 "Name#123",
                 clan.ClanId,
                 clan.ChiefTain));
            Assert.AreEqual("Player not in this clan", exception.Message);
        }

        [Test]
        public async Task KickMember_NotChieftain()
        {
            var clan = await CreateFoundedClanForTest();

            var exception = Assert.ThrowsAsync<ValidationException>(async () => await _handler.KickPlayer(
                clan.Members[3],
                clan.ClanId,
                clan.Members[2]));
            Assert.AreEqual("Only Chieftain or shamans can kick players", exception.Message);
        }

        [Test]
        public async Task KickMember()
        {
            var clan = await CreateFoundedClanForTest();
            var memberTag = $"btag#{Guid.NewGuid()}";
            await _handler.InviteToClan(memberTag, clan.ClanId, clan.ChiefTain);
            await _handler.AcceptInvite(memberTag, clan.ClanId);
            await _handler.KickPlayer(
                memberTag,
                clan.ClanId,
                clan.ChiefTain);

            var clanLoaded = await _clanRepository.LoadClan(clan.ClanId);
            Assert.IsFalse(clanLoaded.Members.Contains(memberTag));
        }

        [Test]
        public async Task KickFounder_NotFoundedClan_RemovesMemberAndFounder()
        {
            var clan = await _handler.CreateClan("Cool Shit", "CS", "Peter#123");
            var memberTag = $"btag#{Guid.NewGuid()}";
            await _handler.InviteToClan(memberTag, clan.ClanId, clan.ChiefTain);
            await _handler.AcceptInvite(memberTag, clan.ClanId);
            var clanLoaded = await _clanRepository.LoadClan(clan.ClanId);
            Assert.IsTrue(clanLoaded.FoundingFathers.Contains(memberTag));
            Assert.IsTrue(clanLoaded.Members.Contains(memberTag));

            await _handler.KickPlayer(
                memberTag,
                clan.ClanId,
                clan.ChiefTain);

            clanLoaded = await _clanRepository.LoadClan(clan.ClanId);
            Assert.IsFalse(clanLoaded.FoundingFathers.Contains(memberTag));
            Assert.IsFalse(clanLoaded.Members.Contains(memberTag));
        }

        [Test]
        public async Task KickFounder_FoundedClan_RemovesMember_StaysFounder()
        {
            var clan = await CreateFoundedClanForTest();

            await _handler.KickPlayer(
                clan.Members[1],
                clan.ClanId,
                clan.ChiefTain);

            var clanLoaded = await _clanRepository.LoadClan(clan.ClanId);

            Assert.AreEqual(6, clanLoaded.Members.Count);
            Assert.IsTrue(clanLoaded.FoundingFathers.Contains(clan.Members[1]));
            Assert.IsFalse(clanLoaded.Members.Contains(clan.Members[1]));
        }

        [Test]
        public async Task KickMember_Cheiftain()
        {
            var clan = await CreateFoundedClanForTest();

            var exception = Assert.ThrowsAsync<ValidationException>(async () => await _handler.KickPlayer(
                clan.ChiefTain,
                clan.ClanId,
                clan.ChiefTain));
            Assert.AreEqual("Can not kick chieftain", exception.Message);
        }

        [Test]
        public async Task KickMember_Shaman()
        {
            var clan = await CreateFoundedClanForTest();
            var memberTag = $"btag#{Guid.NewGuid()}";
            await _handler.InviteToClan(memberTag, clan.ClanId, clan.ChiefTain);
            await _handler.AcceptInvite(memberTag, clan.ClanId);

            await _handler.AddShamanToClan(
                memberTag,
                clan.ClanId,
                clan.ChiefTain);

            await _handler.KickPlayer(
                memberTag,
                clan.ClanId,
                clan.ChiefTain);

            var clanLoaded = await _clanRepository.LoadClan(clan.ClanId);

            Assert.AreEqual(0, clanLoaded.Shamans.Count);
            Assert.IsFalse(clanLoaded.Members.Contains(memberTag));
            Assert.IsFalse(clanLoaded.Shamans.Contains(memberTag));
        }

        [Test]
        public async Task KickMember_ShamanFounder_RemovesMember_StaysFounder()
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
            Assert.IsTrue(clanLoaded.FoundingFathers.Contains(clan.Members[1]));
            Assert.IsFalse(clanLoaded.Members.Contains(clan.Members[1]));
            Assert.IsFalse(clanLoaded.Shamans.Contains(clan.Members[1]));
        }

        [Test]
        public async Task CreateClanWithSameNameNotPossible()
        {
            var clanNameExpected = "Cool Shit";
            await _handler.CreateClan(clanNameExpected, "CS", "Peter#123");

            var exception = Assert.ThrowsAsync<ValidationException>(async () =>
                await _handler.CreateClan(clanNameExpected, "CS1", "John#456"));
            Assert.AreEqual("Clan Name or Abbreviation already taken", exception.Message);
        }

        [Test]
        public async Task CreateClanWithSameAbbreviationNotPossible()
        {
            var clanAbbrevation = "CS";
            await _handler.CreateClan("Clan Name 1", clanAbbrevation, "Peter#123");

            var exception = Assert.ThrowsAsync<ValidationException>(async () =>
                await _handler.CreateClan("Clan Name 2", clanAbbrevation, "John#456"));
            Assert.AreEqual("Clan Name or Abbreviation already taken", exception.Message);
        }

        [Test]
        public async Task CreateClan_FounderGetsCreated()
        {
            var clanNameExpected = "Cool Shit";
            var clan = await _handler.CreateClan(clanNameExpected, "CS", "Peter#123");

            var member = await _clanRepository.LoadMemberShip("Peter#123");
            var loadedClan = await _clanRepository.LoadClan(member.ClanId);

            Assert.AreEqual(member.ClanId, clan.ClanId);
            Assert.AreEqual(member.BattleTag, "Peter#123");
            Assert.IsTrue(loadedClan.FoundingFathers.Contains(member.BattleTag));
        }

        [Test]
        public async Task CreateClan_FoundingWhenInOtherClanIsProhibited()
        {
            await _handler.CreateClan("Cool Shit", "CS", "Peter#123");

            var exception = Assert.ThrowsAsync<ValidationException>(async () => await _handler.CreateClan("Cool Shit NEW", "CS", "Peter#123"));
            Assert.AreEqual("Founder can not be in another clan", exception.Message);
        }

        [Test]
        public async Task CreateClan_BeingFounderOfMultipleClansIsAllowed_ButMemberInOneOnlyAtGivenTime()
        {
            var firstClan = await CreateFoundedClanForTest();
            var founder = firstClan.FoundingFathers[1];
            await _handler.KickPlayer(firstClan.FoundingFathers[1], firstClan.ClanId, firstClan.ChiefTain);

            var secondClan = await _handler.CreateClan("Second clan", "SC", founder);

            var firstClanLoaded = await _clanRepository.LoadClan(firstClan.ClanId);
            var secondClanLoaded = await _clanRepository.LoadClan(secondClan.ClanId);
            Assert.IsTrue(firstClanLoaded.FoundingFathers.Contains(founder));
            Assert.IsFalse(firstClanLoaded.Members.Contains(founder));
            Assert.IsTrue(secondClanLoaded.FoundingFathers.Contains(founder));
            Assert.IsTrue(secondClanLoaded.Members.Contains(founder));
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

            Assert.IsTrue(clan.Members.Contains(chieftain.BattleTag));
            Assert.IsTrue(clan.Shamans.All(s => clan.Members.Contains(s)));

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
            await CreateFoundedClanForTest("Clan1", "AB", "Crank#123");
            await CreateFoundedClanForTest("Clan2", "CD", "Wolf#456");

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
            await CreateFoundedClanForTest("Clan1", "AB", "Crank#123");
            await CreateFoundedClanForTest("Clan2", "CD", "Wolf#456");

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
        public async Task UserShamanInActiveClanGetsNoInvite()
        {
            var clan1 = await CreateFoundedClanForTest("Clan1", "AB", "Crank#123");
            var clan2 = await CreateFoundedClanForTest("Clan2", "CD", "Wolf#456");
            await _handler.AddShamanToClan(clan1.Members[2], clan1.ClanId, clan1.ChiefTain);

            var exception = Assert.ThrowsAsync<ValidationException>(async () => await _handler.InviteToClan(clan1.Members[2], clan2.ClanId, "Wolf#456"));
            Assert.AreEqual("Player already part of a different clan", exception.Message);

            var loadMemberShip = await _clanRepository.LoadMemberShip(clan1.Members[2]);
            var clanWithMember = await _clanRepository.LoadClan("AB");
            var clanNotWithMember = await _clanRepository.LoadClan("CD");

            Assert.IsNull(loadMemberShip.PendingInviteFromClan);
            Assert.AreEqual(0, clanWithMember.PendingInvites.Count);
            Assert.AreEqual(0, clanNotWithMember.PendingInvites.Count);
        }

        [Test]
        public async Task FounderLeavesClanAndRejoins()
        {
            var clan = await CreateFoundedClanForTest();
            var founder = clan.Members[1];
            await _handler.LeaveClan(clan.ClanId, founder);
            var loadedClan = await _clanRepository.LoadClan(clan.ClanId);
            Assert.IsFalse(loadedClan.Members.Contains(founder));

            Assert.DoesNotThrowAsync(async () =>
            {
                await _handler.InviteToClan(
                    founder,
                    clan.ClanId,
                    clan.ChiefTain);
                await _handler.AcceptInvite(founder, clan.ClanId);
            }, "Re-joining as founder should be possible");

            loadedClan = await _clanRepository.LoadClan(clan.ClanId);
            Assert.IsTrue(loadedClan.Members.Contains(founder));
        }
        [Test]
        public async Task FounderLeavesClanAndRejoins_NotFoundedClan()
        {
            var clan = await _handler.CreateClan("Cool Shit", "CS", "Peter#123");
            var founder = $"btag#{Guid.NewGuid()}";
            await _handler.InviteToClan(founder, clan.ClanId, clan.ChiefTain);
            await _handler.AcceptInvite(founder, clan.ClanId);
            var loadedClan = await _clanRepository.LoadClan(clan.ClanId);
            Assert.IsTrue(loadedClan.FoundingFathers.Contains(founder));
            Assert.IsTrue(loadedClan.Members.Contains(founder));

            await _handler.LeaveClan(clan.ClanId, founder);
            loadedClan = await _clanRepository.LoadClan(clan.ClanId);
            Assert.IsFalse(loadedClan.FoundingFathers.Contains(founder));
            Assert.IsFalse(loadedClan.Members.Contains(founder));

            Assert.DoesNotThrowAsync(async () =>
            {
                await _handler.InviteToClan(
                    founder,
                    clan.ClanId,
                    clan.ChiefTain);
                await _handler.AcceptInvite(founder, clan.ClanId);
            }, "Re-joining as founder should be possible");

            loadedClan = await _clanRepository.LoadClan(clan.ClanId);
            Assert.IsTrue(loadedClan.FoundingFathers.Contains(founder));
            Assert.IsTrue(loadedClan.Members.Contains(founder));
        }

        [Test]
        public async Task FounderGetKickedAndRejoins()
        {
            var clan = await CreateFoundedClanForTest();
            var founder = clan.Members[1];
            await _handler.KickPlayer(founder, clan.ClanId, clan.ChiefTain);
            var loadedClan = await _clanRepository.LoadClan(clan.ClanId);
            Assert.IsFalse(loadedClan.Members.Contains(founder));

            Assert.DoesNotThrowAsync(async () =>
            {
                await _handler.InviteToClan(
                    founder,
                    clan.ClanId,
                    clan.ChiefTain);
                await _handler.AcceptInvite(founder, clan.ClanId);
            }, "Re-joining as founder should be possible");

            loadedClan = await _clanRepository.LoadClan(clan.ClanId);
            Assert.IsTrue(loadedClan.Members.Contains(founder));
        }

        [Test]
        public async Task FounderGetKickedAndRejoins_NotFoundedClan()
        {
            var clan = await _handler.CreateClan("Cool Shit", "CS", "Peter#123");
            var founder = $"btag#{Guid.NewGuid()}";
            await _handler.InviteToClan(founder, clan.ClanId, clan.ChiefTain);
            await _handler.AcceptInvite(founder, clan.ClanId);
            var loadedClan = await _clanRepository.LoadClan(clan.ClanId);
            Assert.IsTrue(loadedClan.FoundingFathers.Contains(founder));
            Assert.IsTrue(loadedClan.Members.Contains(founder));

            await _handler.KickPlayer(founder, clan.ClanId, clan.ChiefTain);
            loadedClan = await _clanRepository.LoadClan(clan.ClanId);
            Assert.IsFalse(loadedClan.FoundingFathers.Contains(founder));
            Assert.IsFalse(loadedClan.Members.Contains(founder));

            Assert.DoesNotThrowAsync(async () =>
            {
                await _handler.InviteToClan(
                    founder,
                    clan.ClanId,
                    clan.ChiefTain);
                await _handler.AcceptInvite(founder, clan.ClanId);
            }, "Re-joining as founder should be possible");

            loadedClan = await _clanRepository.LoadClan(clan.ClanId);
            Assert.IsTrue(loadedClan.FoundingFathers.Contains(founder));
            Assert.IsTrue(loadedClan.Members.Contains(founder));
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

        private async Task<Clan> CreateFoundedClanForTest(string clanName = "Cool Stuff", string clanId = "CS", string warchief = "Peter#123")
        {
            var clan = await _handler.CreateClan(clanName, clanId, warchief);
            var membersId = new List<string>();
            for (int i = 0; i < 6; i++)
            {
                var newGuid = Guid.NewGuid();
                var tag = $"btag#{newGuid}";
                membersId.Add(tag);
                await _handler.InviteToClan(tag, clan.ClanId, warchief);
                await _handler.AcceptInvite(tag, clan.ClanId);
            }
            var loadedClan = await _clanRepository.LoadClan(clan.ClanId);
            Assert.IsTrue(loadedClan.Members.Contains(warchief));
            Assert.IsTrue(loadedClan.FoundingFathers.Contains(warchief));
            Assert.IsTrue(membersId.All(id => loadedClan.Members.Contains(id)));
            Assert.IsTrue(membersId.All(id => loadedClan.FoundingFathers.Contains(id)));
            return loadedClan;
        }
    }
}