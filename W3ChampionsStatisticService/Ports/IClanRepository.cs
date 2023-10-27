using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using W3ChampionsStatisticService.Clans;

namespace W3ChampionsStatisticService.Ports;

public interface IClanRepository
{
    Task<bool> TryInsertClan(Clan clan);
    Task<Clan> LoadClan(string clanId);
    Task UpsertClan(Clan clan);
    Task<ClanMembership> LoadMemberShip(string battleTag);
    Task UpsertMemberShip(ClanMembership clanMemberShip);
    Task DeleteClan(string clanId);
    Task<List<ClanMembership>> LoadMemberShips(List<string> clanMembers);
    Task<List<ClanMembership>> LoadMemberShipsSince(DateTimeOffset from);
    Task SaveMemberShips(List<ClanMembership> clanMembers);
}
