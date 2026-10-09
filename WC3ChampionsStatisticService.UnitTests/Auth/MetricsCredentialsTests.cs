using NUnit.Framework;
using W3ChampionsStatisticService.WebApi.Authorization;

namespace WC3ChampionsStatisticService.Tests.Auth;

[TestFixture]
public class MetricsCredentialsTests
{
    [Test]
    public void MatchingCredentials_Accepted()
    {
        Assert.IsTrue(BasicAuthConfiguration.CredentialsMatch("admin", "admin", "admin", "admin"));
    }

    [TestCase("admin", "wrong")]
    [TestCase("wrong", "admin")]
    [TestCase("admin", "admi")]
    [TestCase("admin", "")]
    [TestCase(null, null)]
    public void MismatchingCredentials_Rejected(string username, string password)
    {
        Assert.IsFalse(BasicAuthConfiguration.CredentialsMatch("admin", "admin", username, password));
    }
}
