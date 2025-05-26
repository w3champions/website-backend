using Microsoft.AspNetCore.Mvc.TagHelpers.Cache;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using Serilog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using W3C.Contracts.Matchmaking;
using W3C.Domain.Repositories;
using W3C.Domain.Tracing;
using W3ChampionsStatisticService.Services;

namespace W3ChampionsStatisticService.Matches;

public class OngoingMatchesCache(MongoClient mongoClient, TracingService tracingService) : MongoDbRepositoryBase(mongoClient), IOngoingMatchesCache
{
    private readonly TracingService _tracingService = tracingService;
    private ConcurrentDictionary<string, OnGoingMatchup> _ongoingMatchesCache = new();
    private ConcurrentDictionary<string, OnGoingMatchup> _loadOnGoingMatchForPlayerCache = new();
    private readonly SemaphoreSlim _initializationSemaphore = new(1, 1);
    // TODO: emit cache metrics

    private readonly IMemoryCache _countOngoingMatchesCache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 5000 });
    private readonly MemoryCacheEntryOptions _countOngoingMatchesCacheOptions = new MemoryCacheEntryOptions
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1),
        Size = 1
    };
    private readonly IMemoryCache _loadOngoingMatchesCache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 5000 });
    private readonly MemoryCacheEntryOptions _loadOngoingMatchesCacheOptions = new MemoryCacheEntryOptions
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1),
        Size = 1
    };

    private DateTime _lastCacheSizeWarning = DateTime.MinValue;
    private readonly TimeSpan _cacheSizeWarningInterval = TimeSpan.FromMinutes(1);

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
        try
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

            _ongoingMatchesCache = tempOngoingMatches;
            _loadOnGoingMatchForPlayerCache = tempOngoingMatchesForPlayer;
            Log.Information("Cache initialized with {MatchCount} matches and {PlayerCount} player mappings", 
                tempOngoingMatches.Count, tempOngoingMatchesForPlayer.Count);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to repopulate caches");
            throw;
        }
    }

    private async Task EnsureCachesInitialized()
    {
        // Quick check without locking
        if (!_ongoingMatchesCache.IsEmpty) return;
        
        await _initializationSemaphore.WaitAsync();
        try
        {
            // Double-check inside the lock
            if (_ongoingMatchesCache.IsEmpty)
            {
                await RepopulateCaches();
            }
        }
        finally
        {
            _initializationSemaphore.Release();
        }
    }

    private void CheckCacheSize()
    {
        var now = DateTime.UtcNow;

        var matchCount = _ongoingMatchesCache.Count;
        var playerCount = _loadOnGoingMatchForPlayerCache.Count;
            
        if (matchCount > 10000 || playerCount > 50000)
        {
            if (now - _lastCacheSizeWarning > _cacheSizeWarningInterval)
            {
                Log.Error("Cache sizes are large, are we having a leak? Matches: {MatchCount}, Players: {PlayerCount}", 
                    matchCount, playerCount);
                _lastCacheSizeWarning = now;
            }
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
        // Cache key for the complete query (filters + sort)
        var cacheKey = string.Join("|", gameMode, gateWay, map, minMmr, maxMmr, sort);
        
        // Check if we have cached results for this exact query
        if (_loadOngoingMatchesCache.TryGetValue(cacheKey, out CacheResult<List<OnGoingMatchup>> cachedValue))
        {
            if (cachedValue.IsNegativeCache)
            {
                return new List<OnGoingMatchup>();
            }
            
            // Return paginated results from cached list
            return cachedValue.Value
                .Skip(offset)
                .Take(pageSize)
                .ToList();
        }
        
        return await _tracingService.ExecuteWithSpanAsync(this, async () =>
        {
            await EnsureCachesInitialized();
            
            // Filter from the main cache
            var matches = _ongoingMatchesCache.Values
                .Where(m => (gameMode == GameMode.Undefined || m.GameMode == gameMode)
                            && (gateWay == GateWay.Undefined || m.GateWay == gateWay)
                            && (map == "Overall" || m.Map == map)
                            && (minMmr == 0 || !m.Teams.Any(team => team.Players.Any(player => player.OldMmr < minMmr)))
                            && (maxMmr == 3000 || !m.Teams.Any(team => team.Players.Any(player => player.OldMmr > maxMmr))));

            // Apply sorting
            matches = sort switch
            {
                MatchSortMethod.MmrAscending => matches.OrderBy(GetMaxMmrInMatch),
                MatchSortMethod.MmrDescending => matches.OrderByDescending(GetMaxMmrInMatch),
                MatchSortMethod.StartTimeAscending => matches.OrderBy(m => m.StartTime),
                MatchSortMethod.StartTimeDescending => matches.OrderByDescending(m => m.StartTime),
                _ => throw new ArgumentException($"Invalid sort option: {sort}"),
            };
            
            var sortedList = matches.ToList();
            
            // Cache the filtered and sorted list
            _loadOngoingMatchesCache.Set(cacheKey, new CacheResult<List<OnGoingMatchup>> { Value = sortedList }, _loadOngoingMatchesCacheOptions);
            
            // Return paginated results
            return sortedList
                .Skip(offset)
                .Take(pageSize)
                .ToList();
        }, [new("cacheKey", cacheKey), new("gameMode", gameMode), new("gateWay", gateWay), new("map", map), new("minMmr", minMmr), new("maxMmr", maxMmr), new("sort", sort)]);
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
        
        CheckCacheSize();
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
        
        CheckCacheSize();
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