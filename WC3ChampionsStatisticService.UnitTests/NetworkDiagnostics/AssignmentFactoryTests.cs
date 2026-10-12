using System;
using NUnit.Framework;
using W3ChampionsStatisticService.NetworkDiagnostics.Runs;

namespace WC3ChampionsStatisticService.Tests.NetworkDiagnostics;

[TestFixture]
public class AssignmentFactoryTests
{
    [Test]
    public void Create_SetsPendingRandomTokenAndRetention()
    {
        var after = new DateTime(2026, 10, 12, 12, 0, 0, DateTimeKind.Utc);

        var a = AssignmentFactory.Create("run-1", "Name#1234", after, after.AddDays(14));
        var b = AssignmentFactory.Create("run-1", "Other#1234", after, after.AddDays(14));

        Assert.IsTrue(Guid.TryParse(a.Id, out _));
        Assert.AreEqual(AssignmentStatus.Pending, a.Status);
        Assert.That(a.Token, Does.Match("^[A-Za-z0-9_-]{43}$"), "A10: 256-bit base64url token stored on the assignment");
        Assert.AreNotEqual(a.Token, b.Token);
        Assert.AreEqual(after.AddDays(14 + 365), a.ExpiresAt);
        Assert.IsNull(a.DeliveredAt);
        Assert.IsNull(a.SubmittedAt);
    }

    // A10: the token is never logged. A record's generated ToString prints every property, and an assignment
    // passed as a log argument (or shown in a debugger dump) would leak it, so ToString leaves it out.
    [Test]
    public void ToString_NeverContainsTheToken()
    {
        var a = AssignmentFactory.Create("run-1", "Name#1234", DateTime.UtcNow, DateTime.UtcNow.AddDays(1));

        StringAssert.DoesNotContain(a.Token, a.ToString());
        StringAssert.Contains(a.Id, a.ToString());
    }
}
