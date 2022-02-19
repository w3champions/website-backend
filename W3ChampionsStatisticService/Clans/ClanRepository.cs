﻿using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MongoDB.Driver;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.ReadModelBase;

namespace W3ChampionsStatisticService.Clans
{
    public class ClanRepository : MongoDbRepositoryBase, IClanRepository
    {
        public async Task<bool> TryInsertClan(Clan clan)
        {
            var clanFoundById = await LoadFirst<Clan>(c => c.ClanId == clan.ClanId);
            var clanFoundByName = await LoadFirst<Clan>(c => c.ClanName == clan.ClanName);
            if (clanFoundById != null || clanFoundByName != null) return false;
            await Insert(clan);
            return true;
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

        public ClanRepository(MongoClient mongoClient) : base(mongoClient)
        {
        }
    }
}