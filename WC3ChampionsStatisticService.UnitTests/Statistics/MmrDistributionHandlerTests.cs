using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using NUnit.Framework;
using W3ChampionsStatisticService.W3ChampionsStats.MmrDistribution;
using W3ChampionsStatisticService.Ports;
using W3C.Contracts.Matchmaking;
using Moq;

namespace WC3ChampionsStatisticService.Statistics;
[TestFixture]
public class MmrDistributionHandlerTests
{
    private Mock<IPlayerRepository> _playerRepoMock;
    private MmrDistributionHandler _handler;

    [SetUp]
    public void Setup()
    {
        _playerRepoMock = new Mock<IPlayerRepository>();
        _handler = new MmrDistributionHandler(_playerRepoMock.Object);
    }
    [Test]
    public async Task GetDistributions_ReturnsCorrectStats()
    {
        var mmrs = new List<int> { 3000, 2500, 2000, 1500, 1000, 500 };
        _playerRepoMock.Setup(r => r.LoadMmrs(It.IsAny<int>(), It.IsAny<GateWay>(), It.IsAny<GameMode>()))
            .ReturnsAsync(mmrs);

        var stats = await _handler.GetDistributions(1, GateWay.Europe, GameMode.GM_1v1);

        Assert.IsNotNull(stats);
        Assert.IsTrue(stats.DistributedMmrs.Count > 0);
        Assert.AreEqual(mmrs.Count, stats.StandardDeviation > 0 ? mmrs.Count : 0);
    }

    [Test]
    public async Task GetDistributions_CacheIsUsed()
    {
        var mmrs = new List<int> { 3000, 2500, 2000 };
        _playerRepoMock.Setup(r => r.LoadMmrs(It.IsAny<int>(), It.IsAny<GateWay>(), It.IsAny<GameMode>()))
            .ReturnsAsync(mmrs);

        var stats1 = await _handler.GetDistributions(1, GateWay.Europe, GameMode.GM_1v1);
        var stats2 = await _handler.GetDistributions(1, GateWay.Europe, GameMode.GM_1v1);

        // Should not call repository again, so change the mock return and verify cache is used
        _playerRepoMock.Setup(r => r.LoadMmrs(It.IsAny<int>(), It.IsAny<GateWay>(), It.IsAny<GameMode>()))
            .ReturnsAsync(new List<int> { 100 });
        var stats3 = await _handler.GetDistributions(1, GateWay.Europe, GameMode.GM_1v1);

        Assert.IsNotNull(stats1);
        Assert.IsTrue(stats1.DistributedMmrs.Count > 0);
        Assert.AreSame(stats1, stats2);
        Assert.AreSame(stats1, stats3);
    }

    [Test]
    public async Task GetDistributions_CacheExpires()
    {
        // Use a short TTL for this test
        var shortTtlHandler = new MmrDistributionHandler(_playerRepoMock.Object, TimeSpan.FromSeconds(1));
        var mmrs = new List<int> { 3000, 2500, 2000 };
        _playerRepoMock.Setup(r => r.LoadMmrs(It.IsAny<int>(), It.IsAny<GateWay>(), It.IsAny<GameMode>()))
            .ReturnsAsync(mmrs);

        var stats1 = await shortTtlHandler.GetDistributions(2, GateWay.Europe, GameMode.GM_1v1);

        // Wait for cache to expire
        await Task.Delay(TimeSpan.FromSeconds(2));
        _playerRepoMock.Setup(r => r.LoadMmrs(It.IsAny<int>(), It.IsAny<GateWay>(), It.IsAny<GameMode>()))
            .ReturnsAsync(new List<int> { 100 });

        var stats2 = await shortTtlHandler.GetDistributions(2, GateWay.Europe, GameMode.GM_1v1);

        Assert.AreNotSame(stats1, stats2);
    }

    [Test]
    public async Task GetPercentileMmr_ReturnsHighestForTop2Percentile()
    {
        var mmrs = new List<int> { 3000, 2500, 2000, 1500, 1000, 500 };
        _playerRepoMock.Setup(r => r.LoadMmrs(It.IsAny<int>(), It.IsAny<GateWay>(), It.IsAny<GameMode>()))
            .ReturnsAsync(mmrs);

        var result1 = await _handler.GetPercentileMmr(1, GateWay.Europe, GameMode.GM_1v1, 0, 2);
        Assert.AreEqual(3000, result1.maxMmr);
        Assert.AreEqual(3000, result1.minMmr); // Only one value in top 2% for 6 items

        var result2 = await _handler.GetPercentileMmr(1, GateWay.Europe, GameMode.GM_1v1, 50, 100);
        Assert.AreEqual(500, result2.minMmr);
        Assert.AreEqual(1500, result2.maxMmr);
    }

    [Test]
    public async Task GetPercentileMmr_ReturnsMaxMmrForEmptyList()
    {
        _playerRepoMock.Setup(r => r.LoadMmrs(It.IsAny<int>(), It.IsAny<GateWay>(), It.IsAny<GameMode>()))
            .ReturnsAsync(new List<int>());

        var (minMmr, maxMmr) = await _handler.GetPercentileMmr(1, GateWay.Europe, GameMode.GM_1v1, 0, 2);
        Assert.AreEqual(0, minMmr);
        Assert.AreEqual(W3ChampionsStatisticService.Common.Constants.MmrConstants.MaxMmr, maxMmr);
    }
}
