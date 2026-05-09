using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using W3C.Domain.Rewards.Events;
using W3C.Domain.Rewards.Repositories;
using W3ChampionsStatisticService.Rewards.Providers.Patreon;
using WC3ChampionsStatisticService.UnitTests.Rewards.Fixtures;

namespace WC3ChampionsStatisticService.UnitTests.Rewards;

/// <summary>
/// Tests for PatreonProvider webhook parsing and HMAC signature validation.
/// Uses real webhook payloads loaded from PatreonPayloadLoader.
///
/// Note on ResolveUserId:
/// ParseWebhookEvent calls ResolveUserId (via IPatreonAccountLinkRepository) to map
/// the Patreon user ID to a BattleTag.  When the repository returns null (no linked
/// account) ParseWebhookEvent returns null as well — this is intentional production
/// behaviour.  Tests that need a non-null RewardEvent stub the repository to return
/// a known BattleTag.
/// </summary>
[TestFixture]
public class PatreonProviderTests
{
    private const string TestWebhookSecret = "super-secret-hmac-key";
    // In real webhook payloads data.id is the MEMBER resource ID, not the Patreon user ID.
    // PatreonProvider.ParseWebhookEvent passes data.id directly to ResolveUserId.
    private const string MemberResourceId = "d29e61b6-f73b-4662-955e-dade715cef83";
    private const string ExpectedTierId = "196241737"; // Elite Tier, 1337 cents

    private Mock<ILogger<PatreonProvider>> _loggerMock;
    private Mock<IPatreonAccountLinkRepository> _repoMock;
    private PatreonProvider _provider;

    [SetUp]
    public void SetUp()
    {
        Environment.SetEnvironmentVariable("PATREON_WEBHOOK_SECRET", TestWebhookSecret);
        _loggerMock = new Mock<ILogger<PatreonProvider>>();
        _repoMock = new Mock<IPatreonAccountLinkRepository>();
        _provider = new PatreonProvider(_loggerMock.Object, _repoMock.Object);
    }

    [TearDown]
    public void TearDown()
    {
        Environment.SetEnvironmentVariable("PATREON_WEBHOOK_SECRET", null);
    }

    // Helper: compute the expected HMAC-SHA256 hex signature for a payload.
    private static string ComputeHmac(string payload, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash).ToLower();
    }

    /// <summary>
    /// Stub the repository so the member-resource ID from the real payloads resolves to a BattleTag.
    /// PatreonProvider calls ResolveUserId(webhookData.Data.Id) where Data.Id is the member-resource ID
    /// (a UUID like d29e61b6-...) which is then passed directly to GetByPatreonUserId.
    /// </summary>
    private void StubBattleTagResolution(string battleTag = "TestUser#1234")
    {
        var link = new W3C.Domain.Rewards.Entities.PatreonAccountLink
        {
            PatreonUserId = MemberResourceId,
            BattleTag = battleTag
        };
        _repoMock
            .Setup(r => r.GetByPatreonUserId(MemberResourceId))
            .ReturnsAsync(link);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 1: members:create real payload → extracts tier ID and Patreon user ID
    // ──────────────────────────────────────────────────────────────────────────
    [Test]
    public async Task ParseWebhookEvent_MembersCreateRealPayload_ExtractsTierIdAndUserId()
    {
        StubBattleTagResolution();
        var payload = PatreonPayloadLoader.MembersCreateJson();
        var headers = new Dictionary<string, string> { ["X-Patreon-Event"] = "members:create" };

        var evt = await _provider.ParseWebhookEvent(payload, headers);

        Assert.IsNotNull(evt);
        Assert.AreEqual(RewardEventType.SubscriptionCreated, evt.EventType);
        Assert.AreEqual("TestUser#1234", evt.UserId);
        Assert.IsNotNull(evt.Metadata);
        // patreon_user_id in metadata holds webhookData.Data.Id (the member resource ID),
        // since PatreonProvider stores that value directly before identity resolution.
        Assert.AreEqual(MemberResourceId, evt.Metadata["patreon_user_id"]?.ToString());
        Assert.AreEqual(1, evt.EntitledTiers.Count);
        Assert.AreEqual(ExpectedTierId, evt.EntitledTiers[0].TierId);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 2: members:update real payload → event type is SubscriptionRenewed
    // ──────────────────────────────────────────────────────────────────────────
    [Test]
    public async Task ParseWebhookEvent_MembersUpdateRealPayload_DetectsTierChange()
    {
        StubBattleTagResolution();
        var payload = PatreonPayloadLoader.MembersUpdateJson();
        var headers = new Dictionary<string, string> { ["X-Patreon-Event"] = "members:update" };

        var evt = await _provider.ParseWebhookEvent(payload, headers);

        Assert.IsNotNull(evt);
        Assert.AreEqual(RewardEventType.SubscriptionRenewed, evt.EventType);
        Assert.AreEqual("TestUser#1234", evt.UserId);
        Assert.AreEqual(1, evt.EntitledTiers.Count);
        Assert.AreEqual(ExpectedTierId, evt.EntitledTiers[0].TierId);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 3: members:delete real payload → event type is SubscriptionCancelled
    // ──────────────────────────────────────────────────────────────────────────
    [Test]
    public async Task ParseWebhookEvent_MembersDeleteRealPayload_MapsToRevoke()
    {
        StubBattleTagResolution();
        var payload = PatreonPayloadLoader.MembersDeleteJson();
        var headers = new Dictionary<string, string> { ["X-Patreon-Event"] = "members:delete" };

        var evt = await _provider.ParseWebhookEvent(payload, headers);

        Assert.IsNotNull(evt);
        Assert.AreEqual(RewardEventType.SubscriptionCancelled, evt.EventType);
        Assert.AreEqual("TestUser#1234", evt.UserId);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 4: correct HMAC signature on a real payload → validation passes
    // ──────────────────────────────────────────────────────────────────────────
    [Test]
    public async Task ValidateWebhookSignature_RealPayloadAndSecret_PassesHmac()
    {
        var payload = PatreonPayloadLoader.MembersCreateJson();
        var signature = ComputeHmac(payload, TestWebhookSecret);
        var headers = new Dictionary<string, string>();

        var isValid = await _provider.ValidateWebhookSignature(payload, signature, headers);

        Assert.IsTrue(isValid, "A correctly-signed payload must pass HMAC validation");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 5: same signature, tampered body → validation fails
    // ──────────────────────────────────────────────────────────────────────────
    [Test]
    public async Task ValidateWebhookSignature_TamperedPayload_FailsValidation()
    {
        var originalPayload = PatreonPayloadLoader.MembersCreateJson();
        var signature = ComputeHmac(originalPayload, TestWebhookSecret);

        // Tamper by appending a single character — signature must not match
        var tamperedPayload = originalPayload + " ";
        var headers = new Dictionary<string, string>();

        var isValid = await _provider.ValidateWebhookSignature(tamperedPayload, signature, headers);

        Assert.IsFalse(isValid, "A tampered payload must fail HMAC validation");
    }
}
