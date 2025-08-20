using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using W3C.Domain.Rewards.Abstractions;
using W3C.Domain.Rewards.Events;
using W3C.Domain.Rewards.Repositories;

namespace W3ChampionsStatisticService.Rewards.Providers.Patreon;

public class PatreonProvider(ILogger<PatreonProvider> logger, IPatreonAccountLinkRepository patreonLinkRepository) : IRewardProvider
{
    private readonly ILogger<PatreonProvider> _logger = logger;
    private readonly IPatreonAccountLinkRepository _patreonLinkRepository = patreonLinkRepository;
    private readonly string _webhookSecret = Environment.GetEnvironmentVariable("PATREON_WEBHOOK_SECRET");

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

            var eventType = MapPatreonEventType(headers, webhookData.Data.Attributes.PatronStatus);
            var userId = await ResolveUserId(webhookData.Data.Id);
            var tierIds = ExtractAllTierIdsFromRelationships(webhookData);
            
            if (string.IsNullOrEmpty(userId))
            {
                _logger.LogInformation("No BattleTag linked for PatreonUserId {PatreonUserId}. Skipping webhook.", webhookData.Data.Id);
                return null; // Skip this webhook - no linked account
            }
            
            // Create a single RewardEvent with all entitled tiers
            // Use Patreon's webhook event ID for idempotency instead of generating new GUID
            var rewardEvent = new RewardEvent
            {
                EventId = $"patreon_{webhookData.Data.Id}",
                EventType = eventType,
                ProviderId = ProviderId,
                UserId = userId,
                ProviderReference = webhookData.Data.Id,
                Timestamp = DateTime.UtcNow,
                EntitledTierIds = tierIds,
                Metadata = new Dictionary<string, object>
                {
                    ["patron_status"] = webhookData.Data.Attributes.PatronStatus ?? "unknown",
                    ["total_entitled_tiers"] = tierIds.Count,
                    ["patreon_user_id"] = webhookData.Data.Id
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

    public async Task<string> ResolveUserId(string patreonUserId)
    {
        try
        {
            var accountLink = await _patreonLinkRepository.GetByPatreonUserId(patreonUserId);
            if (accountLink != null)
            {
                _logger.LogDebug("Resolved PatreonUserId {PatreonUserId} to BattleTag {BattleTag}", patreonUserId, accountLink.BattleTag);
                return accountLink.BattleTag;
            }
            
            _logger.LogDebug("No BattleTag found for PatreonUserId {PatreonUserId}", patreonUserId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resolving PatreonUserId {PatreonUserId} to BattleTag", patreonUserId);
            return null;
        }
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