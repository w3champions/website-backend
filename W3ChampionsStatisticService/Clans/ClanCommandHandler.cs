using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using W3ChampionsStatisticService.Ladder;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.Services;

namespace W3ChampionsStatisticService.Clans
{
    public class ClanCommandHandler

    {
        private readonly IClanRepository _clanRepository;
        private readonly TrackingService _trackingService;
        private readonly IRankRepository _rankRepository;

        public ClanCommandHandler(IClanRepository clanRepository,
            IRankRepository rankRepository,
            TrackingService trackingService)
        {
            _clanRepository = clanRepository;
            _trackingService = trackingService;
            _rankRepository = rankRepository;
        }

        public async Task<Clan> CreateClan(string clanName, string clanAbbrevation, string battleTagOfFounder)
        {
            var memberShip = await _clanRepository.LoadMemberShip(battleTagOfFounder) ?? ClanMembership.Create(battleTagOfFounder);
            var clan = Clan.Create(clanName, clanAbbrevation, memberShip);
            var wasSaved = await _clanRepository.TryInsertClan(clan);
            if (!wasSaved) throw new ValidationException("Clan Name allready taken");
            memberShip.ClanId = clan.ClanId;
            memberShip.ClanName = clan.ClanName;
            await _clanRepository.UpsertMemberShip(memberShip);
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

        public async Task<Clan> AcceptInvite(string playerBattleTag, string clanId)
        {
            var clan = await _clanRepository.LoadClan(clanId);
            var clanMemberShip = await _clanRepository.LoadMemberShip(playerBattleTag) ?? ClanMembership.Create(playerBattleTag);
            clan.AcceptInvite(clanMemberShip);
            await _clanRepository.UpsertClan(clan);
            await _clanRepository.UpsertMemberShip(clanMemberShip);
            return clan;
        }

        public async Task DeleteClan(string clanId, string actingPlayer)
        {
            var clan = await _clanRepository.LoadClan(clanId);
            if (clan.ChiefTain != actingPlayer) throw new ValidationException("Only Chieftain can delete the clan");
            
            await _clanRepository.DeleteClan(clanId);

            clan.Members.Add(clan.ChiefTain);
            var memberShips = await _clanRepository.LoadMemberShips(clan.Members);

            foreach (var member in memberShips)
            {
                member.LeaveClan();
            }

            await _clanRepository.SaveMemberShips(memberShips);
        }

        public async Task<Clan> GetClanForPlayer(string battleTag)
        {
            var membership = await _clanRepository.LoadMemberShip(battleTag);
            if (membership?.ClanId != null)
            {
                var clan = await LoadClan(membership.ClanId);
                return clan;
            }

            return null;
        }

        public async Task RevokeInvitationToClan(string battleTag, string clanId, string personWhoInvitesBattleTag)
        {
            var clanMemberShip = await _clanRepository.LoadMemberShip(battleTag);
            var clan = await _clanRepository.LoadClan(clanId);

            if (clan == null || clanMemberShip == null) throw new ValidationException("Clan or member not found");

            clan.RevokeInvite(clanMemberShip, personWhoInvitesBattleTag);

            await _clanRepository.UpsertClan(clan);
            await _clanRepository.UpsertMemberShip(clanMemberShip);
        }

        public async Task<Clan> RejectInvite(string clanId, string battleTag)
        {
            var clanMemberShip = await _clanRepository.LoadMemberShip(battleTag);
            var clan = await _clanRepository.LoadClan(clanId);

            if (clan == null || clanMemberShip == null) throw new ValidationException("Clan or member not found");

            clan.RejectInvite(clanMemberShip);

            await _clanRepository.UpsertClan(clan);
            await _clanRepository.UpsertMemberShip(clanMemberShip);

            return clan;
        }

        public async Task<Clan> LeaveClan(string clanId, string battleTag)
        {
            var clanMemberShip = await _clanRepository.LoadMemberShip(battleTag);
            var clan = await _clanRepository.LoadClan(clanId);

            if (clan == null || clanMemberShip == null) throw new ValidationException("Clan or member not found");

            clan.LeaveClan(clanMemberShip);

            await _clanRepository.UpsertClan(clan);
            await _clanRepository.UpsertMemberShip(clanMemberShip);

            return clan;
        }

        public async Task<Clan> RemoveShamanFromClan(string shamanId, string clanId, string actingPlayer)
        {
            var clan = await _clanRepository.LoadClan(clanId);

            if (clan == null) throw new ValidationException("Clan not found");

            clan.RemoveShaman(shamanId, actingPlayer);

            await _clanRepository.UpsertClan(clan);

            return clan;
        }

        public async Task<Clan> AddShamanToClan(string shamanId, string clanId, string actingPlayer)
        {
            var clan = await _clanRepository.LoadClan(clanId);

            if (clan == null) throw new ValidationException("Clan not found");

            clan.AddShaman(shamanId, actingPlayer);

            await _clanRepository.UpsertClan(clan);

            return clan;
        }

        public async Task<Clan> KickPlayer(string battleTag, string clanId, string actingPlayer)
        {
            var clanMemberShip = await _clanRepository.LoadMemberShip(battleTag);
            var clan = await _clanRepository.LoadClan(clanId);

            if (clan == null || clanMemberShip == null) throw new ValidationException("Clan or member not found");

            clan.KickPlayer(clanMemberShip, actingPlayer);

            await _clanRepository.UpsertMemberShip(clanMemberShip);
            await _clanRepository.UpsertClan(clan);

            return clan;
        }

        public async Task<Clan> SwitchChieftain(string newChieftain, string clanId, string actingPlayer)
        {
            var clan = await _clanRepository.LoadClan(clanId);

            if (clan == null) throw new ValidationException("Clan not found");

            clan.SwitchChieftain(newChieftain, actingPlayer);

            await _clanRepository.UpsertClan(clan);

            return clan;
        }

        public async Task<Clan> LoadClan(string clanId)
        {
            var clan = await _clanRepository.LoadClan(clanId);
            var seasons = await _rankRepository.LoadSeasons();
            var season = seasons.Max(s => s.Id);
            var leagueConstellation = await _rankRepository.LoadLeagueConstellation(season);

            var list = new List<string>();
            list.AddRange(clan.Members);
            list.AddRange(clan.Shamans);
            list.Add(clan.ChiefTain);
            var ranksFromClan = await _rankRepository.LoadRanksForPlayers(list, season);

            PopulateLeague(ranksFromClan, leagueConstellation);

            clan.Ranks = ranksFromClan.ToList();

            return clan;
        }

        private void PopulateLeague(
            List<Rank> ranks,
            List<LeagueConstellation> allLeagues)
        {
            foreach (var rank in ranks)
            {
                try
                {
                    var leagueConstellation = allLeagues.Single(l => l.Gateway == rank.Gateway && l.Season == rank.Season && l.GameMode == rank.GameMode);
                    var league = leagueConstellation.Leagues.Single(l => l.Id == rank.League);

                    var rankOfPlayer = ranks.SingleOrDefault(g => g.Id == rank.Id);
                    if (rankOfPlayer == null) return;

                    rankOfPlayer.LeagueName = league.Name;
                    rankOfPlayer.LeagueDivision = league.Division;
                    rankOfPlayer.LeagueOrder = league.Order;

                    rankOfPlayer.RankingPoints = rankOfPlayer.RankingPoints;
                    rankOfPlayer.League = rankOfPlayer.League;
                    rankOfPlayer.RankNumber = rankOfPlayer.RankNumber;
                }
                catch (Exception e)
                {
                    _trackingService.TrackException(e, $"A League was not found for {rank.Id} RN: {rank.RankNumber} LE:{rank.League}");
                }
            }
        }
    }
}