using System;
using MongoDB.Bson.Serialization.Attributes;
using W3C.Domain.Repositories;

namespace W3ChampionsStatisticService.Ladder;

/// <summary>The last league promotion detection has seen and processed for one single-member
/// standing. Its id is the standing's deterministic <see cref="Rank"/> id
/// ({season}_{battleTag@gateway}_{gameMode}[_{race}]), so a season start has no baseline
/// document and records silently — same for the first batch after this collection ships.</summary>
[BsonIgnoreExtraElements]
public class LeagueBaseline(string id, int league) : IIdentifiable
{
    public string Id { get; set; } = id;
    public int League { get; set; } = league;
    public DateTimeOffset LastUpdated { get; set; }
}
