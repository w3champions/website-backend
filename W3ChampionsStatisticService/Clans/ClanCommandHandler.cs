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

        public async Task<Clan> CreateClan(CreateClanDto clanDto)
        {
            var clan = Clan.Create(clanDto.ClanName);
            var wasSaved = await _clanRepository.TryInsertClan(clan);
            if (!wasSaved) throw new ValidationException("Clan Name allready taken");
            return clan;
        }

        public async Task<Clan> SignClanPetition(string clanId, string battleTag)
        {
            var clan = await _clanRepository.LoadClan(clanId);
            var clanMemberShip = await _clanRepository.LoadMemberShip(battleTag) ?? ClanMembership.Create(battleTag);
            clan.Sign(clanMemberShip);
            await _clanRepository.UpsertClan(clan);
            await _clanRepository.UpsertMemberShip(clanMemberShip);
            return clan;
        }

        public async Task InviteToClan(string battleTag, string clanId)
        {
            var clanMemberShip = await _clanRepository.LoadMemberShip(battleTag)
                                 ?? ClanMembership.Create(battleTag);
            var clan = await _clanRepository.LoadClan(clanId);

            if (clan == null)
            {
                throw new ValidationException("Clan not found");
            }

            clan.Invite(clanMemberShip);

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
    }
}