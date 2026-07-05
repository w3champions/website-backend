using NUnit.Framework;
using W3C.Domain.ChatService;

namespace WC3ChampionsStatisticService.Tests.Friend;

// Ctor-level tests only (mirrors PresenceSettingsTests style): FromEnvironment() itself is a
// one-line env read and is intentionally NOT exercised via env-var mutation here.
[TestFixture]
public class ChatPingSettingsTests
{
    [Test]
    public void Enabled_WhenUrlAndSecretPresent_IsTrue()
    {
        var settings = new ChatPingSettings("https://chat.example.com", "secret-value");

        Assert.That(settings.Enabled, Is.True);
        Assert.That(settings.ChatApiUrl, Is.EqualTo("https://chat.example.com"));
        Assert.That(settings.Secret, Is.EqualTo("secret-value"));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void Enabled_WhenSecretBlank_IsFalse(string blankSecret)
    {
        var settings = new ChatPingSettings("https://chat.example.com", blankSecret);

        Assert.That(settings.Enabled, Is.False);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void Enabled_WhenUrlBlank_IsFalse(string blankUrl)
    {
        var settings = new ChatPingSettings(blankUrl, "secret-value");

        Assert.That(settings.Enabled, Is.False);
    }
}
