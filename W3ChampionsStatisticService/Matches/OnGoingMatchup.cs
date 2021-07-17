using System;
using System.Collections.Generic;
using System.Linq;
using W3ChampionsStatisticService.PadEvents;

namespace W3ChampionsStatisticService.Matches
{
    public class OnGoingMatchup: Matchup
    {
        public static OnGoingMatchup Create(MatchStartedEvent matchStartedEvent)
        {
            var match = matchStartedEvent.match;

            var startTime = DateTimeOffset.FromUnixTimeMilliseconds(match.startTime);

            var result = new OnGoingMatchup()
            {
                Id = matchStartedEvent.Id,
                MapName = match.mapName,
                MapId = match.mapId,
                MatchId = match.id,
                GateWay = match.gateway,
                GameMode = match.gameMode,
                StartTime = startTime,
            };

            result.SetServerInfo(match);

            var teamGroups = SplitPlayersIntoTeams(match.players, match.gameMode);

            foreach (var team in teamGroups)
            {
                result.Teams.Add(CreateTeam(team.Value));
            }

            SetTeamPlayers(result);

            return result;
        }

        private static Team CreateTeam(IEnumerable<UnfinishedMatchPlayer> players)
        {
            var team = new Team();
            team.Players.AddRange(CreatePlayerArray(players));
            return team;
        }

        private static IEnumerable<PlayerOverviewMatches> CreatePlayerArray(IEnumerable<UnfinishedMatchPlayer> players)
        {
            return players.Select(w => new PlayerOverviewMatches
            {
                Name = w.battleTag.Split("#")[0],
                BattleTag = w.battleTag,
                OldMmr = (int)w.mmr.rating,
                Race = w.race,
                Location = w.country
            });
        }
    }
}