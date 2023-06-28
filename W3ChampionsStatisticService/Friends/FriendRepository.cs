using MongoDB.Bson;
using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using W3C.Domain.Repositories;
using W3ChampionsStatisticService.Ports;

namespace W3ChampionsStatisticService.Friends
{
    public class FriendRepository : MongoDbRepositoryBase, IFriendRepository
    {
        public FriendRepository(MongoClient mongoClient) : base(mongoClient) {}

        public async Task<Friendlist> LoadFriendlist(string battleTag)
        {
            var friendlist = await LoadFirst<Friendlist>(battleTag);

            if (friendlist == null)
            {
                friendlist = new Friendlist(battleTag);
                await Insert(friendlist);
            }

            return friendlist;
        }

        public Task UpsertFriendlist(Friendlist friendlist)
        {
            return Upsert(friendlist, p => p.Id == friendlist.Id);
        }
    }
}
