using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using MongoDB.Bson;
using W3ChampionsStatisticService.CommonValueObjects;
using W3ChampionsStatisticService.PadEvents;

namespace W3ChampionsStatisticService.Matches
{
    public class OnGoingMatchup
    {
        public string Map { get; set; }
        [JsonIgnore]
        public ObjectId Id { get; set; }

        [JsonPropertyName("id")]
        public string ObjectId => Id.ToString();
        [JsonIgnore]
        public string MatchId { get; set; }

        public DateTimeOffset StartTime { get; set; }
        public GameMode GameMode { get; set; }
        public IList<Team> Teams { get; set; } = new List<Team>();
        public GateWay GateWay { get; set; }

        [JsonIgnore]
        public string Team1Players { get; set; }
        [JsonIgnore]
        public string Team2Players { get; set; }

        public OnGoingMatchup(MatchStartedEvent matchStartedEvent)
        {
            var match = matchStartedEvent.match;
            Map = new MapName(matchStartedEvent.match.map).Name;
            Id = matchStartedEvent.Id;
            MatchId = match.id;
            GateWay = match.gateway;

            GameMode = matchStartedEvent.match.gameMode;

            StartTime = DateTimeOffset.Now;
            StartTime =  DateTimeOffset.FromUnixTimeMilliseconds(matchStartedEvent.match.startTime);

            var numberOfTeams = match.players.GroupBy(x => x.team).Count();

            var firstTeam = match.players.Where(p => p.team == 0);
            var secondTeam = match.players.Where(p => p.team == 1);

            if (numberOfTeams == 1)
            {
                var totalPlayers = match.players.Count;
                var playersInTeam = totalPlayers / 2;
                firstTeam = match.players.Take(playersInTeam);
                secondTeam = match.players.Skip(playersInTeam).Take(playersInTeam);
            }

            Teams.Add(CreateTeam(firstTeam));
            Teams.Add(CreateTeam(secondTeam));

            if (Teams.Count > 0)
            {
                Team1Players = string.Join(";", Teams[0].Players.Select(x => x.BattleTag));
            }

            if (Teams.Count > 1)
            {
                Team2Players = string.Join(";", Teams[1].Players.Select(x => x.BattleTag));
            }
        }

        private static Team CreateTeam(IEnumerable<UnfinishedMatchPlayer> players)
        {
            var team = new Team();
            team.Players.AddRange(CreatePlayerArray(players));
            return team;
        }

        private static IEnumerable<PlayerOverviewMatches> CreatePlayerArray(IEnumerable<UnfinishedMatchPlayer> players)
        {
            return players.Select(w => new PlayerOverviewMatches {
                Name = w.battleTag.Split("#")[0],
                BattleTag = w.battleTag,
                OldMmr = (int) w.mmr.rating,
                Race = w.race
            });
        }
    }
}