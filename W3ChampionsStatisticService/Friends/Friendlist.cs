using System.Collections.Generic;
using W3C.Domain.Repositories;

namespace W3ChampionsStatisticService.Friends
{
    public class Friendlist : IIdentifiable
    {
        public Friendlist(string battleTag)
        {
            Id = battleTag;
            Friends = new List<string> {};
            BlockedBattleTags = new List<string> {};
            BlockAllRequests = false;
        }
        public string Id { get; set; }
        public List<string> Friends { get; set; }
        public List<string> BlockedBattleTags  { get; set; }
        public bool BlockAllRequests  { get; set; }
    }
}
