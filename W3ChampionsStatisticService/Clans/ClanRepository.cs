using System.Threading.Tasks;
using MongoDB.Bson;
using MongoDB.Driver;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.ReadModelBase;

namespace W3ChampionsStatisticService.Clans
{
    public class ClanRepository : MongoDbRepositoryBase, IClanRepository
    {
        public async Task<bool> TryInsertClan(Clan clan)
        {
            var clanFound = await LoadFirst<Clan>(c => c.ClanName == clan.ClanName);
            if (clanFound != null) return false;
            await Upsert(clan, c => c.Id == clan.Id);
            return true;
        }

        public Task UpsertClan(Clan clan)
        {
            return Upsert(clan, c => c.Id == clan.Id);
        }

        public Task<Clan> LoadClan(string clanId)
        {
            return LoadFirst<Clan>(l => l.Id == new ObjectId(clanId));
        }

        public Task<ClanMembership> LoadMemberShip(string battleTag)
        {
            return LoadFirst<ClanMembership>(m => m.BattleTag == battleTag);
        }

        public Task UpsertMemberShip(ClanMembership clanMemberShip)
        {
            return Upsert(clanMemberShip, c => c.BattleTag == clanMemberShip.BattleTag);
        }

        public ClanRepository(MongoClient mongoClient) : base(mongoClient)
        {
        }
    }
}