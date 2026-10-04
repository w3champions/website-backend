using System;
using System.Collections.Generic;
using W3C.Domain.Repositories;

namespace W3ChampionsStatisticService.Friends;

public class Friendlist(string battleTag) : IIdentifiable
{
    public string Id { get; set; } = battleTag;
    public List<string> Friends { get; set; } = new List<string> { };
    public List<string> BlockedBattleTags { get; set; } = new List<string> { };
    public List<string> VoiceMutedBattleTags { get; set; } = new List<string> { };
    public bool BlockAllRequests { get; set; } = false;

    public void SetVoiceMuted(string battleTag, bool muted)
    {
        VoiceMutedBattleTags ??= new List<string>();
        VoiceMutedBattleTags.RemoveAll(x => string.Equals(x, battleTag, StringComparison.OrdinalIgnoreCase));
        if (muted)
        {
            VoiceMutedBattleTags.Add(battleTag);
        }
    }
}
