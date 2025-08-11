using System.Collections.Generic;
using System.Threading.Tasks;
using W3C.Domain.Rewards.Events;

namespace W3C.Domain.Rewards.Abstractions;

public interface IRewardProvider
{
    string ProviderId { get; }
    string ProviderName { get; }
    
    Task<bool> ValidateWebhookSignature(string payload, string signature, Dictionary<string, string> headers);
    Task<RewardEvent> ParseWebhookEvent(string payload, Dictionary<string, string> headers = null);
    Task<ProviderProduct> GetProduct(string productId);
    Task<string> ResolveUserId(string providerUserId);
}

public class ProviderProduct
{
    public string ProductId { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; }
    public ProductType Type { get; set; }
    public Dictionary<string, object> Metadata { get; set; }
}

public enum ProductType
{
    OneTimePurchase,
    RecurringSubscription,
    Donation
}