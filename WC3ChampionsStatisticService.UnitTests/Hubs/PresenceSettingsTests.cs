using System;
using NUnit.Framework;
using W3ChampionsStatisticService.Hubs;

namespace WC3ChampionsStatisticService.Tests.Hubs;

// Direct coverage for PresenceSettings.FromEnvironment()'s bool.TryParse-based env-var
// parsing. This project runs NUnit tests sequentially (no [Parallelizable] attributes or
// .runsettings parallelization found in this solution), so mutating the process-global
// RETIRE_FRIEND_ONLINE_STATUS env var per-test and resetting it in [TearDown] is safe and
// will not race other fixtures running concurrently.
[TestFixture]
public class PresenceSettingsTests
{
    private const string EnvVarName = "RETIRE_FRIEND_ONLINE_STATUS";

    [TearDown]
    public void TearDown()
    {
        Environment.SetEnvironmentVariable(EnvVarName, null);
    }

    [Test]
    public void FromEnvironment_VarAbsent_DefaultsToFalse()
    {
        // Arrange
        Environment.SetEnvironmentVariable(EnvVarName, null);

        // Act
        var settings = PresenceSettings.FromEnvironment();

        // Assert
        Assert.That(settings.RetireFriendOnlineStatus, Is.False);
    }

    [TestCase("true", true)]
    [TestCase("True", true)]
    [TestCase("TRUE", true)]
    [TestCase("false", false)]
    [TestCase("False", false)]
    [TestCase("1", false)]
    [TestCase("yes", false)]
    [TestCase("", false)]
    public void FromEnvironment_ParsesVarValue(string envValue, bool expected)
    {
        // Arrange
        Environment.SetEnvironmentVariable(EnvVarName, envValue);

        // Act
        var settings = PresenceSettings.FromEnvironment();

        // Assert
        Assert.That(settings.RetireFriendOnlineStatus, Is.EqualTo(expected));
    }
}
