using System;
using System.Text.Json.Serialization;
using MongoDB.Bson.Serialization.Attributes;
using W3ChampionsStatisticService.CommonValueObjects;

namespace W3ChampionsStatisticService.PlayerProfiles
{
    [BsonIgnoreExtraElements]
    public class GameModeStat : BaseRankedStat
    {
        public GameModeStat(GameMode gameMode)
        {
            Mode = gameMode;
        }

        public GameMode Mode { set; get; }
    }
}