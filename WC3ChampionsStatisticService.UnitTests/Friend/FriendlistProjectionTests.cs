using System.Collections.Generic;
using NUnit.Framework;
using W3ChampionsStatisticService.Friends;

namespace WC3ChampionsStatisticService.Tests.Friend;

[TestFixture]
public class FriendlistProjectionTests
{
    [Test]
    public void RemovesAdminsCaseInsensitively_KeepsOthersInOrder()
    {
        var original = new Friendlist("User#1")
        {
            BlockedBattleTags = new List<string> { "Foe#1", "Admin#2", "Foe#3" }
        };
        // Deliberately a default (case-sensitive) set: the projection must not depend on the caller's comparer.
        var admins = new HashSet<string> { "ADMIN#2" };

        var projected = FriendlistProjection.WithoutBlockedAdmins(original, admins);

        Assert.That(projected.BlockedBattleTags, Is.EqualTo(new List<string> { "Foe#1", "Foe#3" }));
    }

    [Test]
    public void DoesNotMutateInputAndCopiesOtherFields()
    {
        var original = new Friendlist("User#1")
        {
            Friends = new List<string> { "Admin#2", "Pal#4" },
            BlockedBattleTags = new List<string> { "Foe#1", "Admin#2", "Foe#3" },
            BlockAllRequests = true
        };
        var admins = new HashSet<string> { "Admin#2" };

        var projected = FriendlistProjection.WithoutBlockedAdmins(original, admins);

        Assert.That(original.BlockedBattleTags, Is.EqualTo(new List<string> { "Foe#1", "Admin#2", "Foe#3" }));
        Assert.That(projected, Is.Not.SameAs(original));
        Assert.That(projected.Friends, Is.Not.SameAs(original.Friends));
        Assert.That(projected.BlockedBattleTags, Is.Not.SameAs(original.BlockedBattleTags));
        Assert.That(projected.Id, Is.EqualTo("User#1"));
        Assert.That(projected.Friends, Is.EqualTo(new List<string> { "Admin#2", "Pal#4" }));
        Assert.That(projected.BlockAllRequests, Is.True);
    }

    [Test]
    public void NullFriendlist_ReturnsNull()
    {
        Assert.That(FriendlistProjection.WithoutBlockedAdmins(null, new HashSet<string> { "Admin#2" }), Is.Null);
    }

    [Test]
    public void NullListsOnLegacyDoc_StayNull()
    {
        var original = new Friendlist("legacy#1") { Friends = null, BlockedBattleTags = null };

        var projected = FriendlistProjection.WithoutBlockedAdmins(original, new HashSet<string> { "Admin#2" });

        Assert.That(projected.Friends, Is.Null);
        Assert.That(projected.BlockedBattleTags, Is.Null);
    }
}
