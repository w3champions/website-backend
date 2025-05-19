using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using W3ChampionsStatisticService.Cache;

namespace WC3ChampionsStatisticService.Tests.Cache;

[TestFixture]
public class CacheTest
{
    private ServiceCollection _serviceCollection;

    [SetUp]
    public void SetupTest()
    {
        var services = new ServiceCollection();
        services.AddMemoryCache();
        services.AddTransient(typeof(ICachedDataProvider<>), typeof(InMemoryCachedDataProvider<>));
        services.AddTransient<ITestService, TestService>();
        _serviceCollection = services;
    }

    [Test]
    public async Task TestCacheLocking()
    {
        var serviceProvider = _serviceCollection.BuildServiceProvider();
        //Simulate parallel requests to test the cache locking
        ConcurrentBag<TestData> data = new();

        await Parallel.ForEachAsync(Enumerable.Range(1, 2), async (_, _) =>
        {
            var testService = serviceProvider.GetRequiredService<ITestService>();
            data.Add(await testService.GetTestDataAsync(null));
        });

        //Validate that all the requests returned the same cached data
        Assert.AreEqual(1, data.Select(x => x.RequestId).Distinct().Count());
    }

    [Test]
    public async Task TestCacheWithoutLocking()
    {
        _serviceCollection.Configure<CacheOptionsFor<TestData>>(
            x => { x.LockDuringFetch = false; });

        var serviceProvider = _serviceCollection.BuildServiceProvider();
        //Simulate parallel requests to test the cache without locking
        ConcurrentBag<TestData> data = new();

        await Parallel.ForEachAsync(Enumerable.Range(1, 2), async (_, _) =>
        {
            var testService = serviceProvider.GetRequiredService<ITestService>();
            data.Add(await testService.GetTestDataAsync(null));
        });

        //Validate that all the requests returned the different cached data
        Assert.AreEqual(2, data.Select(x => x.RequestId).Distinct().Count());
    }

    [Test]
    public async Task TestCacheTimeToLive()
    {
        _serviceCollection.Configure<CacheOptionsFor<TestData>>(
            x => { x.CacheDuration = TimeSpan.FromMilliseconds(100); });

        var serviceProvider = _serviceCollection.BuildServiceProvider();

        var getDataFunc = new Func<Task<TestData>>(async () =>
        {
            var testService = serviceProvider.GetRequiredService<ITestService>();
            return await testService.GetTestDataAsync(null);
        });

        var data1 = await getDataFunc();
        await Task.Delay(50);
        var data2 = await getDataFunc();

        //Validate that cache is used
        Assert.AreEqual(data1.RequestId, data2.RequestId);
        Assert.AreEqual(data1.Key, data2.Key);

        await Task.Delay(100);

        var data3 = await getDataFunc();

        Assert.AreEqual(data1.Key, data3.Key);
        //Validate that cache is dropped and we get a new instance
        Assert.AreNotEqual(data1.RequestId, data3.RequestId);
    }


    [Test]
    public async Task TestCacheKey()
    {
        var serviceProvider = _serviceCollection.BuildServiceProvider();

        var getDataWithKey = new Func<string, Task<TestData>>(async key =>
        {
            var testService = serviceProvider.GetRequiredService<ITestService>();
            return await testService.GetTestDataAsync(key);
        });

        var data1 = await getDataWithKey("key1");
        Assert.AreEqual(data1.Key, "key1");

        var data2 = await getDataWithKey("key2");
        Assert.AreEqual(data2.Key, "key2");


        var dataWithKey1 = await getDataWithKey("key1");
        Assert.AreEqual(dataWithKey1.Key, "key1");
        Assert.AreEqual(dataWithKey1.Key, data1.Key);

        //Validate that we get same data for the same key
        Assert.AreEqual(dataWithKey1.RequestId, data1.RequestId);

        var dataWithKey2 = await getDataWithKey("key2");
        Assert.AreEqual(dataWithKey2.Key, "key2");

        //Validate that we get same data for the same key
        Assert.AreEqual(dataWithKey2.RequestId, data2.RequestId);
    }
}

public class TestData
{
    public Guid RequestId { get; init; }
    public string Key { get; init; }
}

public class TestService(ICachedDataProvider<TestData> testCachedDataProvider) : ITestService
{
    private readonly ICachedDataProvider<TestData> _testCachedDataProvider = testCachedDataProvider;

    public async Task<TestData> GetTestDataAsync(string key)
    {
        return await _testCachedDataProvider.GetCachedOrRequestAsync(
            async () =>
            {
                //Simulate a slow request
                await Task.Delay(100);
                return new TestData
                {
                    Key = key,
                    //We generate new guid for each request to validate that we get a new instance
                    RequestId = Guid.NewGuid(),
                };
            }, key);
    }
}

public interface ITestService
{
    Task<TestData> GetTestDataAsync(string key);
}
