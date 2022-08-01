﻿using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using W3C.Domain.CommonValueObjects;
using W3C.Domain.Repositories;

namespace W3ChampionsStatisticService.Matches
{
    public class OngoingMatchesCache : MongoDbRepositoryBase, IOngoingMatchesCache
    {
        private List<OnGoingMatchup> _values = new List<OnGoingMatchup>();
        private Object _lock = new Object();

        public async Task<long> CountOnGoingMatches(
          GameMode gameMode,
          GateWay gateWay,
          string map,
          int minMmr,
          int maxMmr)
        {
            await UpdateCacheIfNeeded();
            return _values.Count(m => (gameMode == GameMode.Undefined || m.GameMode == gameMode)
                                      && (gateWay == GateWay.Undefined || m.GateWay == gateWay)
                                      && (map == "Overall" || m.Map == map)
                                      && !m.Teams.Any(team => team.Players.Any(player => player.OldMmr < minMmr))
                                      && !m.Teams.Any(team => team.Players.Any(player => player.OldMmr > maxMmr)));

        }

        public async Task<List<OnGoingMatchup>> LoadOnGoingMatches(
          GameMode gameMode,
          GateWay gateWay,
          int offset,
          int pageSize,
          string map,
          int minMmr,
          int maxMmr)
        {
            await UpdateCacheIfNeeded();

            return _values
                .Where(m => (gameMode == GameMode.Undefined || m.GameMode == gameMode)
                            && (gateWay == GateWay.Undefined || m.GateWay == gateWay)
                            && (map == "Overall" || m.Map == map)
                            && !m.Teams.Any(team => team.Players.Any(player => player.OldMmr < minMmr))
                            && !m.Teams.Any(team => team.Players.Any(player => player.OldMmr > maxMmr)))
                .Skip(offset)
                .Take(pageSize)
                .ToList();
        }

        public async Task<OnGoingMatchup> LoadOnGoingMatchForPlayer(string playerId)
        {
            await UpdateCacheIfNeeded();
            return _values.FirstOrDefault(m => m.Team1Players != null && m.Team1Players.Contains(playerId)
                                           || m.Team2Players != null && m.Team2Players.Contains(playerId)
                                           || m.Team3Players != null && m.Team3Players.Contains(playerId)
                                           || m.Team4Players != null && m.Team4Players.Contains(playerId));
        }

        public void Upsert(OnGoingMatchup matchup)
        {
            lock (_lock)
            {
                var orderByDescending = _values.Where(m => m.MatchId != matchup.MatchId);
                _values = orderByDescending.Append(matchup).OrderByDescending(s => s.Id).ToList();
            }

        }

        public void Delete(string matchId)
        {
            lock (_lock)
            {
                _values = _values.Where(m => m.MatchId != matchId).ToList();
            }
        }

        private async Task UpdateCacheIfNeeded()
        {
            if (_values.Count == 0)
            {
                var mongoCollection = CreateCollection<OnGoingMatchup>();
                _values = await mongoCollection.Find(r => true).SortByDescending(s => s.Id).ToListAsync();
            }
        }

        public OngoingMatchesCache(MongoClient mongoClient) : base(mongoClient)
        {
        }
    }

    public interface IOngoingMatchesCache
    {
        Task<long> CountOnGoingMatches(
          GameMode gameMode,
          GateWay gateWay,
          string map,
          int minMmr,
          int maxMmr);

        Task<List<OnGoingMatchup>> LoadOnGoingMatches(
          GameMode gameMode,
          GateWay gateWay,
          int offset,
          int pageSize,
          string map,
          int minMmr,
          int maxMmr);

        Task<OnGoingMatchup> LoadOnGoingMatchForPlayer(string playerId);
        void Upsert(OnGoingMatchup matchup);
        void Delete(string matchId);
    }
}
