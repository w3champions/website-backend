using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.RateLimiting;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace W3ChampionsStatisticService.RateLimiting.Services;

public interface IRateLimitBucketService
{
    Task<RateLimitLease> TryAcquireAsync(string partitionKey, int hourlyLimit, int dailyLimit);
}

public class RateLimitBucketService : IRateLimitBucketService
{
    private readonly IMemoryCache _cache;
    private readonly ILogger<RateLimitBucketService> _logger;

    public RateLimitBucketService(IMemoryCache cache, ILogger<RateLimitBucketService> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    public async Task<RateLimitLease> TryAcquireAsync(string partitionKey, int hourlyLimit, int dailyLimit)
    {
        var hourlyKey = $"ratelimit:hourly:{partitionKey}";
        var dailyKey = $"ratelimit:daily:{partitionKey}";

        // Get or create hourly limiter
        var hourlyLimiter = await _cache.GetOrCreateAsync(hourlyKey, async entry =>
        {
            entry.SlidingExpiration = TimeSpan.FromHours(2); // Keep in cache for 2 hours after last access
            _logger.LogDebug("Creating new hourly rate limiter for partition {Partition} with limit {Limit}", partitionKey, hourlyLimit);
            return new SlidingWindowRateLimiter(new SlidingWindowRateLimiterOptions
            {
                PermitLimit = hourlyLimit,
                Window = TimeSpan.FromHours(1),
                SegmentsPerWindow = 4,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            });
        });

        // Get or create daily limiter
        var dailyLimiter = await _cache.GetOrCreateAsync(dailyKey, async entry =>
        {
            entry.SlidingExpiration = TimeSpan.FromHours(25); // Keep in cache for 25 hours after last access
            _logger.LogDebug("Creating new daily rate limiter for partition {Partition} with limit {Limit}", partitionKey, dailyLimit);
            return new SlidingWindowRateLimiter(new SlidingWindowRateLimiterOptions
            {
                PermitLimit = dailyLimit,
                Window = TimeSpan.FromHours(24),
                SegmentsPerWindow = 4,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            });
        });

        // Try to acquire from both limiters
        var hourlyLease = await hourlyLimiter.AcquireAsync(1);
        if (!hourlyLease.IsAcquired)
        {
            _logger.LogDebug("Hourly rate limit exceeded for partition {Partition}", partitionKey);
            return hourlyLease;
        }

        var dailyLease = await dailyLimiter.AcquireAsync(1);
        if (!dailyLease.IsAcquired)
        {
            // Release the hourly lease since we can't get the daily one
            hourlyLease.Dispose();
            _logger.LogDebug("Daily rate limit exceeded for partition {Partition}", partitionKey);
            return dailyLease;
        }

        // Both leases acquired
        _logger.LogDebug("Rate limit lease acquired for partition {Partition}", partitionKey);
        return new CombinedRateLimitLease(hourlyLease, dailyLease);
    }
}

internal class CombinedRateLimitLease : RateLimitLease
{
    private readonly RateLimitLease _hourlyLease;
    private readonly RateLimitLease _dailyLease;

    public CombinedRateLimitLease(RateLimitLease hourlyLease, RateLimitLease dailyLease)
    {
        _hourlyLease = hourlyLease;
        _dailyLease = dailyLease;
    }

    public override bool IsAcquired => true; // Both leases are acquired if we get here

    public override IEnumerable<string> MetadataNames 
    {
        get
        {
            var names = new HashSet<string>();
            foreach (var name in _hourlyLease.MetadataNames)
                names.Add(name);
            foreach (var name in _dailyLease.MetadataNames)
                names.Add(name);
            return names;
        }
    }

    public override bool TryGetMetadata(string metadataName, out object metadata)
    {
        if (_hourlyLease.TryGetMetadata(metadataName, out metadata))
            return true;
        
        return _dailyLease.TryGetMetadata(metadataName, out metadata);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _hourlyLease?.Dispose();
            _dailyLease?.Dispose();
        }
    }
}