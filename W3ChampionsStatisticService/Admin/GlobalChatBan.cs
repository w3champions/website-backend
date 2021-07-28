using System;
using System.Collections.Generic;

namespace W3ChampionsStatisticService.Admin
{
    public class GlobalChatBan 
    {
        public int id { get; set; }
        public string battleTag { get; set; }
        public DateTime? expiresAt { get; set; }
    }

    public class PlayerChatBanWrapper 
    {
        public List<PlayerChatBan> playerBansList { get; set; }
    }

    public class PlayerChatBan
    {
        public int id { get; set; }
        public PlayerChatBanPlayerData player { get; set; }
        public int banType { get; set; }
        public PlayerChatBanTimestamp banExpiresAt { get; set; }
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
        public int seconds { get; set; }
        public int nanos { get; set; }
    }

    public class ChatBanPutDto
    {
        public string battleTag { get; set; }
        public string expiresAt { get; set; }
    }
}