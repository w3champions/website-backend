using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using W3C.Domain.Rewards.Abstractions;
using W3C.Domain.Rewards.Events;

namespace W3ChampionsStatisticService.Rewards.Providers.KoFi;

public class KoFiProvider(IConfiguration configuration, ILogger<KoFiProvider> logger) : IRewardProvider
{
    private readonly IConfiguration _configuration = configuration;
    private readonly ILogger<KoFiProvider> _logger = logger;
    private readonly string _verificationToken = configuration["Rewards:Providers:KoFi:VerificationToken"];

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

    public async Task<RewardEvent> ParseWebhookEvent(string payload, Dictionary<string, string> headers = null)
    {
        if (string.IsNullOrEmpty(payload))
            throw new ArgumentException("Payload cannot be null or empty", nameof(payload));

        try
        {
            var webhookData = JsonSerializer.Deserialize<KoFiWebhookData>(payload);
            
            if (webhookData == null)
                throw new InvalidOperationException("Webhook data is null - malformed webhook");
                
            if (string.IsNullOrEmpty(webhookData.Email))
                throw new InvalidOperationException("User email is missing from Ko-Fi webhook - cannot resolve user");
                
            if (string.IsNullOrEmpty(webhookData.KofiTransactionId))
                throw new InvalidOperationException("Ko-Fi transaction ID is missing - cannot track event");
                
            if (string.IsNullOrEmpty(webhookData.MessageId))
                throw new InvalidOperationException("Ko-Fi message ID is missing - cannot ensure idempotency");
                
            if (string.IsNullOrEmpty(webhookData.Type))
                throw new InvalidOperationException("Ko-Fi event type is missing");

            var eventType = MapKoFiEventType(webhookData.Type, webhookData.IsSubscription);
            var userId = await ResolveUserId(webhookData.Email);
            
            if (string.IsNullOrEmpty(userId))
                throw new InvalidOperationException($"Failed to resolve user ID for email: {webhookData.Email}");
            
            var tierId = webhookData.ShopItemId ?? webhookData.TierId ?? "donation";
            
            // Validate amount for public donations
            if (webhookData.IsPublic && !string.IsNullOrEmpty(webhookData.Amount))
            {
                if (!decimal.TryParse(webhookData.Amount, out var amount) || amount < 0)
                    throw new InvalidOperationException($"Invalid donation amount: {webhookData.Amount}");
            }
            
            // Use Ko-Fi's message_id for idempotency (stays same across retries)
            var rewardEvent = new RewardEvent
            {
                EventId = $"kofi_{webhookData.MessageId}",
                EventType = eventType,
                ProviderId = ProviderId,
                UserId = userId,
                ProviderReference = webhookData.KofiTransactionId,
                AnnouncementAmount = webhookData.IsPublic && !string.IsNullOrEmpty(webhookData.Amount) 
                    ? decimal.Parse(webhookData.Amount) 
                    : null,
                Currency = webhookData.Currency ?? "USD",
                Timestamp = DateTime.UtcNow,
                EntitledTierIds = new List<string> { tierId },
                Metadata = new Dictionary<string, object>
                {
                    ["message"] = webhookData.Message ?? "",
                    ["is_public"] = webhookData.IsPublic,
                    ["transaction_id"] = webhookData.KofiTransactionId
                }
            };
            
            // Validate the event before returning
            rewardEvent.Validate();
            
            return rewardEvent;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to deserialize Ko-Fi webhook payload");
            throw new InvalidOperationException("Invalid JSON in Ko-Fi webhook payload", ex);
        }
        catch (Exception ex) when (!(ex is InvalidOperationException || ex is ArgumentException))
        {
            _logger.LogError(ex, "Unexpected error parsing Ko-Fi webhook event");
            throw new InvalidOperationException("Failed to parse Ko-Fi webhook event", ex);
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