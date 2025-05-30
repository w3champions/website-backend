using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using W3C.Domain.Repositories;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;


namespace W3ChampionsStatisticService.W3ChampionsStats.DistinctPlayersPerDays;

public class DistinctPlayersPerDay : IIdentifiable
{
    [BsonRepresentation(BsonType.Array)]
    public DateTimeOffset Date { get; set; }
    public long DistinctPlayers => Players.Count;
    [JsonIgnore]
    public List<string> Players { get; set; } = new List<string>();
    public string Id => Date.Date.ToString("yyyy-MM-dd");

    public static DistinctPlayersPerDay Create(DateTimeOffset endTime)
    {
        return new DistinctPlayersPerDay
        {
            Date = endTime.Date
        };
    }

    public void AddPlayer(string player)
    {
        if (!Players.Contains(player))
        {
            Players.Add(player);
        }
    }
}
