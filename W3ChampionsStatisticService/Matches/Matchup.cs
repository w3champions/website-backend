using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using MongoDB.Bson;
using W3ChampionsStatisticService.CommonValueObjects;
using W3ChampionsStatisticService.PadEvents;
using W3ChampionsStatisticService.PlayerProfiles;

namespace W3ChampionsStatisticService.Matches
{
    public class Matchup
    {
        public string Map { get; set; }
        [JsonIgnore]
        public ObjectId Id { get; set; }

        [JsonPropertyName("id")]
        public string ObjectId => Id.ToString();
        [JsonIgnore]
        public string MatchId { get; set; }
        [JsonIgnore]
        public TimeSpan Duration { get; set; }

        public long DurationInSeconds => (long) Duration.TotalSeconds;
        public DateTimeOffset StartTime { get; set; }
        public DateTimeOffset EndTime { get; set; }
        public GameMode GameMode { get; set; }
        public IList<Team> Teams { get; set; } = new List<Team>();
        public GateWay GateWay { get; set; }

        [JsonIgnore]
        public string Team1Players { get; set; }
        [JsonIgnore]
        public string Team2Players { get; set; }

        public Matchup(MatchFinishedEvent matchFinishedEvent)
        {
            var match = matchFinishedEvent.match;
            Map = new MapName(matchFinishedEvent.match.map).Name;
            Id = matchFinishedEvent.Id;
            MatchId = match.id;
            GateWay = match.gateway;

            GameMode = matchFinishedEvent.match.gameMode;

            StartTime = DateTimeOffset.Now;
            EndTime =  DateTimeOffset.FromUnixTimeMilliseconds(matchFinishedEvent.match.endTime);
            StartTime =  DateTimeOffset.FromUnixTimeMilliseconds(matchFinishedEvent.match.startTime);
            Duration = EndTime - StartTime;

            var winners = match.players.Where(p => p.won);
            var loosers = match.players.Where(p => !p.won);

            Teams.Add(CreateTeam(winners));
            Teams.Add(CreateTeam(loosers));

            if (Teams.Count > 0)
            {
                Team1Players = string.Join(";", Teams[0].Players.Select(x => x.BattleTag));
            }

            if (Teams.Count > 1)
            {
                Team2Players = string.Join(";", Teams[1].Players.Select(x => x.BattleTag));
            }
        }

        private static Team CreateTeam(IEnumerable<PlayerMMrChange> loosers)
        {
            var team = new Team();
            team.Players.AddRange(CreatePlayerArray(loosers));
            return team;
        }

        private static IEnumerable<PlayerOverviewMatches> CreatePlayerArray(IEnumerable<PlayerMMrChange> players)
        {
            return players.Select(w => new PlayerOverviewMatches {
                Name = w.battleTag.Split("#")[0],
                BattleTag = w.battleTag,
                CurrentMmr = (int?) w.updatedMmr?.rating ?? (int) w.mmr.rating,
                OldMmr = (int) w.mmr.rating,
                Won = w.won,
                Race = w.race
            });
        }
    }
}