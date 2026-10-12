using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using Stripe;
using W3ChampionsStatisticService.ReplayStore;

namespace WC3ChampionsStatisticService.Tests.ReplayStore;

[TestFixture]
public class StripeReplayPaymentsTests
{
    private const string WebhookSecret = "whsec_unit_test_secret";

    private const string CompletedEvent = """
        {
          "id": "evt_1",
          "object": "event",
          "api_version": "2020-08-27",
          "created": 1791800000,
          "livemode": false,
          "type": "checkout.session.completed",
          "data": { "object": { "id": "cs_test_123", "object": "checkout.session", "payment_status": "paid" } }
        }
        """;

    [Test]
    public void WithoutKey_TheStoreIsNotConfigured()
    {
        Assert.IsFalse(Payments(secretKey: null).IsConfigured);
        Assert.IsFalse(Payments(secretKey: " ").IsConfigured);
        Assert.IsTrue(Payments().IsConfigured);
    }

    [Test]
    public void Webhook_WithAValidSignature_GivesTheTypeAndTheSession()
    {
        // An older API version than the library's: the store reads only the type and the session id.
        var header = EventUtility.GenerateSignatureHeader(CompletedEvent, WebhookSecret);

        var paymentEvent = Payments().ParseWebhook(CompletedEvent, header);

        Assert.AreEqual(EventTypes.CheckoutSessionCompleted, paymentEvent.Type);
        Assert.AreEqual("cs_test_123", paymentEvent.CheckoutSessionId);
    }

    [Test]
    public void Webhook_WithAnotherSecretOrNoSecret_IsRefused()
    {
        var header = EventUtility.GenerateSignatureHeader(CompletedEvent, "whsec_someone_else");

        Assert.IsNull(Payments().ParseWebhook(CompletedEvent, header));
        Assert.IsNull(Payments().ParseWebhook(CompletedEvent, null));
        Assert.IsNull(Payments(webhookSecret: null).ParseWebhook(CompletedEvent, EventUtility.GenerateSignatureHeader(CompletedEvent, WebhookSecret)));
    }

    [Test]
    public void Webhook_WithAChangedPayload_IsRefused()
    {
        var header = EventUtility.GenerateSignatureHeader(CompletedEvent, WebhookSecret);

        Assert.IsNull(Payments().ParseWebhook(CompletedEvent.Replace("cs_test_123", "cs_test_999"), header));
    }

    [Test]
    public void Webhook_ReadsOnlyTheTypeAndTheSession()
    {
        // A field that Stripe.net's model cannot read (another API version) must not refuse the event.
        const string payload = """
            { "id": "evt_2", "object": "event", "api_version": "2099-01-01.future", "type": "checkout.session.expired",
              "data": { "object": { "id": "cs_test_456", "object": "checkout.session", "amount_total": { "new": "shape" } } } }
            """;
        const string otherObject = """
            { "id": "evt_3", "object": "event", "type": "charge.succeeded", "data": { "object": { "id": "ch_1", "object": "charge" } } }
            """;

        var expired = Payments().ParseWebhook(payload, EventUtility.GenerateSignatureHeader(payload, WebhookSecret));
        var charge = Payments().ParseWebhook(otherObject, EventUtility.GenerateSignatureHeader(otherObject, WebhookSecret));

        Assert.AreEqual(new ReplayPaymentEvent("checkout.session.expired", "cs_test_456"), expired);
        Assert.AreEqual(new ReplayPaymentEvent("charge.succeeded", null), charge);
    }

    [Test]
    public void Webhook_SignedButNotAnEvent_IsRefused()
    {
        foreach (var payload in new[] { "not json", "[]", "{\"id\": " })
        {
            Assert.IsNull(Payments().ParseWebhook(payload, EventUtility.GenerateSignatureHeader(payload, WebhookSecret)), payload);
        }
    }

    [TestCase(5.00, 500)]
    [TestCase(24.68, 2468)]
    [TestCase(100.00, 10000)]
    public void MinorUnits_AreCents(decimal amount, long cents)
    {
        Assert.AreEqual(cents, StripeReplayPayments.ToMinorUnits(amount));
    }

    private static StripeReplayPayments Payments(string secretKey = "sk_test_unit", string webhookSecret = WebhookSecret) =>
        new(secretKey, webhookSecret, null, NullLogger<StripeReplayPayments>.Instance);
}
