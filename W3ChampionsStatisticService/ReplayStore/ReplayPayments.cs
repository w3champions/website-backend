using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Stripe;
using Stripe.Checkout;
using W3C.Domain;

namespace W3ChampionsStatisticService.ReplayStore;

/// <summary>The parts of a Stripe Checkout Session that the store uses.</summary>
public class ReplayCheckoutSession
{
    public string Id { get; init; }
    public string Url { get; init; }

    /// <summary>The order id, from client_reference_id.</summary>
    public string OrderId { get; init; }

    /// <summary>open, complete or expired.</summary>
    public string Status { get; init; }

    /// <summary>paid, unpaid or no_payment_required.</summary>
    public string PaymentStatus { get; init; }

    /// <summary>In the smallest unit of the currency (cents).</summary>
    public long? AmountTotal { get; init; }

    public string Currency { get; init; }
    public string PaymentIntentId { get; init; }
}

/// <summary>A webhook event with a valid signature: its type and the id of its Checkout Session.</summary>
public record ReplayPaymentEvent(string Type, string CheckoutSessionId);

/// <summary>The payment provider of the replay store. Stripe in production; tests use a fake.</summary>
public interface IReplayPayments
{
    /// <summary>False when the secret key is not set: then the store sells nothing.</summary>
    bool IsConfigured { get; }

    Task<ReplayCheckoutSession> CreateCheckout(ReplayOrder order, string successUrl, string cancelUrl);

    /// <summary>The session as Stripe has it now, or null when Stripe does not know it.</summary>
    Task<ReplayCheckoutSession> GetCheckout(string sessionId);

    /// <summary>Checks the signature of a webhook. Returns null when the signature or the payload is not valid.</summary>
    ReplayPaymentEvent ParseWebhook(string payload, string signatureHeader);
}

/// <summary>
/// Stripe Checkout (the page hosted by Stripe). The payment methods come from the Stripe Dashboard
/// (Settings > Payment methods): cards, wallets and, for accounts in Europe, PayPal. The code sets none.
///
/// <c>STRIPE_SECRET_KEY</c>: sk_test_… for test mode, sk_live_… in production.
/// <c>STRIPE_WEBHOOK_SECRET</c>: whsec_… of the webhook endpoint (api/replay-store/stripe/webhook).
/// <c>STRIPE_API_BASE</c>: only for local tests against a stand-in of the Stripe API.
/// </summary>
public class StripeReplayPayments : IReplayPayments
{
    /// <summary>
    /// The time to pay. Stripe's shortest time is 30 minutes after it creates the session; the 2 more minutes
    /// cover the network, clock differences and the retries of Stripe.net (they send the same time).
    /// </summary>
    public static readonly TimeSpan CheckoutLifetime = TimeSpan.FromMinutes(32);

    private readonly StripeClient _client;
    private readonly string _webhookSecret;
    private readonly ILogger<StripeReplayPayments> _logger;

    public StripeReplayPayments(IHttpClientFactory httpClientFactory, ILogger<StripeReplayPayments> logger)
        : this(
            Environment.GetEnvironmentVariable("STRIPE_SECRET_KEY"),
            Environment.GetEnvironmentVariable("STRIPE_WEBHOOK_SECRET"),
            Environment.GetEnvironmentVariable("STRIPE_API_BASE"),
            logger,
            httpClientFactory.CreateClient(HttpClientNames.Stripe))
    {
    }

    /// <param name="httpClient">The named client with its timeout. Null: the own client of Stripe.net (tests).</param>
    public StripeReplayPayments(string secretKey, string webhookSecret, string apiBase, ILogger<StripeReplayPayments> logger, HttpClient httpClient = null)
    {
        _logger = logger;
        _webhookSecret = string.IsNullOrWhiteSpace(webhookSecret) ? null : webhookSecret.Trim();
        if (string.IsNullOrWhiteSpace(secretKey)) return;

        _client = new StripeClient(
            secretKey.Trim(),
            httpClient: httpClient == null ? null : new SystemNetHttpClient(httpClient, maxNetworkRetries: 2),
            apiBase: string.IsNullOrWhiteSpace(apiBase) ? null : apiBase.Trim());
    }

    public bool IsConfigured => _client != null;

    public async Task<ReplayCheckoutSession> CreateCheckout(ReplayOrder order, string successUrl, string cancelUrl)
    {
        var metadata = new Dictionary<string, string>
        {
            ["orderId"] = order.Id.ToString(),
            ["battleTag"] = order.BattleTag,
            ["replays"] = order.MatchIds.Count.ToString(),
        };
        var options = new SessionCreateOptions
        {
            Mode = "payment",
            ClientReferenceId = order.Id.ToString(),
            SuccessUrl = successUrl,
            CancelUrl = cancelUrl,
            ExpiresAt = DateTime.UtcNow.Add(CheckoutLifetime),
            SubmitType = "pay",
            Metadata = metadata,
            PaymentIntentData = new SessionPaymentIntentDataOptions
            {
                Description = $"W3Champions replays: {order.MatchIds.Count:N0}",
                Metadata = metadata,
            },
            LineItems =
            [
                new SessionLineItemOptions
                {
                    Quantity = 1,
                    PriceData = new SessionLineItemPriceDataOptions
                    {
                        Currency = order.Currency.ToLowerInvariant(),
                        UnitAmount = ToMinorUnits(order.Amount),
                        ProductData = new SessionLineItemPriceDataProductDataOptions
                        {
                            Name = order.MatchIds.Count == 1 ? "1 Warcraft III replay" : $"{order.MatchIds.Count:N0} Warcraft III replays",
                            Description = Shorten(order.Description ?? "Replays of W3Champions ladder games, as one ZIP download.", 300),
                        },
                    },
                },
            ],
        };

        // The same order never makes a second session, also when the request is sent again.
        var session = await _client.V1.Checkout.Sessions.CreateAsync(options, new RequestOptions { IdempotencyKey = $"replay-order-{order.Id}" });
        return ToCheckout(session);
    }

    public async Task<ReplayCheckoutSession> GetCheckout(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId)) return null;
        try
        {
            return ToCheckout(await _client.V1.Checkout.Sessions.GetAsync(sessionId));
        }
        catch (StripeException ex) when (ex.StripeError?.Code == "resource_missing")
        {
            return null;
        }
    }

    public ReplayPaymentEvent ParseWebhook(string payload, string signatureHeader)
    {
        if (_webhookSecret == null)
        {
            _logger.LogWarning("A Stripe webhook arrived, but STRIPE_WEBHOOK_SECRET is not set");
            return null;
        }
        // Stripe.net fails with a NullReferenceException on a missing header.
        if (string.IsNullOrWhiteSpace(signatureHeader) || string.IsNullOrEmpty(payload)) return null;

        try
        {
            // Only the signature, not Stripe.net's model of the event: the store reads the type and the session
            // id, and then gets the session from the API. So the API version of the endpoint does not matter.
            EventUtility.ValidateSignature(payload, signatureHeader, _webhookSecret);
            using var json = JsonDocument.Parse(payload);
            var type = json.RootElement.GetProperty("type").GetString();
            var data = json.RootElement.GetProperty("data").GetProperty("object");
            var isSession = data.TryGetProperty("object", out var kind) && kind.ValueKind == JsonValueKind.String && kind.GetString() == "checkout.session";
            return new ReplayPaymentEvent(type, isSession ? data.GetProperty("id").GetString() : null);
        }
        catch (Exception ex)
        {
            // StripeException for the signature; a JSON exception for a signed body that is not an event.
            _logger.LogWarning("Stripe webhook with an invalid signature or payload: {Message}", ex.Message);
            return null;
        }
    }

    /// <summary>Cents for USD and EUR. The store sells only in currencies with two decimals.</summary>
    public static long ToMinorUnits(decimal amount) => (long)Math.Round(amount * 100, MidpointRounding.AwayFromZero);

    private static string Shorten(string text, int maxLength) => text.Length <= maxLength ? text : text[..(maxLength - 1)] + "…";

    private static ReplayCheckoutSession ToCheckout(Session session) => new()
    {
        Id = session.Id,
        Url = session.Url,
        OrderId = session.ClientReferenceId,
        Status = session.Status,
        PaymentStatus = session.PaymentStatus,
        AmountTotal = session.AmountTotal,
        Currency = session.Currency,
        PaymentIntentId = session.PaymentIntentId,
    };
}
