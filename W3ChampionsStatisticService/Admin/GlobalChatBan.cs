using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace W3ChampionsStatisticService.Admin;

public class GlobalChatBan 
{
    public int id { get; set; }
    public string battleTag { get; set; }
    public DateTime? expiresAt { get; set; }
}

public class PlayerChatBanWrapper 
{
    [JsonProperty("player_bans")]
    public List<PlayerChatBan> playerBansList { get; set; }
}

public class PlayerChatBan
{
    public int id { get; set; }
    public PlayerChatBanPlayerData player { get; set; }
    public int banType { get; set; }

    [JsonProperty("ban_expires_at")]
    public PlayerChatBanTimestamp banExpiresAt { get; set; }

    [JsonProperty("created_at")]
    public PlayerChatBanTimestamp createdAt { get; set; }
}

public class PlayerChatBanPlayerData
{
    public int id { get; set; }
    public string name { get; set; }
    public int source { get; set; }
    public PlayerChatBanPlayerDataRealm realm { get; set; }
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
}
