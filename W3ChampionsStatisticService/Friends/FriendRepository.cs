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

        public async Task<FriendRequest> CreateFriendRequest(FriendRequest request)
        {
            await Insert(request);
            return request;
        }

        public async Task<FriendRequest> LoadFriendRequest(string sender, string receiver)
        {
            return await LoadFirst<FriendRequest>(r => r.Sender == sender && r.Receiver == receiver);
        }

        public async Task DeleteFriendRequest(FriendRequest request)
        {
            await Delete<FriendRequest>(r => r.Sender == request.Sender && r.Receiver == request.Receiver);
        }

        public async Task<bool> FriendRequestExists(FriendRequest request)
        {
            var req = await LoadFirst<FriendRequest>(r => r.Sender == request.Sender && r.Receiver == request.Receiver);
            if (req == null) return false;
            return true;
        }

        public async Task<List<FriendRequest>> LoadAllFriendRequestsSentByPlayer(string sender)
        {
            var requests = await LoadAll<FriendRequest>(r => r.Sender == sender);
            return requests;
        }

        public async Task<List<FriendRequest>> LoadAllFriendRequestsSentToPlayer(string receiver)
        {
            var requests = await LoadAll<FriendRequest>(r => r.Receiver == receiver);
            return requests;
        }
    }
}
