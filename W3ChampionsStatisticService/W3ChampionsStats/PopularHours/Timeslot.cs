using System;
using System.Text.Json.Serialization;
using MongoDB.Bson.Serialization.Attributes;

namespace W3ChampionsStatisticService.W3ChampionsStats.PopularHours;

public class Timeslot
{
    public int Games { get; set; }
    [JsonIgnore]
    public DateTime Time { get; set; }

    [BsonIgnore]
    public int Minutes => Time.Minute;
    [BsonIgnore]
    public int Hours => Time.Hour;

    public void AddGame()
    {
        Games++;
    }
}
