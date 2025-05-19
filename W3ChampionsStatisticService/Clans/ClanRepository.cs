using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using W3C.Domain.Repositories;
using W3ChampionsStatisticService.Ports;

namespace W3ChampionsStatisticService.Clans;

public class ClanRepository(MongoClient mongoClient) : MongoDbRepositoryBase(mongoClient), IClanRepository
{
    public async Task TryInsertClan(Clan clan)
    {
        var clanFoundById = await LoadFirst<Clan>(c => c.ClanId == clan.ClanId);
        if (clanFoundById != null) throw new ValidationException($"Clan abbreviation already taken: {clan.ClanId}");
        var clanFoundByName = await LoadFirst<Clan>(c => c.ClanName == clan.ClanName);
        if (clanFoundByName != null) throw new ValidationException($"Clan name already taken: {clan.ClanName}");
        await Insert(clan);
    }

    public Task UpsertClan(Clan clan)
    {
        return Upsert(clan, c => c.ClanId == clan.ClanId);
    }

    public Task<Clan> LoadClan(string clanId)
    {
        return LoadFirst<Clan>(l => l.ClanId == clanId);
    }

    public Task<ClanMembership> LoadMemberShip(string battleTag)
    {
        return LoadFirst<ClanMembership>(m => m.BattleTag == battleTag);
    }

    public Task UpsertMemberShip(ClanMembership clanMemberShip)
    {
        return UpsertTimed(clanMemberShip, c => c.BattleTag == clanMemberShip.BattleTag);
    }

    public Task DeleteClan(string clanId)
    {
        return Delete<Clan>(c => c.ClanId == clanId);
    }

    public Task<List<ClanMembership>> LoadMemberShips(List<string> clanMembers)
    {
        return LoadAll<ClanMembership>(m => clanMembers.Contains(m.BattleTag));
    }

    public Task<List<ClanMembership>> LoadMemberShipsSince(DateTimeOffset from)
    {
        return LoadSince<ClanMembership>(from);
    }

    public Task SaveMemberShips(List<ClanMembership> clanMembers)
    {
        return UpsertMany(clanMembers);
    }
}
