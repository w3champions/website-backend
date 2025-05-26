using Microsoft.AspNetCore.Mvc.TagHelpers.Cache;
using Microsoft.Extensions.Caching.Memory;
using MongoDB.Driver;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Threading.Tasks;
using W3C.Contracts.Matchmaking;
using W3C.Domain.Repositories;
using W3C.Domain.Tracing;
using W3ChampionsStatisticService.Services;

namespace W3ChampionsStatisticService.Matches;

public class OngoingMatchesCache(MongoClient mongoClient, TracingService tracingService) : MongoDbRepositoryBase(mongoClient), IOngoingMatchesCache
{
    private readonly TracingService _tracingService = tracingService;
    private readonly object _dictLock = new();
    private ConcurrentDictionary<string, OnGoingMatchup> _ongoingMatchesCache = new();
    private ConcurrentDictionary<string, OnGoingMatchup> _loadOnGoingMatchForPlayerCache = new();
    // TODO: emit cache metrics

    private MemoryCache _countOngoingMatchesCache = new(new MemoryCacheOptions { SizeLimit = 5000 });
    private readonly MemoryCacheEntryOptions _countOngoingMatchesCacheOptions = new MemoryCacheEntryOptions
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1),
        Size = 1
    };
    private MemoryCache _loadOngoingMatchesCache = new(new MemoryCacheOptions { SizeLimit = 5000 });
    private readonly MemoryCacheEntryOptions _loadOngoingMatchesCacheOptions = new MemoryCacheEntryOptions
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1),
        Size = 1
    };

    private bool _cachesInitialized = false;

    public class CacheResult<T>
    {
#nullable enable
        public T? Value { get; set; }
        public bool IsNegativeCache => Value == null;
#nullable disable
    }

    [Trace]
    private async Task RepopulateCaches()
    {
        var mongoCollection = CreateCollection<OnGoingMatchup>();

        // We're creating new dictionaries to avoid locking during population
        var tempOngoingMatches = new ConcurrentDictionary<string, OnGoingMatchup>();
        var tempOngoingMatchesForPlayer = new ConcurrentDictionary<string, OnGoingMatchup>();

        await mongoCollection.Find(r => true).ForEachAsync(matchup =>
        {
            tempOngoingMatches.TryAdd(matchup.MatchId, matchup);
            foreach (var playerBattleTag in matchup.Teams.SelectMany(t => t.Players.Select(p => p.BattleTag).Distinct()))
            {
                tempOngoingMatchesForPlayer.TryAdd(playerBattleTag, matchup);
            }
        });

        lock (_dictLock)
        {
            if (!_cachesInitialized)
            {
                _ongoingMatchesCache = tempOngoingMatches;
                _loadOnGoingMatchForPlayerCache = tempOngoingMatchesForPlayer;
                _cachesInitialized = true;
            }
        }
    }

    private async Task EnsureCachesInitialized()
    {
        if (!_cachesInitialized)
        {
            await RepopulateCaches();
        }
    }

    public async Task<long> CountOnGoingMatches(
        GameMode gameMode,
        GateWay gateWay,
        string map,
        int minMmr,
        int maxMmr)
    {
        var cacheKey = string.Join("|", gameMode, gateWay, map, minMmr, maxMmr);
        if (_countOngoingMatchesCache.Get(cacheKey) is CacheResult<long> cachedValue)
        {
            if (cachedValue.IsNegativeCache)
            {
                return 0L;
            }

            return cachedValue.Value;
        }

        return await _tracingService.ExecuteWithSpanAsync(this, async () =>
        {
            await EnsureCachesInitialized();

            var count = _ongoingMatchesCache
            .Count(m => (gameMode == GameMode.Undefined || m.Value.GameMode == gameMode)
                        && (gateWay == GateWay.Undefined || m.Value.GateWay == gateWay)
                        && (map == "Overall" || m.Value.Map == map)
                        && (minMmr == 0 || !m.Value.Teams.Any(team => team.Players.Any(player => player.OldMmr < minMmr)))
                        && (maxMmr == 3000 || !m.Value.Teams.Any(team => team.Players.Any(player => player.OldMmr > maxMmr))));

            _countOngoingMatchesCache.Set(cacheKey, new CacheResult<long> { Value = count }, _countOngoingMatchesCacheOptions);
            return count;
        }, [new("gameMode", gameMode), new("gateWay", gateWay), new("map", map), new("minMmr", minMmr), new("maxMmr", maxMmr)]);
    }

    public async Task<List<OnGoingMatchup>> LoadOnGoingMatches(
        GameMode gameMode,
        GateWay gateWay,
        int offset,
        int pageSize,
        string map,
        int minMmr,
        int maxMmr,
        MatchSortMethod sort)
    {
        var cacheKey = string.Join("|", gameMode, gateWay, map, minMmr, maxMmr, sort);
        IEnumerable<OnGoingMatchup> matches;
        if (_loadOngoingMatchesCache.Get(cacheKey) is CacheResult<List<OnGoingMatchup>> cachedValue)
        {
            if (cachedValue.IsNegativeCache)
            {
                return new List<OnGoingMatchup>();
            }

            matches = cachedValue.Value;
        }
        else
        {
            return await _tracingService.ExecuteWithSpanAsync(this, async () =>
            {
                await EnsureCachesInitialized();
                matches = _ongoingMatchesCache.Values
                    .Where(m => (gameMode == GameMode.Undefined || m.GameMode == gameMode)
                                && (gateWay == GateWay.Undefined || m.GateWay == gateWay)
                            && (map == "Overall" || m.Map == map)
                            && (minMmr == 0 || !m.Teams.Any(team => team.Players.Any(player => player.OldMmr < minMmr)))
                            && (maxMmr == 3000 || !m.Teams.Any(team => team.Players.Any(player => player.OldMmr > maxMmr))));

                matches = sort switch
                {
                    MatchSortMethod.MmrAscending => matches.OrderBy(GetMaxMmrInMatch),
                    MatchSortMethod.MmrDescending => matches.OrderByDescending(GetMaxMmrInMatch),
                    MatchSortMethod.StartTimeAscending => matches.OrderBy(m => m.StartTime),
                    MatchSortMethod.StartTimeDescending => matches.OrderByDescending(m => m.StartTime),
                    _ => throw new ArgumentException($"Invalid sort option: {sort}"),
                };
                var matchesList = matches.ToList();
                matches = matchesList;

                _loadOngoingMatchesCache.Set(cacheKey, new CacheResult<List<OnGoingMatchup>> { Value = matchesList }, _loadOngoingMatchesCacheOptions);
                return matchesList;
            }, [new("cacheKey", cacheKey), new("gameMode", gameMode), new("gateWay", gateWay), new("map", map), new("minMmr", minMmr), new("maxMmr", maxMmr), new("sort", sort)]);
        }

        var result = matches
            .Skip(offset)
            .Take(pageSize)
            .ToList();

        return result;
    }

    public int GetMaxMmrInTeam(Team team)
    {
        return team.Players.Max(p => p.OldMmr);
    }

    public int GetMaxMmrInMatch(OnGoingMatchup match)
    {
        return match.Teams.Max(t => GetMaxMmrInTeam(t));
    }

    public async Task<OnGoingMatchup> LoadOnGoingMatchForPlayer(string playerId)
    {
        await EnsureCachesInitialized();

        if (_loadOnGoingMatchForPlayerCache.TryGetValue(playerId, out var cachedValue))
        {
            return cachedValue;
        }

        return null;
    }

    [Trace]
    public async Task Upsert(OnGoingMatchup matchup)
    {
        await EnsureCachesInitialized();

        _ongoingMatchesCache.AddOrUpdate(matchup.MatchId, matchup, (key, oldValue) => matchup);
        foreach (var playerBattleTag in matchup.Teams.SelectMany(t => t.Players.Select(p => p.BattleTag).Distinct()))
        {
            _loadOnGoingMatchForPlayerCache.AddOrUpdate(playerBattleTag, matchup, (key, oldValue) => matchup);
        }
    }

    [Trace]
    public async Task Delete(Matchup matchup)
    {
        await EnsureCachesInitialized();

        foreach (var playerBattleTag in matchup.Teams.SelectMany(t => t.Players.Select(p => p.BattleTag).Distinct()))
        {
            _loadOnGoingMatchForPlayerCache.TryRemove(playerBattleTag, out _);
        }
        _ongoingMatchesCache.TryRemove(matchup.MatchId, out _);
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
        int maxMmr,
        MatchSortMethod sort);

    Task<OnGoingMatchup> LoadOnGoingMatchForPlayer(string playerId);
    Task Upsert(OnGoingMatchup matchup);
    Task Delete(Matchup matchup);
}