using System.Collections.Generic;
using NUnit.Framework;
using W3ChampionsStatisticService.Friends;

namespace WC3ChampionsStatisticService.Tests.Friend;

/// <summary>The in-memory half of <see cref="FriendCommandHandler.AddFriend"/>: Friends and the stored
/// BlockedBattleTags stay disjoint, so becoming friends drops this list's own block of the new friend.</summary>
[TestFixture]
public class FriendCommandHandlerBefriendTests
{
    [Test]
    public void AddsFriendAndRemovesOwnBlockOfThatPlayer_KeepsOtherBlocks()
    {
        var list = new Friendlist("User#1");
        list.BlockedBattleTags.AddRange(new[] { "Foe#2", "New#3", "Foe#4" });

        FriendCommandHandler.Befriend(list, "New#3");

        Assert.That(list.Friends, Is.EqualTo(new List<string> { "New#3" }));
        Assert.That(list.BlockedBattleTags, Is.EqualTo(new List<string> { "Foe#2", "Foe#4" }));
    }

    [Test]
    public void AlreadyFriend_IsIdempotent()
    {
        var list = new Friendlist("User#1");
        list.Friends.Add("New#3");

        FriendCommandHandler.Befriend(list, "New#3");

        Assert.That(list.Friends, Is.EqualTo(new List<string> { "New#3" }));
        Assert.That(list.BlockedBattleTags, Is.Empty);
    }

    [Test]
    public void LegacyNullBlockedList_DoesNotThrow()
    {
        var list = new Friendlist("User#1") { BlockedBattleTags = null };

        FriendCommandHandler.Befriend(list, "New#3");

        Assert.That(list.Friends, Is.EqualTo(new List<string> { "New#3" }));
        Assert.That(list.BlockedBattleTags, Is.Null);
    }

    [Test]
    public void RemovesOwnBlockCaseInsensitively_IncludingEveryLegacyCaseVariant()
    {
        var list = new Friendlist("User#1");
        list.BlockedBattleTags.AddRange(new[] { "new#3", "Foe#2", "NEW#3" });

        FriendCommandHandler.Befriend(list, "New#3");

        Assert.That(list.BlockedBattleTags, Is.EqualTo(new List<string> { "Foe#2" }));
    }
}
