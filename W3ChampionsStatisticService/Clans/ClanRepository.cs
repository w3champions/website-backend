using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using W3C.Domain.Repositories;
using W3ChampionsStatisticService.Ports;

namespace W3ChampionsStatisticService.Clans;

public class ClanRepository : MongoDbRepositoryBase, IClanRepository
{
    public async Task TryInsertClan(Clan clan)
    {
        var clanFoundById = await LoadFirst(Builders<Clan>.Filter.Eq(c => c.ClanId, clan.ClanId));
        if (clanFoundById != null) throw new ValidationException($"Clan abbreviation already taken: {clan.ClanId}");
        var clanFoundByName = await LoadFirst(Builders<Clan>.Filter.Eq(c => c.ClanName, clan.ClanName));
        if (clanFoundByName != null) throw new ValidationException($"Clan name already taken: {clan.ClanName}");
        await Insert(clan);
    }

    public Task UpsertClan(Clan clan)
    {
        return Upsert(clan, Builders<Clan>.Filter.Eq(c => c.ClanId, clan.ClanId));
    }

    public Task<Clan> LoadClan(string clanId)
    {
        return LoadFirst(Builders<Clan>.Filter.Eq(l => l.ClanId, clanId));
    }

    public Task<ClanMembership> LoadMemberShip(string battleTag)
    {
        return LoadFirst(Builders<ClanMembership>.Filter.Eq(m => m.BattleTag, battleTag));
    }

    public Task UpsertMemberShip(ClanMembership clanMemberShip)
    {
        return UpsertTimed(clanMemberShip, Builders<ClanMembership>.Filter.Eq(c => c.BattleTag, clanMemberShip.BattleTag));
    }

    public Task DeleteClan(string clanId)
    {
        return Delete<Clan>(c => c.ClanId == clanId);
    }

    public Task<List<ClanMembership>> LoadMemberShips(List<string> clanMembers)
    {
        return LoadAll(Builders<ClanMembership>.Filter.In(m => m.BattleTag, clanMembers));
    }

    public Task<List<ClanMembership>> LoadMemberShipsSince(DateTimeOffset from)
    {
        return LoadManySince<ClanMembership>(from);
    }

    public Task SaveMemberShips(List<ClanMembership> clanMembers)
    {
        return UpsertMany(clanMembers);
    }

    public ClanRepository(MongoClient mongoClient) : base(mongoClient)
    {
    }
}
