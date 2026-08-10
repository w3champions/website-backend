using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using W3C.Domain.ChatService;
using W3ChampionsStatisticService.Clans;
using W3ChampionsStatisticService.PersonalSettings;
using W3ChampionsStatisticService.Ports;

namespace WC3ChampionsStatisticService.Tests.PersonalSettings;

[TestFixture]
public class FlairNotifyingRepositoryTests
{
    private Mock<IPersonalSettingsRepository> _settingsInner;
    private Mock<IClanRepository> _clanInner;
    private Mock<IFlairChangeNotifier> _notifier;
    private List<string> _notified;

    [SetUp]
    public void SetUp()
    {
        _settingsInner = new Mock<IPersonalSettingsRepository>();
        _clanInner = new Mock<IClanRepository>();
        _notifier = new Mock<IFlairChangeNotifier>();
        _notified = new List<string>();
        _notifier.Setup(n => n.NotifyChanged(It.IsAny<IReadOnlyCollection<string>>()))
            .Callback((IReadOnlyCollection<string> tags) => _notified.AddRange(tags));
    }

    private FlairNotifyingPersonalSettingsRepository Settings() =>
        new(_settingsInner.Object, _notifier.Object);

    private FlairNotifyingClanRepository Clans() =>
        new(_clanInner.Object, _notifier.Object);

    [Test]
    public async Task Save_NotifiesTheSavedBattleTag()
    {
        await Settings().Save(new PersonalSetting("peter#123"));

        Assert.That(_notified, Is.EqualTo(new[] { "peter#123" }));
    }

    [Test]
    public async Task SaveMany_NotifiesEveryBattleTag()
    {
        await Settings().SaveMany(new List<PersonalSetting>
        {
            new("peter#123"),
            new("alice#456"),
        });

        Assert.That(_notified, Is.EqualTo(new[] { "peter#123", "alice#456" }));
    }

    [Test]
    public async Task UpsertMemberShip_NotifiesTheMember()
    {
        await Clans().UpsertMemberShip(new ClanMembership { BattleTag = "peter#123", ClanId = "W3C" });

        Assert.That(_notified, Is.EqualTo(new[] { "peter#123" }));
    }

    [Test]
    public async Task SaveMemberShips_NotifiesEveryMember()
    {
        // This is the bulk clan-delete path: ClanCommandHandler.DeleteClan persists every former member
        // through SaveMemberShips, so covering this one method covers the whole clan teardown.
        await Clans().SaveMemberShips(new List<ClanMembership>
        {
            new() { BattleTag = "peter#123" },
            new() { BattleTag = "alice#456" },
            new() { BattleTag = "bob#789" },
        });

        Assert.That(_notified, Is.EqualTo(new[] { "peter#123", "alice#456", "bob#789" }));
    }

    [Test]
    public void Save_WhenTheInnerWriteThrows_DoesNotNotify()
    {
        _settingsInner.Setup(r => r.Save(It.IsAny<PersonalSetting>())).ThrowsAsync(new InvalidOperationException("db down"));

        Assert.ThrowsAsync<InvalidOperationException>(() => Settings().Save(new PersonalSetting("peter#123")));
        Assert.That(_notified, Is.Empty);
    }

    [Test]
    public void UpsertMemberShip_WhenTheInnerWriteThrows_DoesNotNotify()
    {
        _clanInner.Setup(r => r.UpsertMemberShip(It.IsAny<ClanMembership>())).ThrowsAsync(new InvalidOperationException("db down"));

        Assert.ThrowsAsync<InvalidOperationException>(() => Clans().UpsertMemberShip(new ClanMembership { BattleTag = "peter#123" }));
        Assert.That(_notified, Is.Empty);
    }

    [Test]
    public void Save_WhenTheNotifierThrows_TheWriteStillSucceeds()
    {
        // A broken notifier must never cost a player their settings save.
        _notifier.Setup(n => n.NotifyChanged(It.IsAny<IReadOnlyCollection<string>>()))
            .Throws(new InvalidOperationException("notifier exploded"));

        Assert.DoesNotThrowAsync(() => Settings().Save(new PersonalSetting("peter#123")));
        _settingsInner.Verify(r => r.Save(It.IsAny<PersonalSetting>()), Times.Once);
    }

    [Test]
    public void SaveMemberShips_WhenTheNotifierThrows_TheWriteStillSucceeds()
    {
        _notifier.Setup(n => n.NotifyChanged(It.IsAny<IReadOnlyCollection<string>>()))
            .Throws(new InvalidOperationException("notifier exploded"));

        Assert.DoesNotThrowAsync(() => Clans().SaveMemberShips(new List<ClanMembership>
        {
            new() { BattleTag = "peter#123" },
        }));
        _clanInner.Verify(r => r.SaveMemberShips(It.IsAny<List<ClanMembership>>()), Times.Once);
    }

    [Test]
    public async Task ReadMethods_AreForwardedAndDoNotNotify()
    {
        _settingsInner.Setup(r => r.Load("peter#123")).ReturnsAsync(new PersonalSetting("peter#123"));

        var loaded = await Settings().Load("peter#123");

        Assert.That(loaded.Id, Is.EqualTo("peter#123"));
        _settingsInner.Verify(r => r.Load("peter#123"), Times.Once);
        Assert.That(_notified, Is.Empty);
    }

    [Test]
    public async Task LoadOrCreate_WhenNoSettingsExist_CreatesAndNotifiesTheBattleTag()
    {
        _settingsInner.Setup(r => r.Load("peter#123")).ReturnsAsync((PersonalSetting)null);
        _settingsInner.Setup(r => r.LoadOrCreate("peter#123")).ReturnsAsync(new PersonalSetting("peter#123"));

        var created = await Settings().LoadOrCreate("peter#123");

        Assert.That(created.Id, Is.EqualTo("peter#123"));
        _settingsInner.Verify(r => r.Load("peter#123"), Times.Once);
        _settingsInner.Verify(r => r.LoadOrCreate("peter#123"), Times.Once);
        Assert.That(_notified, Is.EqualTo(new[] { "peter#123" }));
    }

    [Test]
    public async Task LoadOrCreate_WhenSettingsAlreadyExist_LoadsWithoutCreatingOrNotifying()
    {
        _settingsInner.Setup(r => r.Load("peter#123")).ReturnsAsync(new PersonalSetting("peter#123"));

        var loaded = await Settings().LoadOrCreate("peter#123");

        Assert.That(loaded.Id, Is.EqualTo("peter#123"));
        _settingsInner.Verify(r => r.Load("peter#123"), Times.Once);
        _settingsInner.Verify(r => r.LoadOrCreate(It.IsAny<string>()), Times.Never);
        Assert.That(_notified, Is.Empty);
    }

    [Test]
    public async Task DeleteClan_IsForwardedButDoesNotNotifyOnItsOwn()
    {
        // DeleteClan carries no battleTags. Its callers persist the affected memberships through
        // SaveMemberShips, which is where the notification comes from.
        await Clans().DeleteClan("W3C");

        _clanInner.Verify(r => r.DeleteClan("W3C"), Times.Once);
        Assert.That(_notified, Is.Empty);
    }
}
