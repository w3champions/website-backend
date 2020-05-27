using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using W3ChampionsStatisticService.Ports;

namespace W3ChampionsStatisticService.Clans
{
    public class ClanCommandHandler

    {
        private readonly IClanRepository _clanRepository;

        public ClanCommandHandler(IClanRepository clanRepository)
        {
            _clanRepository = clanRepository;
        }

        public async Task<Clan> CreateClan(string clanName, string battleTagOfFounder)
        {
            var memberShip = await _clanRepository.LoadMemberShip(battleTagOfFounder) ?? ClanMembership.Create(battleTagOfFounder);
            var clan = Clan.Create(clanName, memberShip);
            var wasSaved = await _clanRepository.TryInsertClan(clan);
            if (!wasSaved) throw new ValidationException("Clan Name allready taken");
            memberShip.SignForClan(clan);
            await _clanRepository.UpsertMemberShip(memberShip);
            return clan;
        }

        public async Task<Clan> SignClanPetition(string battleTag, string clanId)
        {
            var clan = await _clanRepository.LoadClan(clanId);
            var clanMemberShip = await _clanRepository.LoadMemberShip(battleTag) ?? ClanMembership.Create(battleTag);
            clan.Sign(clanMemberShip);
            await _clanRepository.UpsertClan(clan);
            await _clanRepository.UpsertMemberShip(clanMemberShip);
            return clan;
        }

        public async Task InviteToClan(string battleTag, string clanId, string personWhoInvitesBattleTag)
        {
            var clanMemberShip = await _clanRepository.LoadMemberShip(battleTag)
                                 ?? ClanMembership.Create(battleTag);
            var clan = await _clanRepository.LoadClan(clanId);

            if (clan == null)
            {
                throw new ValidationException("Clan not found");
            }

            clan.Invite(clanMemberShip, personWhoInvitesBattleTag);

            await _clanRepository.UpsertClan(clan);
            await _clanRepository.UpsertMemberShip(clanMemberShip);
        }

        public async Task<Clan> AddMember(string clanId, string playerBattleTag)
        {
            var clan = await _clanRepository.LoadClan(clanId);
            var clanMemberShip = await _clanRepository.LoadMemberShip(playerBattleTag) ?? ClanMembership.Create(playerBattleTag);
            clan.AddMember(clanMemberShip);
            await _clanRepository.UpsertClan(clan);
            await _clanRepository.UpsertMemberShip(clanMemberShip);
            return clan;
        }

        public async Task DeleteClan(string clanId)
        {
            var clan = await _clanRepository.LoadClan(clanId);
            await _clanRepository.DeleteClan(clanId);

            foreach (var member in clan.Members)
            {
                var memberOfDb = await _clanRepository.LoadMemberShip(member);
                memberOfDb.ExitClan();
                await _clanRepository.UpsertMemberShip(memberOfDb);
            }
        }
    }
}