using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using W3ChampionsStatisticService.Friends;

namespace W3ChampionsStatisticService.Ports
{
    public interface IFriendRepository
    {
        Task<Friendlist> LoadFriendlist(string battleTag);
        // Task SaveFriendlist(string battleTag);
        Task UpsertFriendlist(Friendlist friendlist);
    }
}
