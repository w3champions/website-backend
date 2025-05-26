using Microsoft.AspNetCore.Mvc.TagHelpers.Cache;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using Prometheus;
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
    private static readonly TimeSpan CACHE_EXPIRATION = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan CACHE_WARNING_INTERVAL = TimeSpan.FromSeconds(60);

    private readonly TracingService _tracingService = tracingService;
    private ConcurrentDictionary<string, OnGoingMatchup> _ongoingMatchesCache = new();
    private ConcurrentDictionary<string, OnGoingMatchup> _loadOnGoingMatchForPlayerCache = new();
    private readonly SemaphoreSlim _initializationSemaphore = new(1, 1);

    // Prometheus metrics for cache sizes
    private static readonly Gauge OngoingMatchesGlobalCacheSize = Metrics
        .CreateGauge("w3champions_ongoing_matches_count", "Number of ongoing matches in the ongoing matches cache");

    private static readonly Gauge OngoingMatchesPlayerCacheSize = Metrics
        .CreateGauge("w3champions_ongoing_matches_for_players_count", "Number of ongoing matches for players in the ongoing matches cache");

    private static readonly Gauge OngoingMatchesCountCacheSize = Metrics
        .CreateGauge("w3champions_ongoing_matches_count_cache_size", "Number of elements in the count ongoing matches cache");

    private static readonly Gauge OngoingMatchesFilteredCacheSize = Metrics
        .CreateGauge("w3champions_ongoing_matches_filtered_cache_size", "Number of elements in the load ongoing matches cache");

    private static readonly Counter OngoingMatchesCountCacheHits = Metrics
        .CreateCounter("w3champions_ongoing_matches_count_cache_hits_total", "Total number of cache hits for count ongoing matches cache");

    private static readonly Counter OngoingMatchesCountCacheMisses = Metrics
        .CreateCounter("w3champions_ongoing_matches_count_cache_misses_total", "Total number of cache misses for count ongoing matches cache");

    private static readonly Counter OngoingMatchesFilteredCacheHits = Metrics
        .CreateCounter("w3champions_ongoing_matches_filtered_cache_hits_total", "Total number of cache hits for load ongoing matches cache");

    private static readonly Counter OngoingMatchesFilteredCacheMisses = Metrics
        .CreateCounter("w3champions_ongoing_matches_filtered_cache_misses_total", "Total number of cache misses for load ongoing matches cache");


    private readonly IMemoryCache _countOngoingMatchesCache = new MemoryCache(new MemoryCacheOptions
    {
        SizeLimit = 500,
        TrackStatistics = true
    });
    private readonly MemoryCacheEntryOptions _countOngoingMatchesCacheOptions = new MemoryCacheEntryOptions
    {
        AbsoluteExpirationRelativeToNow = CACHE_EXPIRATION,
        Size = 1
    };
    private readonly IMemoryCache _loadOngoingMatchesCache = new MemoryCache(new MemoryCacheOptions
    {
        SizeLimit = 500,
        TrackStatistics = true
    });
    private readonly MemoryCacheEntryOptions _loadOngoingMatchesCacheOptions = new MemoryCacheEntryOptions
    {
        AbsoluteExpirationRelativeToNow = CACHE_EXPIRATION,
        Size = 1
    };

    private DateTime _lastCacheSizeWarning = DateTime.MinValue;

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

            // Update metrics after repopulation
            UpdateCacheMetrics();

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

    private void UpdateCacheMetrics()
    {
        OngoingMatchesGlobalCacheSize.Set(_ongoingMatchesCache.Count);
        OngoingMatchesPlayerCacheSize.Set(_loadOnGoingMatchForPlayerCache.Count);

        // Use MemoryCacheStatistics to get the actual count of entries and hit/miss statistics
        if (_countOngoingMatchesCache is MemoryCache countCache)
        {
            var countStats = countCache.GetCurrentStatistics();
            if (countStats != null)
            {
                OngoingMatchesCountCacheSize.Set(countStats.CurrentEntryCount);
                OngoingMatchesCountCacheHits.IncTo(countStats.TotalHits);
                OngoingMatchesCountCacheMisses.IncTo(countStats.TotalMisses);
            }
        }

        if (_loadOngoingMatchesCache is MemoryCache loadCache)
        {
            var loadStats = loadCache.GetCurrentStatistics();
            if (loadStats != null)
            {
                OngoingMatchesFilteredCacheSize.Set(loadStats.CurrentEntryCount);
                OngoingMatchesFilteredCacheHits.IncTo(loadStats.TotalHits);
                OngoingMatchesFilteredCacheMisses.IncTo(loadStats.TotalMisses);
            }
        }
    }

    private void CheckCacheSize()
    {
        var now = DateTime.UtcNow;

        var matchCount = _ongoingMatchesCache.Count;
        var playerCount = _loadOnGoingMatchForPlayerCache.Count;

        if (matchCount > 10000 || playerCount > 50000)
        {
            if (now - _lastCacheSizeWarning > CACHE_WARNING_INTERVAL)
            {
                Log.Error("Cache sizes are large, are we having a leak? Matches: {MatchCount}, Players: {PlayerCount}",
                    matchCount, playerCount);
                _lastCacheSizeWarning = now;
            }
        }

        // Update metrics whenever we check cache size
        UpdateCacheMetrics();
    }

    public async Task<long> CountOnGoingMatches(
        GameMode gameMode,
        GateWay gateWay,
        string map,
        int minMmr,
        int maxMmr)
    {
        var cacheKey = string.Join("|", gameMode, gateWay, map, minMmr, maxMmr);
        if (_countOngoingMatchesCache.Get(cacheKey) is long cachedValue)
        {
            return cachedValue;
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

            _countOngoingMatchesCache.Set(cacheKey, (long)count, _countOngoingMatchesCacheOptions);

            // Update metrics after cache operation
            UpdateCacheMetrics();

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
        if (_loadOngoingMatchesCache.TryGetValue(cacheKey, out List<OnGoingMatchup> cachedValue))
        {
            // Return paginated results from cached list
            return cachedValue
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
            _loadOngoingMatchesCache.Set(cacheKey, sortedList, _loadOngoingMatchesCacheOptions);

            // Update metrics after cache operation
            UpdateCacheMetrics();

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
