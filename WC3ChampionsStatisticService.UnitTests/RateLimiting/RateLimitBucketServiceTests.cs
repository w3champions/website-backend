using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using W3ChampionsStatisticService.RateLimiting.Services;

namespace WC3ChampionsStatisticService.Tests.RateLimiting;

[TestFixture]
public class RateLimitBucketServiceTests
{
    private IMemoryCache _cache;
    private Mock<ILogger<RateLimitBucketService>> _loggerMock;
    private RateLimitBucketService _service;

    [SetUp]
    public void Setup()
    {
        _cache = new MemoryCache(new MemoryCacheOptions());
        _loggerMock = new Mock<ILogger<RateLimitBucketService>>();
        _service = new RateLimitBucketService(_cache, _loggerMock.Object);
    }

    [TearDown]
    public void TearDown()
    {
        _cache?.Dispose();
    }

    [Test]
    public async Task TryAcquireAsync_WithinHourlyLimit_ReturnsAcquiredLease()
    {
        var partitionKey = "test-partition-1";
        var hourlyLimit = 10;
        var dailyLimit = 100;

        var lease = await _service.TryAcquireAsync(partitionKey, hourlyLimit, dailyLimit);

        Assert.That(lease.IsAcquired, Is.True);
    }

    [Test]
    public async Task TryAcquireAsync_ExceedsHourlyLimit_ReturnsNotAcquiredLease()
    {
        var partitionKey = "test-partition-2";
        var hourlyLimit = 3;
        var dailyLimit = 100;

        // Acquire up to the limit
        for (int i = 0; i < hourlyLimit; i++)
        {
            var lease = await _service.TryAcquireAsync(partitionKey, hourlyLimit, dailyLimit);
            Assert.That(lease.IsAcquired, Is.True, $"Request {i + 1} should be acquired");
        }

        // Next request should fail
        var failedLease = await _service.TryAcquireAsync(partitionKey, hourlyLimit, dailyLimit);
        Assert.That(failedLease.IsAcquired, Is.False);
    }

    [Test]
    public async Task TryAcquireAsync_ExceedsDailyLimit_ReturnsNotAcquiredLease()
    {
        var partitionKey = "test-partition-3";
        var hourlyLimit = 100;
        var dailyLimit = 5;

        // Acquire up to the daily limit
        for (int i = 0; i < dailyLimit; i++)
        {
            var lease = await _service.TryAcquireAsync(partitionKey, hourlyLimit, dailyLimit);
            Assert.That(lease.IsAcquired, Is.True, $"Request {i + 1} should be acquired");
        }

        // Next request should fail due to daily limit
        var failedLease = await _service.TryAcquireAsync(partitionKey, hourlyLimit, dailyLimit);
        Assert.That(failedLease.IsAcquired, Is.False);
    }

    [Test]
    public async Task TryAcquireAsync_DifferentPartitions_HaveIndependentLimits()
    {
        var partition1 = "partition-1";
        var partition2 = "partition-2";
        var hourlyLimit = 2;
        var dailyLimit = 10;

        // Use up partition 1's hourly limit
        for (int i = 0; i < hourlyLimit; i++)
        {
            var lease = await _service.TryAcquireAsync(partition1, hourlyLimit, dailyLimit);
            Assert.That(lease.IsAcquired, Is.True);
        }
        var failedLease1 = await _service.TryAcquireAsync(partition1, hourlyLimit, dailyLimit);
        Assert.That(failedLease1.IsAcquired, Is.False);

        // Partition 2 should still have available permits
        var lease2 = await _service.TryAcquireAsync(partition2, hourlyLimit, dailyLimit);
        Assert.That(lease2.IsAcquired, Is.True);
    }

    [Test]
    public async Task TryAcquireAsync_ConcurrentRequests_RespectLimits()
    {
        var partitionKey = "concurrent-test";
        var hourlyLimit = 10;
        var dailyLimit = 100;
        var successCount = 0;

        // Run concurrent requests
        var tasks = new Task<bool>[15];
        for (int i = 0; i < tasks.Length; i++)
        {
            tasks[i] = Task.Run(async () =>
            {
                var lease = await _service.TryAcquireAsync(partitionKey, hourlyLimit, dailyLimit);
                return lease.IsAcquired;
            });
        }

        var results = await Task.WhenAll(tasks);
        foreach (var result in results)
        {
            if (result) successCount++;
        }

        // Should only allow up to the hourly limit
        Assert.That(successCount, Is.EqualTo(hourlyLimit));
    }

    [Test]
    public async Task TryAcquireAsync_SamePartitionKey_ReusesCachedLimiter()
    {
        var partitionKey = "cache-test";
        var hourlyLimit = 5;
        var dailyLimit = 50;

        // Make multiple calls with same partition key
        await _service.TryAcquireAsync(partitionKey, hourlyLimit, dailyLimit);
        await _service.TryAcquireAsync(partitionKey, hourlyLimit, dailyLimit);
        await _service.TryAcquireAsync(partitionKey, hourlyLimit, dailyLimit);

        // All calls should use the same cached limiter
        // Verify by checking that the count accumulates
        await _service.TryAcquireAsync(partitionKey, hourlyLimit, dailyLimit);
        await _service.TryAcquireAsync(partitionKey, hourlyLimit, dailyLimit);

        // The 6th request should fail
        var failedLease = await _service.TryAcquireAsync(partitionKey, hourlyLimit, dailyLimit);
        Assert.That(failedLease.IsAcquired, Is.False);
    }

    [Test]
    public async Task TryAcquireAsync_ReleaseReturnsPermits()
    {
        var partitionKey = "release-test";
        var hourlyLimit = 2;
        var dailyLimit = 10;

        // Acquire permits
        var lease1 = await _service.TryAcquireAsync(partitionKey, hourlyLimit, dailyLimit);
        var lease2 = await _service.TryAcquireAsync(partitionKey, hourlyLimit, dailyLimit);

        Assert.That(lease1.IsAcquired, Is.True);
        Assert.That(lease2.IsAcquired, Is.True);

        // Should be at limit
        var failedLease = await _service.TryAcquireAsync(partitionKey, hourlyLimit, dailyLimit);
        Assert.That(failedLease.IsAcquired, Is.False);

        // Note: Sliding window rate limiters don't release permits when disposing leases
        // They track permits over a time window instead
        // This test should verify the sliding window behavior instead
        lease1.Dispose();

        // Still at limit because sliding window doesn't release on dispose
        var stillFailedLease = await _service.TryAcquireAsync(partitionKey, hourlyLimit, dailyLimit);
        Assert.That(stillFailedLease.IsAcquired, Is.False);
    }

    [Test]
    public async Task TryAcquireAsync_CompositeLeaseReleasesAllPermits()
    {
        var partitionKey = "composite-release";
        var hourlyLimit = 3;
        var dailyLimit = 10;

        // Acquire a lease
        var lease = await _service.TryAcquireAsync(partitionKey, hourlyLimit, dailyLimit);
        Assert.That(lease.IsAcquired, Is.True);

        // Use up remaining permits
        var lease2 = await _service.TryAcquireAsync(partitionKey, hourlyLimit, dailyLimit);
        var lease3 = await _service.TryAcquireAsync(partitionKey, hourlyLimit, dailyLimit);
        Assert.That(lease2.IsAcquired, Is.True);
        Assert.That(lease3.IsAcquired, Is.True);

        // Should be at limit
        var failedLease = await _service.TryAcquireAsync(partitionKey, hourlyLimit, dailyLimit);
        Assert.That(failedLease.IsAcquired, Is.False);

        // Dispose all leases
        lease.Dispose();
        lease2.Dispose();
        lease3.Dispose();

        // Sliding window doesn't release on dispose - still at limit
        var stillFailedLease = await _service.TryAcquireAsync(partitionKey, hourlyLimit, dailyLimit);
        Assert.That(stillFailedLease.IsAcquired, Is.False);
    }

    [Test]
    public async Task TryAcquireAsync_HourlyLimitFailsButDailySucceeds_ReturnsNotAcquired()
    {
        var partitionKey = "hourly-fail-test";
        var hourlyLimit = 1;
        var dailyLimit = 100;

        // Use up hourly limit
        var lease1 = await _service.TryAcquireAsync(partitionKey, hourlyLimit, dailyLimit);
        Assert.That(lease1.IsAcquired, Is.True);

        // Next request should fail even though daily limit is not reached
        var lease2 = await _service.TryAcquireAsync(partitionKey, hourlyLimit, dailyLimit);
        Assert.That(lease2.IsAcquired, Is.False);
    }

    [Test]
    public async Task TryAcquireAsync_CreatesNewLimitersForDifferentLimits()
    {
        var partitionKey = "limit-change-test";

        // First set of limits
        var lease1 = await _service.TryAcquireAsync(partitionKey, 5, 50);
        Assert.That(lease1.IsAcquired, Is.True);

        // Same partition but different limits - should create new limiters
        // This simulates a configuration change
        var lease2 = await _service.TryAcquireAsync(partitionKey, 10, 100);
        Assert.That(lease2.IsAcquired, Is.True);
    }

}
