using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using W3C.Domain.Rewards.Abstractions;
using W3C.Domain.Rewards.Events;

namespace W3ChampionsStatisticService.Rewards.Providers.Patreon;

public class PatreonProvider : IRewardProvider
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<PatreonProvider> _logger;
    private readonly string _webhookSecret;

    public PatreonProvider(IConfiguration configuration, ILogger<PatreonProvider> logger)
    {
        _configuration = configuration;
        _logger = logger;
        _webhookSecret = configuration["Rewards:Providers:Patreon:WebhookSecret"];
    }

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

    public async Task<RewardEvent> ParseWebhookEvent(string payload)
    {
        try
        {
            var webhookData = JsonSerializer.Deserialize<PatreonWebhookData>(payload);
            
            var eventType = MapPatreonEventType(webhookData.Data.Attributes.PatronStatus);
            var userId = await ResolveUserId(webhookData.Data.Attributes.Email);
            
            return new RewardEvent
            {
                EventId = Guid.NewGuid().ToString(),
                EventType = eventType,
                ProviderId = ProviderId,
                UserId = userId,
                ProductId = webhookData.Data.Attributes.TierId,
                ProviderReference = webhookData.Data.Id,
                Timestamp = DateTime.UtcNow,
                Metadata = new Dictionary<string, object>
                {
                    ["tier_id"] = webhookData.Data.Attributes.TierId
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error parsing Patreon webhook event");
            throw;
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

    private RewardEventType MapPatreonEventType(string patronStatus)
    {
        return patronStatus switch
        {
            "active_patron" => RewardEventType.SubscriptionCreated,
            "former_patron" => RewardEventType.SubscriptionCancelled,
            _ => RewardEventType.Purchase
        };
    }
}