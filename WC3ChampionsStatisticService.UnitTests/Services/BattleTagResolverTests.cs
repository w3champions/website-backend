using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Moq;
using NUnit.Framework;
using W3ChampionsStatisticService.Services;

namespace WC3ChampionsStatisticService.Tests.Services;

[TestFixture]
public class BattleTagResolverTests
{
    private Mock<IdentityServiceClient> _identityClientMock;
    private IMemoryCache _cache;
    private BattleTagResolver _resolver;

    [SetUp]
    public void SetUp()
    {
        _identityClientMock = new Mock<IdentityServiceClient>(new System.Net.Http.HttpClient());
        _cache = new MemoryCache(new MemoryCacheOptions());
        _resolver = new BattleTagResolver(_identityClientMock.Object, _cache);
    }

    [Test]
    public async Task ResolveCanonical_HitsIdentityServiceOnFirstCall()
    {
        _identityClientMock
            .Setup(c => c.ResolveCanonicalBattleTag("torren#11438"))
            .ReturnsAsync("TORREN#11438");

        var result = await _resolver.ResolveCanonical("torren#11438");

        Assert.AreEqual("TORREN#11438", result);
        _identityClientMock.Verify(c => c.ResolveCanonicalBattleTag("torren#11438"), Times.Once);
    }

    [Test]
    public async Task ResolveCanonical_ReturnsCachedValueOnSecondCall()
    {
        _identityClientMock
            .Setup(c => c.ResolveCanonicalBattleTag(It.IsAny<string>()))
            .ReturnsAsync("TORREN#11438");

        await _resolver.ResolveCanonical("torren#11438");
        await _resolver.ResolveCanonical("Torren#11438");
        await _resolver.ResolveCanonical("TORREN#11438");

        _identityClientMock.Verify(c => c.ResolveCanonicalBattleTag(It.IsAny<string>()), Times.Once);
    }

    [Test]
    public async Task ResolveCanonical_NullForNonexistentUser()
    {
        _identityClientMock
            .Setup(c => c.ResolveCanonicalBattleTag("nonexistent#9999"))
            .ReturnsAsync((string)null);

        var result = await _resolver.ResolveCanonical("nonexistent#9999");

        Assert.IsNull(result);
    }

    [Test]
    public async Task ResolveCanonical_CachesNegativeResults()
    {
        _identityClientMock
            .Setup(c => c.ResolveCanonicalBattleTag(It.IsAny<string>()))
            .ReturnsAsync((string)null);

        await _resolver.ResolveCanonical("nonexistent#9999");
        await _resolver.ResolveCanonical("nonexistent#9999");

        _identityClientMock.Verify(c => c.ResolveCanonicalBattleTag(It.IsAny<string>()), Times.Once);
    }

    [Test]
    public async Task ResolveCanonicalBatch_MixedHitsAndMisses_ReturnsMergedDictionary()
    {
        _identityClientMock
            .Setup(c => c.ResolveCanonicalBattleTag("torren#11438"))
            .ReturnsAsync("TORREN#11438");
        _identityClientMock
            .Setup(c => c.ResolveCanonicalBattleTag("faro#2494"))
            .ReturnsAsync("Faro#2494");
        _identityClientMock
            .Setup(c => c.ResolveCanonicalBattleTag("ghost#0000"))
            .ReturnsAsync((string)null);

        var inputs = new[] { "torren#11438", "faro#2494", "ghost#0000" };
        var result = await _resolver.ResolveCanonicalBatch(inputs);

        Assert.AreEqual("TORREN#11438", result["torren#11438"]);
        Assert.AreEqual("Faro#2494", result["faro#2494"]);
        Assert.IsNull(result["ghost#0000"]);
    }

    [Test]
    public async Task ResolveCanonicalBatch_AllCacheHits_DoesNotHitIdentityService()
    {
        _identityClientMock
            .Setup(c => c.ResolveCanonicalBattleTag(It.IsAny<string>()))
            .ReturnsAsync("TORREN#11438");

        await _resolver.ResolveCanonical("torren#11438");
        _identityClientMock.Reset();

        var result = await _resolver.ResolveCanonicalBatch(new[] { "torren#11438", "TORREN#11438" });

        Assert.AreEqual("TORREN#11438", result["torren#11438"]);
        Assert.AreEqual("TORREN#11438", result["TORREN#11438"]);
        _identityClientMock.Verify(c => c.ResolveCanonicalBattleTag(It.IsAny<string>()), Times.Never);
    }
}
