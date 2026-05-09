using System;
using System.Collections.Generic;
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
using WC3ChampionsStatisticService.UnitTests.Rewards.Fixtures;

namespace WC3ChampionsStatisticService.UnitTests.Rewards;

[TestFixture]
public class PatreonApiClientTests
{
    [TestCase("active_patron", "Paid", true)]
    [TestCase("active_patron", "Pending", true)] // NEW
    [TestCase("active_patron", "Free Trial", true)] // NEW
    [TestCase("ACTIVE_PATRON", "PAID", true)] // casing-drift guard
    [TestCase("active_patron", "paid", true)] // casing-drift guard
    [TestCase("active_patron", "free trial", true)] // casing-drift guard
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

/// <summary>
/// Tests for PatreonApiClient.ParseMemberData (made internal for testability).
/// Tests build PatreonApiData objects programmatically using JsonSerializer to produce
/// the JsonElement values that ParseMemberData reads via dictionary deserialization.
/// </summary>
[TestFixture]
public class ParseMemberDataTests
{
    private HttpClient _httpClient;
    private PatreonApiClient _client;

    [SetUp]
    public void SetUp()
    {
        Environment.SetEnvironmentVariable("REWARDS_PATREON_CAMPAIGN_ID", "4973374");
        Environment.SetEnvironmentVariable("REWARDS_PATREON_ACCESS_TOKEN", "test-token");
        _httpClient = new HttpClient(new Mock<HttpMessageHandler>().Object);
        _client = new PatreonApiClient(_httpClient);
    }

    [TearDown]
    public void TearDown()
    {
        Environment.SetEnvironmentVariable("REWARDS_PATREON_CAMPAIGN_ID", null);
        Environment.SetEnvironmentVariable("REWARDS_PATREON_ACCESS_TOKEN", null);
        _httpClient?.Dispose();
    }

    // Deserializes a JSON string to PatreonApiData using the same options as production code.
    private static PatreonApiData DeserializeMemberData(string json)
    {
        return JsonSerializer.Deserialize<PatreonApiData>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }

    private static List<PatreonApiData> DeserializeIncluded(string json)
    {
        return JsonSerializer.Deserialize<List<PatreonApiData>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 1: Real members-create payload — extract all key fields
    // ──────────────────────────────────────────────────────────────────────────
    [Test]
    public void ParseMemberData_RealMembersCreatePayload_ExtractsAllFields()
    {
        // The real members-create.json is a webhook envelope: the member is in data{}
        // and tier details are in included[]. We parse data{} as the member element.
        var rawJson = PatreonPayloadLoader.MembersCreateJson();
        var root = JsonSerializer.Deserialize<JsonElement>(rawJson);

        var memberJson = root.GetProperty("data").GetRawText();
        var includedJson = root.GetProperty("included").GetRawText();

        var memberData = DeserializeMemberData(memberJson);
        var included = DeserializeIncluded(includedJson);

        var member = _client.ParseMemberData(memberData, included);

        Assert.IsNotNull(member);
        Assert.AreEqual("d29e61b6-f73b-4662-955e-dade715cef83", member.Id);
        Assert.AreEqual("active_patron", member.PatronStatus);
        Assert.IsNull(member.LastChargeStatus, "Fresh signup has null last_charge_status");
        Assert.AreEqual("108692085", member.PatreonUserId);
        Assert.IsNotNull(member.EntitledTiers);
        Assert.AreEqual(1, member.EntitledTiers.Count);
        Assert.AreEqual("196241737", member.EntitledTiers[0].TierId);
        Assert.AreEqual(1337, member.EntitledTiers[0].AmountCents);
        Assert.AreEqual("Elite Tier", member.EntitledTiers[0].Title);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 2: amount_cents populated from matching included tier resource
    // ──────────────────────────────────────────────────────────────────────────
    [Test]
    public void ParseMemberData_AmountCentsPopulatedFromIncludedTier_AssociatedWithTierId()
    {
        var memberJson = JsonSerializer.Serialize(new
        {
            id = "member-1",
            type = "member",
            attributes = new { patron_status = "active_patron", last_charge_status = "Paid" },
            relationships = new
            {
                user = new { data = new { id = "user-99", type = "user" } },
                currently_entitled_tiers = new { data = new[] { new { id = "tier-42", type = "tier" } } }
            }
        });

        var includedJson = JsonSerializer.Serialize(new[]
        {
            new { id = "tier-42", type = "tier", attributes = new { title = "Silver", amount_cents = 500 } }
        });

        var member = _client.ParseMemberData(DeserializeMemberData(memberJson), DeserializeIncluded(includedJson));

        Assert.IsNotNull(member);
        Assert.AreEqual(1, member.EntitledTiers.Count);
        Assert.AreEqual("tier-42", member.EntitledTiers[0].TierId);
        Assert.AreEqual(500, member.EntitledTiers[0].AmountCents);
        Assert.AreEqual("Silver", member.EntitledTiers[0].Title);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 3: Tier ID in currently_entitled_tiers but not in included[] → null amount/title
    // ──────────────────────────────────────────────────────────────────────────
    [Test]
    public void ParseMemberData_TierResourceMissingFromIncluded_ProducesEntitledTierWithNullAmount()
    {
        var memberJson = JsonSerializer.Serialize(new
        {
            id = "member-1",
            type = "member",
            attributes = new { patron_status = "active_patron", last_charge_status = "Paid" },
            relationships = new
            {
                user = new { data = new { id = "user-1", type = "user" } },
                currently_entitled_tiers = new { data = new[] { new { id = "tier-missing", type = "tier" } } }
            }
        });

        // included[] has a different tier, not the one referenced above
        var includedJson = JsonSerializer.Serialize(new[]
        {
            new { id = "tier-other", type = "tier", attributes = new { title = "Other", amount_cents = 200 } }
        });

        var member = _client.ParseMemberData(DeserializeMemberData(memberJson), DeserializeIncluded(includedJson));

        Assert.IsNotNull(member);
        Assert.AreEqual(1, member.EntitledTiers.Count);
        Assert.AreEqual("tier-missing", member.EntitledTiers[0].TierId);
        Assert.IsNull(member.EntitledTiers[0].AmountCents, "No matching included[] resource → AmountCents must be null");
        Assert.IsNull(member.EntitledTiers[0].Title, "No matching included[] resource → Title must be null");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 4: null patron_status preserved (free-tier member)
    // ──────────────────────────────────────────────────────────────────────────
    [Test]
    public void ParseMemberData_NullPatronStatus_PreservedAsNull()
    {
        var memberJson = JsonSerializer.Serialize(new
        {
            id = "member-free",
            type = "member",
            attributes = new { patron_status = (string)null, last_charge_status = (string)null },
            relationships = new
            {
                user = new { data = new { id = "user-free", type = "user" } },
                currently_entitled_tiers = new { data = Array.Empty<object>() }
            }
        });

        var member = _client.ParseMemberData(DeserializeMemberData(memberJson), new List<PatreonApiData>());

        Assert.IsNotNull(member);
        Assert.IsNull(member.PatronStatus, "Free-tier member patron_status must be null");
        Assert.IsFalse(member.IsActivePatron, "Free-tier member is not an active patron");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 5: null last_charge_status preserved (fresh signup)
    // ──────────────────────────────────────────────────────────────────────────
    [Test]
    public void ParseMemberData_NullLastChargeStatus_PreservedAsNull()
    {
        var rawJson = PatreonPayloadLoader.MembersCreateJson();
        var root = JsonSerializer.Deserialize<JsonElement>(rawJson);

        var memberData = DeserializeMemberData(root.GetProperty("data").GetRawText());
        var included = DeserializeIncluded(root.GetProperty("included").GetRawText());

        var member = _client.ParseMemberData(memberData, included);

        Assert.IsNotNull(member);
        Assert.AreEqual("active_patron", member.PatronStatus);
        Assert.IsNull(member.LastChargeStatus, "Fresh signup: last_charge_status is null before first charge");
        Assert.IsFalse(member.IsActivePatron, "active_patron + null last_charge_status → not IsActivePatron");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 6: Multiple tiers all populated with correct amounts from included[]
    // ──────────────────────────────────────────────────────────────────────────
    [Test]
    public void ParseMemberData_MultipleTiers_AllPopulatedWithCorrectAmounts()
    {
        var memberJson = JsonSerializer.Serialize(new
        {
            id = "member-multi",
            type = "member",
            attributes = new { patron_status = "active_patron", last_charge_status = "Paid" },
            relationships = new
            {
                user = new { data = new { id = "user-1", type = "user" } },
                currently_entitled_tiers = new
                {
                    data = new[]
                    {
                        new { id = "tier-bronze", type = "tier" },
                        new { id = "tier-silver", type = "tier" },
                        new { id = "tier-gold",   type = "tier" }
                    }
                }
            }
        });

        var includedJson = JsonSerializer.Serialize(new[]
        {
            new { id = "tier-bronze", type = "tier", attributes = new { title = "Bronze", amount_cents = 100 } },
            new { id = "tier-silver", type = "tier", attributes = new { title = "Silver", amount_cents = 500 } },
            new { id = "tier-gold",   type = "tier", attributes = new { title = "Gold",   amount_cents = 1000 } }
        });

        var member = _client.ParseMemberData(DeserializeMemberData(memberJson), DeserializeIncluded(includedJson));

        Assert.IsNotNull(member);
        Assert.AreEqual(3, member.EntitledTiers.Count);

        var bronze = member.EntitledTiers.Single(t => t.TierId == "tier-bronze");
        var silver = member.EntitledTiers.Single(t => t.TierId == "tier-silver");
        var gold = member.EntitledTiers.Single(t => t.TierId == "tier-gold");

        Assert.AreEqual(100, bronze.AmountCents);
        Assert.AreEqual("Bronze", bronze.Title);
        Assert.AreEqual(500, silver.AmountCents);
        Assert.AreEqual("Silver", silver.Title);
        Assert.AreEqual(1000, gold.AmountCents);
        Assert.AreEqual("Gold", gold.Title);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 7: Empty currently_entitled_tiers.data → empty EntitledTiers list
    // ──────────────────────────────────────────────────────────────────────────
    [Test]
    public void ParseMemberData_EmptyEntitledTiers_ResultsInEmptyList()
    {
        var memberJson = JsonSerializer.Serialize(new
        {
            id = "member-empty",
            type = "member",
            attributes = new { patron_status = "former_patron", last_charge_status = "Declined" },
            relationships = new
            {
                user = new { data = new { id = "user-1", type = "user" } },
                currently_entitled_tiers = new { data = Array.Empty<object>() }
            }
        });

        var member = _client.ParseMemberData(DeserializeMemberData(memberJson), new List<PatreonApiData>());

        Assert.IsNotNull(member);
        Assert.IsNotNull(member.EntitledTiers);
        Assert.AreEqual(0, member.EntitledTiers.Count, "Empty currently_entitled_tiers.data → zero entitled tiers");
    }
}

/// <summary>
/// Cross-campaign defense tests for IsMemberForCampaign (made internal for testability).
/// These verify that identity-endpoint payloads with multiple campaign memberships are
/// filtered correctly so only the W3Champions campaign member is selected.
/// </summary>
[TestFixture]
public class CrossCampaignDefenseTests
{
    private const string OurCampaignId = "4973374";

    private HttpClient _httpClient;
    private PatreonApiClient _client;

    [SetUp]
    public void SetUp()
    {
        Environment.SetEnvironmentVariable("REWARDS_PATREON_CAMPAIGN_ID", OurCampaignId);
        Environment.SetEnvironmentVariable("REWARDS_PATREON_ACCESS_TOKEN", "test-token");
        _httpClient = new HttpClient(new Mock<HttpMessageHandler>().Object);
        _client = new PatreonApiClient(_httpClient);
    }

    [TearDown]
    public void TearDown()
    {
        Environment.SetEnvironmentVariable("REWARDS_PATREON_CAMPAIGN_ID", null);
        Environment.SetEnvironmentVariable("REWARDS_PATREON_ACCESS_TOKEN", null);
        _httpClient?.Dispose();
    }

    private static PatreonApiData MakeMemberResource(string memberId, string campaignId)
    {
        var json = JsonSerializer.Serialize(new
        {
            id = memberId,
            type = "member",
            attributes = new { patron_status = "active_patron", last_charge_status = "Paid" },
            relationships = new
            {
                campaign = new { data = new { id = campaignId, type = "campaign" } },
                user = new { data = new { id = "user-1", type = "user" } },
                currently_entitled_tiers = new { data = Array.Empty<object>() }
            }
        });
        return JsonSerializer.Deserialize<PatreonApiData>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }

    private static PatreonApiData MakeMemberResourceNoCampaign(string memberId)
    {
        var json = JsonSerializer.Serialize(new
        {
            id = memberId,
            type = "member",
            attributes = new { patron_status = "active_patron", last_charge_status = "Paid" },
            relationships = new
            {
                user = new { data = new { id = "user-1", type = "user" } },
                currently_entitled_tiers = new { data = Array.Empty<object>() }
                // no "campaign" key
            }
        });
        return JsonSerializer.Deserialize<PatreonApiData>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 8: identity endpoint with two member resources → pick our campaign's member
    // ──────────────────────────────────────────────────────────────────────────
    [Test]
    public void IsMemberForCampaign_MultipleMembers_ReturnsTrueOnlyForOurCampaign()
    {
        var ourMember = MakeMemberResource("member-ours", OurCampaignId);
        var foreignMember = MakeMemberResource("member-foreign", "99999999");

        Assert.IsTrue(_client.IsMemberForCampaign(ourMember),
            "Should match the member whose campaign.data.id == our campaign");
        Assert.IsFalse(_client.IsMemberForCampaign(foreignMember),
            "Should not match a member from a different campaign");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 9: identity endpoint where the only member is from a foreign campaign → no match
    // ──────────────────────────────────────────────────────────────────────────
    [Test]
    public void IsMemberForCampaign_ForeignCampaignMemberOnly_ReturnsFalse()
    {
        var foreignMember = MakeMemberResource("member-foreign", "99999999");

        Assert.IsFalse(_client.IsMemberForCampaign(foreignMember),
            "A member for a foreign campaign must not match our campaign filter");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 10: member resource has no relationships.campaign key → no match
    // ──────────────────────────────────────────────────────────────────────────
    [Test]
    public void IsMemberForCampaign_MissingCampaignRelationship_ReturnsFalse()
    {
        var memberWithNoCampaign = MakeMemberResourceNoCampaign("member-no-campaign");

        Assert.IsFalse(_client.IsMemberForCampaign(memberWithNoCampaign),
            "Member with no campaign relationship must not match our campaign filter");
    }
}
