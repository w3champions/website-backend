using NUnit.Framework;
using W3ChampionsStatisticService.ReplayStore;

namespace WC3ChampionsStatisticService.Tests.ReplayStore;

[TestFixture]
public class ReplayStoreUrlsTests
{
    [TestCase("https://w3champions.com", "https://w3champions.com")]
    [TestCase("https://www.w3champions.com", "https://www.w3champions.com")]
    [TestCase("https://test.w3champions.com", "https://test.w3champions.com")]
    [TestCase("http://localhost:8080", "http://localhost:8080")]
    [TestCase("http://127.0.0.1:5291", "http://127.0.0.1:5291")]
    public void Website_IsTheOriginOfAW3ChampionsOrLocalSite(string origin, string website)
    {
        Assert.AreEqual(website, ReplayStoreUrls.Website(origin));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("null")]
    [TestCase("http://www.w3champions.com")]
    [TestCase("https://w3champions.com.evil.example")]
    [TestCase("https://evilw3champions.com")]
    [TestCase("https://example.com")]
    [TestCase("ftp://localhost")]
    public void Website_IsNullForOtherOrigins(string origin)
    {
        Assert.IsNull(ReplayStoreUrls.Website(origin));
    }
}
