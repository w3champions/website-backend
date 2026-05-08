using NUnit.Framework;
using W3ChampionsStatisticService.Rewards.Providers.Patreon;

namespace WC3ChampionsStatisticService.UnitTests.Rewards;

[TestFixture]
public class PatreonApiClientTests
{
    [TestCase("active_patron", "Paid", true)]
    [TestCase("active_patron", "Pending", true)] // NEW
    [TestCase("active_patron", "Free Trial", true)] // NEW
    [TestCase("active_patron", null, false)] // free-tier members have null status
    [TestCase("active_patron", "Declined", false)]
    [TestCase("active_patron", "Refunded", false)]
    [TestCase("active_patron", "Fraud", false)]
    [TestCase("active_patron", "Other", false)]
    [TestCase("active_patron", "Deleted", false)]
    [TestCase("former_patron", "Paid", false)]
    [TestCase("declined_patron", "Declined", false)]
    [TestCase(null, null, false)]
    public void IsActivePatron_TruthTable(string patronStatus, string lastChargeStatus, bool expected)
    {
        var member = new PatreonMember
        {
            PatronStatus = patronStatus,
            LastChargeStatus = lastChargeStatus
        };
        Assert.AreEqual(expected, member.IsActivePatron);
    }
}
