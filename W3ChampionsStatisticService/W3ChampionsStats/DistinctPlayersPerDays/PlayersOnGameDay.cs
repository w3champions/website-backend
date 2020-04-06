using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace W3ChampionsStatisticService.W3ChampionsStats.DistinctPlayersPerDays
{
    public class PlayersOnGameDay
    {
        public DateTimeOffset Date { get; set; }
        public long DistinctPlayers => Players.Count;
        [JsonIgnore]
        public List<string> Players { get; set; } = new List<string>();
        public string Id => Date.Date.ToString("yyyy-MM-dd");

        public static PlayersOnGameDay Create(DateTimeOffset endTime)
        {
            return new PlayersOnGameDay
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
}