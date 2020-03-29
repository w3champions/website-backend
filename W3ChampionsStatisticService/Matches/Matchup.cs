using System;
using System.Collections.Generic;
using System.Linq;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using W3ChampionsStatisticService.MatchEvents;
using W3ChampionsStatisticService.MongoDb;

namespace W3ChampionsStatisticService.Matches
{
    public class Matchup : Identifiable
    {
        public string Map { get; set; }
        public TimeSpan Duration { get; set; }
        public DateTimeOffset StartTime { get; set; }

        public GameMode GameMode { get; set; }

        public IList<Team> Teams { get; set; } = new List<Team>();

        public Matchup(MatchFinishedEvent matchFinishedEvent)
        {
            var data = matchFinishedEvent.data;
            Map = data.mapInfo.name.Split("/").Last().Replace(".w3x", "").Substring(3);

            var winners = data.players.Where(p => p.won);
            var loosers = data.players.Where(p => !p.won);

            Teams.Add(CreateTeam(loosers));
            Teams.Add(CreateTeam(winners));
        }

        private static Team CreateTeam(IEnumerable<PlayerRaw> loosers)
        {
            var team = new Team();
            team.Players.AddRange(CreatePlayerArray(loosers));
            return team;
        }

        private static IEnumerable<Player> CreatePlayerArray(IEnumerable<PlayerRaw> players)
        {
            return players.Select(w => new Player(0, 0, 0, 0, w.battleTag.Split("#")[0], w.battleTag.Split("#")[1]));
        }
    }
}