using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using W3C.Domain.Repositories;
using W3C.Domain.Tracing;
using W3ChampionsStatisticService.Admin.ReplayExport;

namespace W3ChampionsStatisticService.ReplayStore;

public enum ReplayOrderStatus
{
    /// <summary>The buyer is on the payment page, or a delayed payment method has not paid yet.</summary>
    Pending = 0,

    /// <summary>Paid. The purchase is in ReplayExports, with the same id as the order.</summary>
    Paid = 1,

    /// <summary>The payment page closed without a payment.</summary>
    Expired = 2,

    /// <summary>A delayed payment method did not pay.</summary>
    Failed = 3,
}

/// <summary>
/// One purchase of replays, in the ReplayOrders collection. The order keeps the games and the price from
/// before the payment. When the payment arrives, the games go to ReplayExports as a record with the same id,
/// so the purchase shows in the buyer's history and later searches can leave its games out.
/// </summary>
public class ReplayOrder
{
    [BsonId]
    public ObjectId Id { get; set; }

    public string BattleTag { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<ObjectId> MatchIds { get; set; } = [];
    public ReplayExportFilter Filter { get; set; }
    public string Label { get; set; }
    public string Description { get; set; }

    [BsonRepresentation(BsonType.Decimal128)]
    public decimal Amount { get; set; }

    public string Currency { get; set; } = ReplayExportPricing.Currency;
    public ReplayOrderStatus Status { get; set; }
    public string CheckoutSessionId { get; set; }
    public string PaymentIntentId { get; set; }
    public DateTime? PaidAt { get; set; }
}

/// <summary>What the page gets about an order: no game ids.</summary>
public class ReplayOrderView
{
    public string Id { get; set; }
    public ReplayOrderStatus Status { get; set; }
    public int Count { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? PaidAt { get; set; }

    public static ReplayOrderView From(ReplayOrder order) => new()
    {
        Id = order.Id.ToString(),
        Status = order.Status,
        Count = order.MatchIds.Count,
        Amount = order.Amount,
        Currency = order.Currency,
        CreatedAt = order.CreatedAt,
        PaidAt = order.PaidAt,
    };
}

public class ReplayCheckoutResponse
{
    public string OrderId { get; set; }

    /// <summary>The Stripe payment page. The browser goes there.</summary>
    public string Url { get; set; }

    public int Count { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; }
}

[Trace]
public class ReplayOrderRepository(MongoClient mongoClient) : MongoDbRepositoryBase(mongoClient), IRequiresIndexes
{
    public string CollectionName => "ReplayOrders";

    private IMongoCollection<ReplayOrder> Orders => CreateCollection<ReplayOrder>(CollectionName);

    public Task Create(ReplayOrder order) => Orders.InsertOneAsync(order);

    public async Task<ReplayOrder> Load(ObjectId id) => await Orders.Find(o => o.Id == id).FirstOrDefaultAsync();

    /// <summary>The order, or null when it does not exist or belongs to another user.</summary>
    public async Task<ReplayOrder> LoadForUser(string id, string battleTag)
    {
        if (!ObjectId.TryParse(id, out var objectId)) return null;
        return await Orders.Find(o => o.Id == objectId && o.BattleTag == battleTag).FirstOrDefaultAsync();
    }

    /// <summary>The orders that the user started after <paramref name="since"/>.</summary>
    public Task<long> CountStartedSince(string battleTag, DateTime since) =>
        Orders.CountDocumentsAsync(o => o.BattleTag == battleTag && o.CreatedAt >= since);

    /// <summary>For an order over the hourly limit: it never had a payment page.</summary>
    public Task Delete(ObjectId id) => Orders.DeleteOneAsync(o => o.Id == id);

    public Task SetCheckoutSession(ObjectId id, string sessionId) =>
        Orders.UpdateOneAsync(o => o.Id == id, Builders<ReplayOrder>.Update.Set(o => o.CheckoutSessionId, sessionId));

    /// <summary>Pending to paid. A second call changes nothing.</summary>
    public Task MarkPaid(ObjectId id, string paymentIntentId, DateTime paidAt) =>
        Orders.UpdateOneAsync(
            o => o.Id == id && o.Status != ReplayOrderStatus.Paid,
            Builders<ReplayOrder>.Update
                .Set(o => o.Status, ReplayOrderStatus.Paid)
                .Set(o => o.PaymentIntentId, paymentIntentId)
                .Set(o => o.PaidAt, paidAt));

    /// <summary>Pending to <paramref name="status"/> (expired or failed). A paid order stays paid.</summary>
    public Task Close(ObjectId id, ReplayOrderStatus status) =>
        Orders.UpdateOneAsync(
            o => o.Id == id && o.Status == ReplayOrderStatus.Pending,
            Builders<ReplayOrder>.Update.Set(o => o.Status, status));

    /// <summary>The per-user count of new orders reads this index. The collection is new, so the build is quick.</summary>
    public Task EnsureIndexesAsync() =>
        Orders.Indexes.CreateOneAsync(new CreateIndexModel<ReplayOrder>(
            Builders<ReplayOrder>.IndexKeys.Ascending(o => o.BattleTag).Ascending(o => o.CreatedAt),
            new CreateIndexOptions { Name = "ReplayOrders_BattleTag_CreatedAt" }));
}
