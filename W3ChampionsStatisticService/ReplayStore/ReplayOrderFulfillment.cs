using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using W3C.Domain.Tracing;
using W3ChampionsStatisticService.Admin.ReplayExport;

namespace W3ChampionsStatisticService.ReplayStore;

/// <summary>
/// Turns a paid order into a purchase. The Stripe webhook and the return page both call it, maybe at the same
/// time and more than once for the same payment: every step can run again without a second purchase.
/// </summary>
[Trace]
public class ReplayOrderFulfillment(
    ReplayOrderRepository orders,
    ReplayExportRepository exports,
    IReplayPayments payments,
    ILogger<ReplayOrderFulfillment> logger)
{
    private readonly ReplayOrderRepository _orders = orders;
    private readonly ReplayExportRepository _exports = exports;
    private readonly IReplayPayments _payments = payments;
    private readonly ILogger<ReplayOrderFulfillment> _logger = logger;

    /// <summary>
    /// For a webhook: gets the session from Stripe (the event itself is not trusted for the state) and updates
    /// its order. <paramref name="paymentFailed"/>: a delayed payment method did not pay.
    /// </summary>
    public async Task<ReplayOrder> Sync(string sessionId, bool paymentFailed = false)
    {
        var session = await _payments.GetCheckout(sessionId);
        if (session == null || !ObjectId.TryParse(session.OrderId, out var orderId))
        {
            _logger.LogWarning("Stripe session {SessionId} is not a replay order", sessionId);
            return null;
        }

        var order = await _orders.Load(orderId);
        if (order == null)
        {
            _logger.LogWarning("Stripe session {SessionId} names the unknown replay order {OrderId}", sessionId, orderId);
            return null;
        }

        if (paymentFailed && order.Status == ReplayOrderStatus.Pending && session.PaymentStatus == "unpaid")
        {
            _logger.LogInformation("Replay order {OrderId}: the delayed payment failed", order.Id);
            await _orders.Close(order.Id, ReplayOrderStatus.Failed);
            return await _orders.Load(order.Id);
        }
        return await Apply(order, session);
    }

    /// <summary>Updates the order to the state of its session. Saves the purchase when the session is paid.</summary>
    public async Task<ReplayOrder> Apply(ReplayOrder order, ReplayCheckoutSession session)
    {
        if (order.Status == ReplayOrderStatus.Paid) return order;
        if (session.Id != order.CheckoutSessionId)
        {
            _logger.LogWarning("Stripe session {SessionId} is not the session of replay order {OrderId}", session.Id, order.Id);
            return order;
        }

        // Only "paid" delivers. "unpaid": open, expired, or a delayed payment method that has not paid yet.
        if (session.PaymentStatus != "paid")
        {
            if (session.Status == "expired" && order.Status == ReplayOrderStatus.Pending)
            {
                await _orders.Close(order.Id, ReplayOrderStatus.Expired);
                return await _orders.Load(order.Id);
            }
            return order;
        }

        // The amount was set on the server, but the buyer must not get the replays for another amount.
        var expected = StripeReplayPayments.ToMinorUnits(order.Amount);
        if (session.AmountTotal != expected || !string.Equals(session.Currency, order.Currency, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogError(
                "Replay order {OrderId}: Stripe has {Amount} {Currency}, the order {Expected} {OrderCurrency}. Not delivered.",
                order.Id, session.AmountTotal, session.Currency, expected, order.Currency);
            return order;
        }

        var paidAt = DateTime.UtcNow;
        // The record first: when the second step fails, the next call does it again.
        var saved = await _exports.SavePurchase(new ReplayExportRecord
        {
            Id = order.Id,
            CreatedAt = paidAt,
            RequestedBy = order.BattleTag,
            Label = order.Label,
            Description = order.Description,
            DownloadCount = 0,
            Requested = order.MatchIds.Count,
            MatchIds = order.MatchIds,
            Filter = order.Filter,
            Price = order.Amount,
            Charged = true,
        });
        await _orders.MarkPaid(order.Id, session.PaymentIntentId, paidAt);
        if (saved)
        {
            _logger.LogInformation(
                "Replay order {OrderId} paid by {BattleTag}: {Count} replays, {Amount} {Currency}",
                order.Id, order.BattleTag, order.MatchIds.Count, order.Amount, order.Currency);
        }
        return await _orders.Load(order.Id);
    }
}
