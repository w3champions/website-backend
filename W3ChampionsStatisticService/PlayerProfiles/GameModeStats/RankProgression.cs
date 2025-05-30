using System;
using System.Text.Json.Serialization;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace W3ChampionsStatisticService.PlayerProfiles.GameModeStats;

public class RankProgression
{
    public static RankProgression Create(in int mmr, in double rankingPoints)
    {
        return new RankProgression
        {
            Date = DateTimeOffset.UtcNow,
            RankingPoints = rankingPoints,
            MMR = mmr,
        };
    }

    [JsonIgnore]
    [BsonRepresentation(BsonType.Array)]
    public DateTimeOffset Date { get; set; }
    public double RankingPoints { get; set; }
    public double MMR { get; set; }
}
