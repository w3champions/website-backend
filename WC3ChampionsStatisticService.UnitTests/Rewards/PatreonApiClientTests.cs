using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Moq.Protected;
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

[TestFixture]
public class PatreonApiClientCampaignMemberTests
{
    private Mock<HttpMessageHandler> _handlerMock;
    private HttpClient _httpClient;
    private PatreonApiClient _client;

    [SetUp]
    public void SetUp()
    {
        Environment.SetEnvironmentVariable("REWARDS_PATREON_CAMPAIGN_ID", "4973374");
        Environment.SetEnvironmentVariable("REWARDS_PATREON_ACCESS_TOKEN", "test-token");
        _handlerMock = new Mock<HttpMessageHandler>();
        _httpClient = new HttpClient(_handlerMock.Object);
        _client = new PatreonApiClient(_httpClient);
    }

    [TearDown]
    public void TearDown()
    {
        Environment.SetEnvironmentVariable("REWARDS_PATREON_CAMPAIGN_ID", null);
        Environment.SetEnvironmentVariable("REWARDS_PATREON_ACCESS_TOKEN", null);
        _httpClient?.Dispose();
    }

    private void SetupResponseSequence(params string[] jsonBodies)
    {
        var sequence = _handlerMock.Protected().SetupSequence<Task<HttpResponseMessage>>(
            "SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>());
        foreach (var body in jsonBodies)
            sequence = sequence.ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(body) });
    }

    [Test]
    public async Task GetCampaignMemberByPatreonUserId_UserExistsInFirstPage_ReturnsMember()
    {
        var page1 = JsonSerializer.Serialize(new
        {
            data = new[]
            {
                new {
                    type = "member",
                    id = "member-1",
                    attributes = new { patron_status = "active_patron", last_charge_status = "Paid" },
                    relationships = new {
                        user = new { data = new { id = "152572628" } },
                        currently_entitled_tiers = new { data = new[] { new { id = "6482070", type = "tier" } } }
                    }
                }
            },
            included = new[]
            {
                new { type = "tier", id = "6482070", attributes = new { title = "Gold", amount_cents = 1000 } }
            },
            links = new { next = (string)null }
        });
        SetupResponseSequence(page1);

        var member = await _client.GetCampaignMemberByPatreonUserId("152572628");

        Assert.IsNotNull(member);
        Assert.AreEqual("152572628", member.PatreonUserId);
        Assert.AreEqual(1, member.EntitledTiers.Count);
        Assert.AreEqual("6482070", member.EntitledTiers[0].TierId);
        Assert.AreEqual(1000, member.EntitledTiers[0].AmountCents);
    }

    [Test]
    public async Task GetCampaignMemberByPatreonUserId_UserExistsOnPageThree_TraversesPagination()
    {
        var page1 = MakePage(new[] { ("user-a", "tier-a") }, nextLink: "https://www.patreon.com/api/oauth2/v2/campaigns/4973374/members?page[cursor]=page2");
        var page2 = MakePage(new[] { ("user-b", "tier-b") }, nextLink: "https://www.patreon.com/api/oauth2/v2/campaigns/4973374/members?page[cursor]=page3");
        var page3 = MakePage(new[] { ("152572628", "6482070") }, nextLink: null);
        SetupResponseSequence(page1, page2, page3);

        var member = await _client.GetCampaignMemberByPatreonUserId("152572628");

        Assert.IsNotNull(member);
        Assert.AreEqual("152572628", member.PatreonUserId);
    }

    [Test]
    public async Task GetCampaignMemberByPatreonUserId_UserNotInCampaign_ReturnsNull()
    {
        var page1 = MakePage(new[] { ("user-a", "tier-a"), ("user-b", "tier-b") }, nextLink: null);
        SetupResponseSequence(page1);

        var member = await _client.GetCampaignMemberByPatreonUserId("152572628");

        Assert.IsNull(member);
    }

    [Test]
    public async Task GetCampaignMemberByPatreonUserId_EmptyCampaign_ReturnsNull()
    {
        var emptyPage = JsonSerializer.Serialize(new { data = new object[0], included = new object[0], links = new { next = (string)null } });
        SetupResponseSequence(emptyPage);

        var member = await _client.GetCampaignMemberByPatreonUserId("152572628");

        Assert.IsNull(member);
    }

    [Test]
    public async Task GetCampaignMemberByPatreonUserId_PaginationTerminatesOnNullNextLink()
    {
        var page1 = MakePage(new[] { ("user-a", "tier-a") }, nextLink: null);
        SetupResponseSequence(page1);

        var member = await _client.GetCampaignMemberByPatreonUserId("152572628");

        Assert.IsNull(member);
        // Verify only one HTTP call made
        _handlerMock.Protected().Verify("SendAsync", Times.Once(), ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>());
    }

    [Test]
    public void GetCampaignMemberByPatreonUserId_HttpError_Throws()
    {
        _handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.InternalServerError));

        Assert.ThrowsAsync<HttpRequestException>(async () =>
            await _client.GetCampaignMemberByPatreonUserId("152572628"));
    }

    [Test]
    public void GetCampaignMemberByPatreonUserId_MalformedJson_Throws()
    {
        SetupResponseSequence("{ this is not valid json");
        Assert.ThrowsAsync<JsonException>(async () =>
            await _client.GetCampaignMemberByPatreonUserId("152572628"));
    }

    [Test]
    public async Task GetCampaignMemberByPatreonUserId_PopulatesEntitledTiersWithAmountCents()
    {
        var page1 = JsonSerializer.Serialize(new
        {
            data = new[]
            {
                new {
                    type = "member", id = "member-1",
                    attributes = new { patron_status = "active_patron", last_charge_status = "Paid" },
                    relationships = new {
                        user = new { data = new { id = "152572628" } },
                        currently_entitled_tiers = new { data = new[] {
                            new { id = "6482051", type = "tier" },
                            new { id = "6482070", type = "tier" }
                        } }
                    }
                }
            },
            included = new[]
            {
                new { type = "tier", id = "6482051", attributes = new { title = "Bronze", amount_cents = 100 } },
                new { type = "tier", id = "6482070", attributes = new { title = "Gold", amount_cents = 1000 } }
            },
            links = new { next = (string)null }
        });
        SetupResponseSequence(page1);

        var member = await _client.GetCampaignMemberByPatreonUserId("152572628");

        Assert.AreEqual(2, member.EntitledTiers.Count);
        var gold = member.EntitledTiers.Single(t => t.TierId == "6482070");
        Assert.AreEqual(1000, gold.AmountCents);
        Assert.AreEqual("Gold", gold.Title);
    }

    [Test]
    public async Task GetCampaignMemberByPatreonUserId_FreeMembershipPatronStatusNull_ReturnsMember()
    {
        var page1 = JsonSerializer.Serialize(new
        {
            data = new[]
            {
                new {
                    type = "member", id = "member-1",
                    attributes = new { patron_status = (string)null, last_charge_status = (string)null },
                    relationships = new {
                        user = new { data = new { id = "152572628" } },
                        currently_entitled_tiers = new { data = new[] { new { id = "free-tier", type = "tier" } } }
                    }
                }
            },
            included = new[] { new { type = "tier", id = "free-tier", attributes = new { title = "Free", amount_cents = 0 } } },
            links = new { next = (string)null }
        });
        SetupResponseSequence(page1);

        var member = await _client.GetCampaignMemberByPatreonUserId("152572628");

        Assert.IsNotNull(member);
        Assert.IsNull(member.PatronStatus);
        Assert.IsFalse(member.IsActivePatron, "Free members are not active patrons.");
    }

    private string MakePage((string userId, string tierId)[] members, string nextLink)
    {
        return JsonSerializer.Serialize(new
        {
            data = members.Select((m, i) => new
            {
                type = "member",
                id = $"member-{i}",
                attributes = new { patron_status = "active_patron", last_charge_status = "Paid" },
                relationships = new
                {
                    user = new { data = new { id = m.userId } },
                    currently_entitled_tiers = new { data = new[] { new { id = m.tierId, type = "tier" } } }
                }
            }).ToArray(),
            included = members.Select(m => new { type = "tier", id = m.tierId, attributes = new { title = "X", amount_cents = 100 } }).ToArray(),
            links = new { next = nextLink }
        });
    }
}
