using NUnit.Framework;
using W3ChampionsStatisticService.WebApi.ActionFilters;

namespace WC3ChampionsStatisticService.Tests.Friend;

// Ctor-level tests only (mirrors ChatPingSettingsTests style): FromEnvironment() itself is a
// one-line env read and is intentionally NOT exercised via env-var mutation here.
[TestFixture]
public class ChatRelationshipsAuthSettingsTests
{
    [Test]
    public void Configured_WhenSecretPresent_IsTrue()
    {
        var settings = new ChatRelationshipsAuthSettings("s3cret");

        Assert.That(settings.Configured, Is.True);
        Assert.That(settings.Secret, Is.EqualTo("s3cret"));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void Configured_WhenSecretBlank_IsFalse(string blankSecret)
    {
        var settings = new ChatRelationshipsAuthSettings(blankSecret);

        Assert.That(settings.Configured, Is.False);
    }
}
