using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using W3C.Domain.Rewards.Abstractions;
using W3C.Domain.Rewards.Events;

namespace W3ChampionsStatisticService.Rewards.Providers.KoFi;

public class KoFiProvider : IRewardProvider
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<KoFiProvider> _logger;
    private readonly string _verificationToken;

    public KoFiProvider(IConfiguration configuration, ILogger<KoFiProvider> logger)
    {
        _configuration = configuration;
        _logger = logger;
        _verificationToken = configuration["Rewards:Providers:KoFi:VerificationToken"];
    }

    public string ProviderId => "kofi";
    public string ProviderName => "Ko-Fi";

    public Task<bool> ValidateWebhookSignature(string payload, string signature, Dictionary<string, string> headers)
    {
        try
        {
            var data = JsonSerializer.Deserialize<KoFiWebhookData>(payload);
            var isValid = data?.VerificationToken == _verificationToken;
            
            if (!isValid)
            {
                _logger.LogWarning("Invalid Ko-Fi verification token");
            }
            
            return Task.FromResult(isValid);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating Ko-Fi webhook");
            return Task.FromResult(false);
        }
    }

    public async Task<RewardEvent> ParseWebhookEvent(string payload)
    {
        try
        {
            var webhookData = JsonSerializer.Deserialize<KoFiWebhookData>(payload);
            var eventType = MapKoFiEventType(webhookData.Type, webhookData.IsSubscription);
            var userId = await ResolveUserId(webhookData.Email);
            
            return new RewardEvent
            {
                EventId = Guid.NewGuid().ToString(),
                EventType = eventType,
                ProviderId = ProviderId,
                UserId = userId,
                ProductId = webhookData.ShopItemId ?? webhookData.TierId ?? "donation",
                ProviderReference = webhookData.KofiTransactionId,
                AnnouncementAmount = webhookData.IsPublic ? decimal.Parse(webhookData.Amount) : null,
                Currency = webhookData.Currency,
                Timestamp = DateTime.UtcNow,
                Metadata = new Dictionary<string, object>
                {
                    ["message"] = webhookData.Message,
                    ["is_public"] = webhookData.IsPublic
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error parsing Ko-Fi webhook event");
            throw;
        }
    }

    public Task<ProviderProduct> GetProduct(string productId)
    {
        return Task.FromResult(new ProviderProduct
        {
            ProductId = productId,
            Name = $"Ko-Fi Product {productId}",
            Type = ProductType.OneTimePurchase,
            Metadata = new Dictionary<string, object>()
        });
    }

    public Task<string> ResolveUserId(string providerUserEmail)
    {
        // This would typically look up the user mapping in a database
        // For now, return the email as user identifier
        return Task.FromResult(providerUserEmail);
    }

    private RewardEventType MapKoFiEventType(string type, bool isSubscription)
    {
        return (type, isSubscription) switch
        {
            ("Donation", false) => RewardEventType.Purchase,
            ("Donation", true) => RewardEventType.SubscriptionCreated,
            ("Shop Order", _) => RewardEventType.Purchase,
            ("Subscription", true) => RewardEventType.SubscriptionRenewed,
            _ => RewardEventType.Purchase
        };
    }
}