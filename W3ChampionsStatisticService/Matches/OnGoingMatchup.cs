﻿using System;
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
                Map = new MapName(match.map).Name,
                MatchId = match.id,
                GateWay = match.gateway,
                GameMode = match.gameMode,
                StartTime = startTime,
            };

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

            result.Teams.Add(CreateTeam(firstTeam));
            result.Teams.Add(CreateTeam(secondTeam));

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
                Race = w.race
            });
        }
    }
}