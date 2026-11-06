using FluentAssertions;
using MongoDB.Bson;
using NUnit.Framework;
using W3ChampionsStatisticService.PersonalSettings.Hotkeys;
using W3ChampionsStatisticService.Services.Tracing;

namespace WC3ChampionsStatisticService.Tests.PersonalSettings.Hotkeys;

[TestFixture]
public class MongoTracingFilterTests
{
    [TestCase("insert")]
    [TestCase("update")]
    [TestCase("find")]
    [TestCase("findAndModify")]
    [TestCase("delete")]
    public void ShouldTrace_HotkeySetupCollection_IsFalse(string commandName)
    {
        var command = new BsonDocument { { commandName, nameof(HotkeySetup) }, { "$db", "w3c" } };

        MongoTracingFilter.ShouldTrace(command).Should().BeFalse();
    }

    [Test]
    public void ShouldTrace_OtherCollection_IsTrue()
    {
        MongoTracingFilter.ShouldTrace(new BsonDocument { { "insert", "PersonalSetting" } }).Should().BeTrue();
    }

    [Test]
    public void ShouldTrace_AdminCommandWithoutCollection_IsTrue()
    {
        MongoTracingFilter.ShouldTrace(new BsonDocument { { "ping", 1 } }).Should().BeTrue();
        MongoTracingFilter.ShouldTrace(new BsonDocument()).Should().BeTrue();
        MongoTracingFilter.ShouldTrace(null).Should().BeTrue();
    }
}
