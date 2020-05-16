using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using MongoDB.Bson;
using W3ChampionsStatisticService.CommonValueObjects;
using W3ChampionsStatisticService.PadEvents;

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

        public Matchup()
        {
        }

        public static Matchup Create(MatchFinishedEvent matchFinishedEvent)
        {
            var match = matchFinishedEvent.match;

            var startTime = DateTimeOffset.FromUnixTimeMilliseconds(matchFinishedEvent.match.startTime);
            var endTime = DateTimeOffset.FromUnixTimeMilliseconds(matchFinishedEvent.match.endTime);

            var result = new Matchup()
            {
                Id = matchFinishedEvent.Id,
                Map = new MapName(matchFinishedEvent.match.map).Name,
                MatchId = match.id,
                GateWay = match.gateway,
                GameMode = matchFinishedEvent.match.gameMode,
                StartTime = startTime,
                EndTime = endTime,
                Duration = endTime - startTime,
            };

            var winners = match.players.Where(p => p.won);
            var loosers = match.players.Where(p => !p.won);

            result.Teams.Add(CreateTeam(winners));
            result.Teams.Add(CreateTeam(loosers));

            SetTeamPlayers(result);

            return result;
        }

        protected static void SetTeamPlayers(Matchup result)
        {
            if (result.Teams.Count > 0)
            {
                result.Team1Players = string.Join(";", result.Teams[0].Players.Select(x => x.BattleTag));
            }

            if (result.Teams.Count > 1)
            {
                result.Team2Players = string.Join(";", result.Teams[1].Players.Select(x => x.BattleTag));
            }
        }

        private static Team CreateTeam(IEnumerable<PlayerMMrChange> players)
        {
            var team = new Team();
            team.Players.AddRange(CreatePlayerArray(players));
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