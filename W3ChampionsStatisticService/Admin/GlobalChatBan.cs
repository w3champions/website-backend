using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace W3ChampionsStatisticService.Admin;

public class GlobalChatBan
{
    public int id { get; set; }
    public string battleTag { get; set; }
    public DateTime? expiresAt { get; set; }
    public DateTime createdAt { get; set; }
    public string author { get; set; }
}

public class GlobalChatBanResponse
{
    public List<GlobalChatBan> globalChatBans { get; set; }
    public int? next_id { get; set; }
}

public class PlayerChatBanWrapper
{
    [JsonProperty("player_bans")]
    public List<PlayerChatBan> playerBansList { get; set; }
    public int? next_id { get; set; }
}

public class PlayerChatBan
{
    public int id { get; set; }
    public PlayerChatBanPlayerData player { get; set; }

    [JsonProperty("ban_type")]
    public int banType { get; set; }

    [JsonProperty("ban_expires_at")]
    public DateTime banExpiresAt { get; set; }

    [JsonProperty("created_at")]
    public DateTime createdAt { get; set; }
    public string author { get; set; }
}

public class PlayerChatBanPlayerData
{
    public int id { get; set; }
    public string name { get; set; }
    public int source { get; set; }
    public int realm { get; set; }
}

public class PlayerChatBanPlayerDataRealm
{
    public int value { get; set; }
}

public class PlayerChatBanTimestamp
{
    public uint seconds { get; set; }
    public int nanos { get; set; }
}

public class ChatBanPutDto
{
    public string battleTag { get; set; }
    public string expiresAt { get; set; }
    public string author { get; set; }
}
