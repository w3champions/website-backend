using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using W3C.Domain.Rewards.Abstractions;
using W3C.Domain.Rewards.Events;

namespace W3ChampionsStatisticService.Rewards.Providers.Patreon;

public class PatreonProvider(IConfiguration configuration, ILogger<PatreonProvider> logger) : IRewardProvider
{
    private readonly IConfiguration _configuration = configuration;
    private readonly ILogger<PatreonProvider> _logger = logger;
    private readonly string _webhookSecret = configuration["Rewards:Providers:Patreon:WebhookSecret"];

    public string ProviderId => "patreon";
    public string ProviderName => "Patreon";

    public Task<bool> ValidateWebhookSignature(string payload, string signature, Dictionary<string, string> headers)
    {
        if (string.IsNullOrEmpty(_webhookSecret))
        {
            _logger.LogWarning("Patreon webhook secret not configured");
            return Task.FromResult(false);
        }

        try
        {
            var computedSignature = ComputeHmacSignature(payload);
            var isValid = signature == computedSignature;
            
            if (!isValid)
            {
                _logger.LogWarning("Invalid Patreon webhook signature");
            }
            
            return Task.FromResult(isValid);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating Patreon webhook signature");
            return Task.FromResult(false);
        }
    }

    public async Task<RewardEvent> ParseWebhookEvent(string payload, Dictionary<string, string> headers = null)
    {
        if (string.IsNullOrEmpty(payload))
            throw new ArgumentException("Payload cannot be null or empty", nameof(payload));

        try
        {
            var webhookData = JsonSerializer.Deserialize<PatreonWebhookData>(payload);
            
            if (webhookData?.Data == null)
                throw new InvalidOperationException("Webhook data or data.data is null - malformed webhook");
                
            if (webhookData.Data.Attributes == null)
                throw new InvalidOperationException("Webhook data.attributes is null - malformed webhook");
                
            if (string.IsNullOrEmpty(webhookData.Data.Id))
                throw new InvalidOperationException("Webhook data.id is missing - cannot track event");
                
            if (string.IsNullOrEmpty(webhookData.Data.Attributes.Email))
                throw new InvalidOperationException("User email is missing from webhook - cannot resolve user");

            var eventType = MapPatreonEventType(headers, webhookData.Data.Attributes.PatronStatus);
            var userId = await ResolveUserId(webhookData.Data.Attributes.Email);
            var tierIds = ExtractAllTierIdsFromRelationships(webhookData);
            
            if (string.IsNullOrEmpty(userId))
                throw new InvalidOperationException($"Failed to resolve user ID for email: {webhookData.Data.Attributes.Email}");
            
            // Create a single RewardEvent with all entitled tiers
            var rewardEvent = new RewardEvent
            {
                EventId = Guid.NewGuid().ToString(),
                EventType = eventType,
                ProviderId = ProviderId,
                UserId = userId,
                ProviderReference = webhookData.Data.Id,
                Timestamp = DateTime.UtcNow,
                EntitledTierIds = tierIds,
                Metadata = new Dictionary<string, object>
                {
                    ["patron_status"] = webhookData.Data.Attributes.PatronStatus ?? "unknown",
                    ["total_entitled_tiers"] = tierIds.Count
                }
            };
            
            // Validate the event before returning
            rewardEvent.Validate();
            
            return rewardEvent;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to deserialize Patreon webhook payload");
            throw new InvalidOperationException("Invalid JSON in Patreon webhook payload", ex);
        }
        catch (Exception ex) when (!(ex is InvalidOperationException || ex is ArgumentException))
        {
            _logger.LogError(ex, "Unexpected error parsing Patreon webhook event");
            throw new InvalidOperationException("Failed to parse Patreon webhook event", ex);
        }
    }

    public Task<ProviderProduct> GetProduct(string productId)
    {
        return Task.FromResult(new ProviderProduct
        {
            ProductId = productId,
            Name = $"Patreon Tier {productId}",
            Type = ProductType.RecurringSubscription,
            Metadata = new Dictionary<string, object>()
        });
    }

    public Task<string> ResolveUserId(string providerUserEmail)
    {
        // This would typically look up the user mapping in a database
        // For now, return the email as user identifier
        return Task.FromResult(providerUserEmail);
    }

    private string ComputeHmacSignature(string payload)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_webhookSecret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash).ToLower();
    }

    private RewardEventType MapPatreonEventType(Dictionary<string, string> headers, string patronStatus)
    {
        // Use X-Patreon-Event header for precise event type determination
        if (headers != null && headers.TryGetValue("X-Patreon-Event", out var eventHeader))
        {
            return eventHeader switch
            {
                "members:create" => RewardEventType.SubscriptionCreated,
                "members:update" => RewardEventType.SubscriptionRenewed,
                "members:delete" => RewardEventType.SubscriptionCancelled,
                _ => RewardEventType.Purchase
            };
        }

        // Fallback to patron_status if header is not available
        return patronStatus switch
        {
            "active_patron" => RewardEventType.SubscriptionCreated,
            "former_patron" => RewardEventType.SubscriptionCancelled,
            _ => RewardEventType.Purchase
        };
    }

    private List<string> ExtractAllTierIdsFromRelationships(PatreonWebhookData webhookData)
    {
        var tierIds = new List<string>();

        // Use Patreon's recommended approach: currently_entitled_tiers from relationships
        if (webhookData.Data.Relationships?.CurrentlyEntitledTiers?.Data != null)
        {
            tierIds.AddRange(
                webhookData.Data.Relationships.CurrentlyEntitledTiers.Data
                    .Where(tier => tier.Type == "tier")
                    .Select(tier => tier.Id)
                    .Where(id => !string.IsNullOrEmpty(id))
            );
        }

        return tierIds;
    }
}