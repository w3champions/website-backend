using System.Collections.Generic;
using MongoDB.Bson.Serialization.Attributes;
using W3C.Contracts.GameObjects;
using W3C.Contracts.Matchmaking;

namespace W3C.Domain.MatchmakingService;

[BsonIgnoreExtraElements]
[BsonNoId]
public class ApexStandingsChangedEvent : ISyncable
{
    [BsonElement("_id")]
    public int id { get; set; }
    public int season { get; set; }
    public GameMode gameMode { get; set; }
    public int? cutoffApexPoints { get; set; }
    public int gmCount { get; set; }
    public ApexStandingRaw[] players { get; set; }
    public bool wasSyncedJustNow { get; set; }
}

[BsonIgnoreExtraElements]
[BsonNoId]
public class ApexStandingRaw
{
    public List<string> battleTags { get; set; }
    public Race? race { get; set; }
    public int apexPoints { get; set; }
    public int league { get; set; }
}
