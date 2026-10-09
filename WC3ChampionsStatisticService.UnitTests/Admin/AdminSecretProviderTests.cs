using System;
using System.Net.Http;
using NUnit.Framework;
using W3C.Domain;

namespace WC3ChampionsStatisticService.Tests.Admin;

[TestFixture]
[NonParallelizable]
public class AdminSecretProviderTests
{
    private const string PublicTestSecret = "300C018C-6321-4BAB-B289-9CB3DB760CBB";

    private string _previous;

    [SetUp]
    public void Remember() => _previous = Environment.GetEnvironmentVariable(AdminSecretProvider.VariableName);

    [TearDown]
    public void Restore() => Environment.SetEnvironmentVariable(AdminSecretProvider.VariableName, _previous);

    [TestCase(null)]
    [TestCase("")]
    public void WhenUnsetOrEmpty_IsNotConfiguredAndSendsNoHeaderNorTheKnownTestSecret(string value)
    {
        Environment.SetEnvironmentVariable(AdminSecretProvider.VariableName, value);
        using var request = new HttpRequestMessage();

        AdminSecretProvider.AddTo(request.Headers);

        Assert.That(AdminSecretProvider.Value, Is.Null);
        Assert.That(AdminSecretProvider.IsConfigured, Is.False);
        Assert.That(request.Headers.Contains(AdminSecretProvider.HeaderName), Is.False);
        Assert.That(request.Headers.ToString(), Does.Not.Contain(PublicTestSecret));
    }

    [Test]
    public void WhenSet_SendsExactlyThatValueInTheHeader()
    {
        Environment.SetEnvironmentVariable(AdminSecretProvider.VariableName, "from-env");
        using var request = new HttpRequestMessage();

        AdminSecretProvider.AddTo(request.Headers);

        Assert.That(AdminSecretProvider.IsConfigured, Is.True);
        Assert.That(request.Headers.GetValues(AdminSecretProvider.HeaderName), Is.EqualTo(new[] { "from-env" }));
    }
}
