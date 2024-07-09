using System.Collections.Generic;
using W3C.Domain.Repositories;

namespace W3ChampionsStatisticService.Friends;

public class Friendlist(string battleTag) : IIdentifiable
{
    public string Id { get; set; } = battleTag;
    public List<string> Friends { get; set; } = new List<string> { };
    public List<string> BlockedBattleTags { get; set; } = new List<string> { };
    public bool BlockAllRequests { get; set; } = false;
}
