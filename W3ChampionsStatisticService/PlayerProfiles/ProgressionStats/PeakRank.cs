using System;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace W3ChampionsStatisticService.PlayerProfiles.ProgressionStats;

// A stored progression-rank snapshot (the published rank fields plus when it was reached).
// Ordering semantics live in PrestigeRankComparer.
public class PeakRank
{
    public int? League { get; set; }
    public int? Division { get; set; }
    public int? Points { get; set; }
    public int? ApexPoints { get; set; }

    public int Season { get; set; }

    [BsonRepresentation(BsonType.Array)]
    public DateTimeOffset AchievedAt { get; set; }
}
