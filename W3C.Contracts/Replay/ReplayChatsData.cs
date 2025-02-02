using System.Collections.Generic;
using Newtonsoft.Json;

namespace W3C.Contracts.Replay;

public class ReplayChatsData
{
    public List<ReplayChatsPlayerInfo> Players { get; set; }

    public List<ReplayChatsMessage> Messages { get; set; }
}

public class ReplayChatsPlayerInfo
{
    public int Id { get; set; }

    public string Name { get; set; }

    public int Team { get; set; }

    public int Color { get; set; }
}

public class ReplayChatsMessage
{
    [JsonProperty("from_player")]
    public int FromPlayer { get; set; }

    public ReplayChatsScope Scope { get; set; }

    public string Content { get; set; }
}

public class ReplayChatsScope
{
    public ReplayChatsScopeType Type { get; set; }

    public int? Id { get; set; }
}

public enum ReplayChatsScopeType
{
    All,
    Allies,
    Observers,
    Player,
}
