using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Mongo2Go;
using MongoDB.Bson;
using MongoDB.Driver;
using NUnit.Framework;
using W3ChampionsStatisticService.Admin.ReplayExport;
using W3ChampionsStatisticService.ReplayStore;

namespace WC3ChampionsStatisticService.Tests.ReplayStore;

/// <summary>
/// From a Stripe session to a purchase, against an isolated mongod (Mongo2Go) and a fake Stripe.
/// </summary>
[TestFixture]
public class ReplayOrderFulfillmentTests
{
    private MongoDbRunner _runner;
    private MongoClient _client;
    private ReplayOrderRepository _orders;
    private ReplayExportRepository _exports;
    private FakePayments _payments;
    private ReplayOrderFulfillment _fulfillment;

    [SetUp]
    public void SetUp()
    {
        _runner = MongoDbRunner.Start();
        _client = new MongoClient(_runner.ConnectionString);
        _orders = new ReplayOrderRepository(_client);
        _exports = new ReplayExportRepository(_client, null, null, null);
        _payments = new FakePayments();
        _fulfillment = new ReplayOrderFulfillment(_orders, _exports, _payments, NullLogger<ReplayOrderFulfillment>.Instance);
    }

    [TearDown]
    public void TearDown()
    {
        _runner.Dispose();
    }

    [Test]
    public async Task PaidSession_SavesThePurchaseOnce()
    {
        var order = await CreateOrder(games: 300);
        var session = Session(order, paymentStatus: "paid");

        // The webhook and the return page at the same time, then the webhook again.
        await Task.WhenAll(_fulfillment.Apply(order, session), _fulfillment.Apply(order, session));
        var paid = await _fulfillment.Apply(order, session);

        Assert.AreEqual(ReplayOrderStatus.Paid, paid.Status);
        Assert.AreEqual("pi_1", paid.PaymentIntentId);
        Assert.IsNotNull(paid.PaidAt);

        var purchase = (await _exports.LoadPurchases("Buyer#1")).Single();
        Assert.AreEqual(order.Id.ToString(), purchase.Id, "The purchase has the id of the order");
        Assert.AreEqual(300, purchase.Count);
        Assert.AreEqual(6.00m, purchase.Price);
        Assert.IsTrue(purchase.Charged);
        Assert.AreEqual(0, purchase.DownloadCount, "Paid, not downloaded yet");

        var excluded = await _exports.LoadExcludedIds(new ReplayExportFilter { ExcludePastDownloads = true }, "Buyer#1");
        CollectionAssert.AreEquivalent(order.MatchIds, excluded, "Later searches can leave out the bought games");
    }

    [Test]
    public async Task OnlyPaidDelivers()
    {
        var order = await CreateOrder();

        var result = await _fulfillment.Apply(order, Session(order, paymentStatus: "no_payment_required", status: "complete"));

        Assert.AreEqual(ReplayOrderStatus.Pending, result.Status);
        CollectionAssert.IsEmpty(await _exports.LoadPurchases("Buyer#1"));
    }

    [Test]
    public async Task UnpaidOpenSession_KeepsTheOrderPending()
    {
        var order = await CreateOrder();

        var result = await _fulfillment.Apply(order, Session(order, paymentStatus: "unpaid", status: "open"));

        Assert.AreEqual(ReplayOrderStatus.Pending, result.Status);
        CollectionAssert.IsEmpty(await _exports.LoadPurchases("Buyer#1"));
    }

    [Test]
    public async Task ExpiredSession_ClosesTheOrder()
    {
        var order = await CreateOrder();

        var result = await _fulfillment.Apply(order, Session(order, paymentStatus: "unpaid", status: "expired"));

        Assert.AreEqual(ReplayOrderStatus.Expired, result.Status);
        CollectionAssert.IsEmpty(await _exports.LoadPurchases("Buyer#1"));
    }

    [Test]
    public async Task OtherAmountOrSession_IsNotDelivered()
    {
        var order = await CreateOrder();

        var otherAmount = await _fulfillment.Apply(order, Session(order, paymentStatus: "paid", amount: 1));
        var otherCurrency = await _fulfillment.Apply(order, Session(order, paymentStatus: "paid", currency: "eur"));
        var otherSession = await _fulfillment.Apply(order, Session(order, paymentStatus: "paid", sessionId: "cs_test_other"));

        Assert.AreEqual(ReplayOrderStatus.Pending, otherAmount.Status);
        Assert.AreEqual(ReplayOrderStatus.Pending, otherCurrency.Status);
        Assert.AreEqual(ReplayOrderStatus.Pending, otherSession.Status);
        CollectionAssert.IsEmpty(await _exports.LoadPurchases("Buyer#1"));
    }

    [Test]
    public async Task Webhook_FindsTheOrderOfTheSession()
    {
        var paidOrder = await CreateOrder(sessionId: "cs_test_paid");
        var failedOrder = await CreateOrder(sessionId: "cs_test_failed");
        _payments.Sessions["cs_test_paid"] = Session(paidOrder, paymentStatus: "paid");
        _payments.Sessions["cs_test_failed"] = Session(failedOrder, paymentStatus: "unpaid", status: "complete");

        var paid = await _fulfillment.Sync("cs_test_paid");
        var failed = await _fulfillment.Sync("cs_test_failed", paymentFailed: true);
        var unknown = await _fulfillment.Sync("cs_test_unknown");

        Assert.AreEqual(ReplayOrderStatus.Paid, paid.Status);
        Assert.AreEqual(ReplayOrderStatus.Failed, failed.Status);
        Assert.IsNull(unknown);
        Assert.AreEqual(1, (await _exports.LoadPurchases("Buyer#1")).Count);
    }

    [Test]
    public async Task Purchases_AreOnlyThePaidOnesOfTheUser()
    {
        var order = await CreateOrder();
        await _fulfillment.Apply(order, Session(order, paymentStatus: "paid"));
        await _exports.SaveExport(new ReplayExportRecord { Id = ObjectId.GenerateNewId(), RequestedBy = "Buyer#1", Charged = false });
        await _exports.SaveExport(new ReplayExportRecord { Id = ObjectId.GenerateNewId(), RequestedBy = "Other#2", Charged = true });

        var purchases = await _exports.LoadPurchases("Buyer#1");

        Assert.AreEqual(order.Id.ToString(), purchases.Single().Id);
        Assert.AreEqual(1, await _exports.CountPurchases("Buyer#1"));
        Assert.AreEqual(0, await _exports.CountPurchases("Nobody#3"));
    }

    [Test]
    public async Task Purchases_ShowOnlyTheDownloadsOfTheBuyer()
    {
        var order = await CreateOrder();
        await _fulfillment.Apply(order, Session(order, paymentStatus: "paid"));
        await _exports.MarkDownloadedAgain(order.Id, "Buyer#1");
        // An admin downloads the purchase from the downloads of all users.
        await _exports.MarkDownloadedAgain(order.Id, "Admin#9");

        var purchase = (await _exports.LoadPurchases("Buyer#1")).Single();
        var asAdmin = (await _exports.LoadAllExports("Buyer#1")).Single();

        Assert.AreEqual(1, purchase.DownloadCount);
        CollectionAssert.AreEqual(new[] { "Buyer#1" }, purchase.Downloads.Select(d => d.By));
        Assert.AreEqual(purchase.Downloads[0].At, purchase.LastDownloadedAt);
        Assert.AreEqual(2, asAdmin.DownloadCount, "Admins see all downloads");
    }

    [Test]
    public async Task StoreExclude_LeavesOutOnlyThePurchases()
    {
        var order = await CreateOrder(games: 3);
        await _fulfillment.Apply(order, Session(order, paymentStatus: "paid"));
        var freeIds = new List<ObjectId> { ObjectId.GenerateNewId() };
        await _exports.SaveExport(new ReplayExportRecord { Id = ObjectId.GenerateNewId(), RequestedBy = "Buyer#1", MatchIds = freeIds, Charged = false });
        var filter = new ReplayExportFilter { ExcludePastDownloads = true };

        var store = await _exports.LoadExcludedIds(filter, "Buyer#1", onlyPurchases: true);
        var admin = await _exports.LoadExcludedIds(filter, "Buyer#1");

        CollectionAssert.AreEquivalent(order.MatchIds, store);
        CollectionAssert.AreEquivalent(order.MatchIds.Concat(freeIds), admin);
    }

    [Test]
    public async Task Orders_CountPerUserAndBelongToTheirUser()
    {
        var order = await CreateOrder();
        await CreateOrder(battleTag: "Other#2");

        Assert.AreEqual(1, await _orders.CountStartedSince("Buyer#1", DateTime.UtcNow.AddHours(-1)));
        Assert.AreEqual(0, await _orders.CountStartedSince("Buyer#1", DateTime.UtcNow.AddMinutes(1)));
        Assert.IsNotNull(await _orders.LoadForUser(order.Id.ToString(), "Buyer#1"));
        Assert.IsNull(await _orders.LoadForUser(order.Id.ToString(), "Other#2"));
        Assert.IsNull(await _orders.LoadForUser("not-an-id", "Buyer#1"));
    }

    [Test]
    public async Task ReplayableIds_AreTheGamesWithAFloReplayInTheRequestOrder()
    {
        var withReplay1 = ObjectId.GenerateNewId();
        var withoutReplay = ObjectId.GenerateNewId();
        var withReplay2 = ObjectId.GenerateNewId();
        await _client.GetDatabase("W3Champions-Statistic-Service").GetCollection<BsonDocument>("Matchup").InsertManyAsync(
        [
            new BsonDocument { { "_id", withReplay1 }, { "FloMatchId", 11 } },
            new BsonDocument { { "_id", withoutReplay }, { "FloMatchId", 0 } },
            new BsonDocument { { "_id", withReplay2 }, { "FloMatchId", 12 } },
        ]);

        var ids = await _exports.ReplayableIds([withReplay2, ObjectId.GenerateNewId(), withoutReplay, withReplay1]);

        CollectionAssert.AreEqual(new[] { withReplay2, withReplay1 }, ids);
    }

    private async Task<ReplayOrder> CreateOrder(int games = 3, string sessionId = "cs_test_1", string battleTag = "Buyer#1")
    {
        var order = new ReplayOrder
        {
            Id = ObjectId.GenerateNewId(),
            BattleTag = battleTag,
            CreatedAt = DateTime.UtcNow,
            MatchIds = Enumerable.Range(0, games).Select(_ => ObjectId.GenerateNewId()).ToList(),
            Amount = ReplayExportPricing.Price(games),
            Currency = "USD",
            Status = ReplayOrderStatus.Pending,
            CheckoutSessionId = sessionId,
        };
        await _orders.Create(order);
        return order;
    }

    private static ReplayCheckoutSession Session(
        ReplayOrder order,
        string paymentStatus,
        string status = "complete",
        long? amount = null,
        string currency = "usd",
        string sessionId = null) => new()
        {
            Id = sessionId ?? order.CheckoutSessionId,
            OrderId = order.Id.ToString(),
            Status = status,
            PaymentStatus = paymentStatus,
            AmountTotal = amount ?? StripeReplayPayments.ToMinorUnits(order.Amount),
            Currency = currency,
            PaymentIntentId = "pi_1",
        };

    private class FakePayments : IReplayPayments
    {
        public Dictionary<string, ReplayCheckoutSession> Sessions { get; } = [];
        public bool IsConfigured => true;
        public Task<ReplayCheckoutSession> CreateCheckout(ReplayOrder order, string successUrl, string cancelUrl) => throw new NotSupportedException();
        public Task<ReplayCheckoutSession> GetCheckout(string sessionId) => Task.FromResult(Sessions.GetValueOrDefault(sessionId));
        public ReplayPaymentEvent ParseWebhook(string payload, string signatureHeader) => throw new NotSupportedException();
    }
}
