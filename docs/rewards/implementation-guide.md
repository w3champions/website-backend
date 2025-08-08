# Rewards Management System - Implementation Guide

## Quick Start

This guide covers the implementation of the rewards management system for W3Champions.

## Project Structure

```
W3C.Domain/
├── Rewards/
│   ├── Abstractions/
│   │   ├── IRewardProvider.cs
│   │   ├── IRewardModule.cs
│   │   └── IRewardService.cs
│   ├── Entities/
│   │   ├── Reward.cs
│   │   ├── RewardInstance.cs
│   │   └── RewardAssignment.cs
│   ├── ValueObjects/
│   │   ├── RewardId.cs
│   │   ├── RewardDuration.cs
│   │   └── RewardStatus.cs
│   └── Events/
│       ├── RewardAssignedEvent.cs
│       ├── RewardExpiredEvent.cs
│       └── RewardRevokedEvent.cs

W3ChampionsStatisticService/
├── Rewards/
│   ├── Services/
│   │   ├── RewardService.cs
│   │   ├── RewardAssignmentService.cs
│   │   └── RewardSyncService.cs
│   ├── Providers/
│   │   ├── Patreon/
│   │   │   ├── PatreonProvider.cs
│   │   │   ├── PatreonWebhookModels.cs
│   │   │   └── PatreonConfiguration.cs
│   │   └── KoFi/
│   │       ├── KoFiProvider.cs
│   │       ├── KoFiWebhookModels.cs
│   │       └── KoFiConfiguration.cs
│   ├── Modules/
│   │   ├── PortraitRewardModule.cs
│   │   ├── BadgeRewardModule.cs
│   │   └── CustomTitleRewardModule.cs
│   ├── Repositories/
│   │   ├── RewardRepository.cs
│   │   └── RewardAssignmentRepository.cs
│   ├── Controllers/
│   │   ├── RewardWebhookController.cs
│   │   └── RewardManagementController.cs
│   └── Handlers/
│       └── RewardExpirationHandler.cs
```

## Core Interfaces

### IRewardProvider
```csharp
public interface IRewardProvider
{
    string ProviderId { get; }
    Task<bool> ValidateWebhookSignature(string payload, string signature);
    Task<RewardEvent> ParseWebhookEvent(string payload);
    Task<ProviderProduct> GetProduct(string productId);
}
```

### IRewardModule
```csharp
public interface IRewardModule
{
    string ModuleId { get; }
    Task<RewardApplicationResult> Apply(RewardContext context);
    Task<RewardRevocationResult> Revoke(RewardContext context);
    bool SupportsParameters { get; }
    Task<ValidationResult> ValidateParameters(Dictionary<string, object> parameters);
}
```

## Implementation Steps

### Step 1: Domain Entities

Create the core domain entities in `W3C.Domain/Rewards/`:

```csharp
// Reward.cs
public abstract class Reward
{
    public RewardId Id { get; protected set; }
    public string Name { get; protected set; }
    public string Description { get; protected set; }
    public RewardType Type { get; protected set; }
    public string ModuleId { get; protected set; }
    public Dictionary<string, object> Parameters { get; protected set; }
    public RewardDuration Duration { get; protected set; }
    
    public abstract Task<bool> CanBeAssignedTo(string userId);
}

// RewardAssignment.cs
public class RewardAssignment
{
    public string Id { get; private set; }
    public string UserId { get; private set; }
    public RewardId RewardId { get; private set; }
    public string ProviderId { get; private set; }
    public string ProviderReference { get; private set; }
    public RewardStatus Status { get; private set; }
    public DateTime AssignedAt { get; private set; }
    public DateTime? ExpiresAt { get; private set; }
    public Dictionary<string, object> Metadata { get; private set; }
    
    public bool IsExpired() => ExpiresAt.HasValue && ExpiresAt.Value <= DateTime.UtcNow;
    public void Expire() => Status = RewardStatus.Expired;
    public void Revoke() => Status = RewardStatus.Revoked;
}
```

### Step 2: Provider Implementation

Example Patreon provider implementation:

```csharp
// PatreonProvider.cs
public class PatreonProvider : IRewardProvider
{
    private readonly PatreonConfiguration _config;
    private readonly ILogger<PatreonProvider> _logger;
    
    public string ProviderId => "patreon";
    
    public async Task<bool> ValidateWebhookSignature(string payload, string signature)
    {
        // Implement Patreon webhook signature validation
        var computedSignature = ComputeHMAC(_config.WebhookSecret, payload);
        return signature == computedSignature;
    }
    
    public async Task<RewardEvent> ParseWebhookEvent(string payload)
    {
        var patreonEvent = JsonSerializer.Deserialize<PatreonWebhookEvent>(payload);
        
        return new RewardEvent
        {
            EventType = MapEventType(patreonEvent.Type),
            UserId = await ResolveUserId(patreonEvent.Data.Relationships.User.Id),
            ProductId = patreonEvent.Data.Attributes.TierId,
            Amount = patreonEvent.Data.Attributes.Amount,
            Currency = patreonEvent.Data.Attributes.Currency,
            ProviderReference = patreonEvent.Data.Id,
            Timestamp = patreonEvent.Data.Attributes.CreatedAt
        };
    }
}
```

### Step 3: Reward Module Implementation

Example Portrait reward module:

```csharp
// PortraitRewardModule.cs
public class PortraitRewardModule : IRewardModule
{
    private readonly IPortraitRepository _portraitRepo;
    private readonly IPersonalSettingsRepository _settingsRepo;
    
    public string ModuleId => "portrait_reward";
    public bool SupportsParameters => true;
    
    public async Task<RewardApplicationResult> Apply(RewardContext context)
    {
        var portraitIds = context.Parameters["portraitIds"] as List<int>;
        var settings = await _settingsRepo.Load(context.UserId);
        
        foreach (var portraitId in portraitIds)
        {
            settings.SpecialPictures = settings.SpecialPictures
                .Append(new SpecialPicture { PictureId = portraitId })
                .ToArray();
        }
        
        await _settingsRepo.Save(settings);
        
        return new RewardApplicationResult
        {
            Success = true,
            Message = $"Added {portraitIds.Count} special portraits"
        };
    }
    
    public async Task<RewardRevocationResult> Revoke(RewardContext context)
    {
        var portraitIds = context.Parameters["portraitIds"] as List<int>;
        var settings = await _settingsRepo.Load(context.UserId);
        
        settings.SpecialPictures = settings.SpecialPictures
            .Where(p => !portraitIds.Contains(p.PictureId))
            .ToArray();
        
        await _settingsRepo.Save(settings);
        
        return new RewardRevocationResult { Success = true };
    }
}
```

### Step 4: Webhook Controller

```csharp
// RewardWebhookController.cs
[ApiController]
[Route("api/rewards/webhooks")]
public class RewardWebhookController : ControllerBase
{
    private readonly IRewardService _rewardService;
    private readonly IServiceProvider _serviceProvider;
    
    [HttpPost("{provider}")]
    public async Task<IActionResult> HandleWebhook(
        string provider,
        [FromBody] string payload,
        [FromHeader(Name = "X-Webhook-Signature")] string signature)
    {
        var rewardProvider = _serviceProvider
            .GetServices<IRewardProvider>()
            .FirstOrDefault(p => p.ProviderId == provider);
        
        if (rewardProvider == null)
            return BadRequest($"Unknown provider: {provider}");
        
        if (!await rewardProvider.ValidateWebhookSignature(payload, signature))
            return Unauthorized("Invalid signature");
        
        var rewardEvent = await rewardProvider.ParseWebhookEvent(payload);
        await _rewardService.ProcessRewardEvent(rewardEvent);
        
        return Ok();
    }
}
```

### Step 5: Service Registration

Register services in Program.cs:

```csharp
// In Program.cs
builder.Services.AddScoped<IRewardService, RewardService>();
builder.Services.AddScoped<IRewardAssignmentService, RewardAssignmentService>();
builder.Services.AddScoped<IRewardRepository, RewardRepository>();
builder.Services.AddScoped<IRewardAssignmentRepository, RewardAssignmentRepository>();

// Register providers
builder.Services.AddScoped<IRewardProvider, PatreonProvider>();
builder.Services.AddScoped<IRewardProvider, KoFiProvider>();

// Register modules
builder.Services.AddScoped<IRewardModule, PortraitRewardModule>();
builder.Services.AddScoped<IRewardModule, BadgeRewardModule>();

// Register background service for expiration
builder.Services.AddHostedService<RewardExpirationHandler>();
```

## Configuration

### appsettings.json
```json
{
  "Rewards": {
    "Providers": {
      "Patreon": {
        "WebhookSecret": "${PATREON_WEBHOOK_SECRET}",
        "ClientId": "${PATREON_CLIENT_ID}",
        "ClientSecret": "${PATREON_CLIENT_SECRET}"
      },
      "KoFi": {
        "WebhookToken": "${KOFI_WEBHOOK_TOKEN}"
      }
    },
    "ExpirationCheckInterval": "00:30:00"
  }
}
```

## Testing

### Unit Tests
- Test reward assignment logic
- Test expiration logic
- Test provider webhook parsing
- Test module application/revocation

### Integration Tests
- Test webhook endpoints
- Test database operations
- Test full reward flow

## Deployment Considerations

1. **Environment Variables**: Set provider secrets as environment variables
2. **Database Indexes**: Create indexes on userId and status fields
3. **Monitoring**: Add metrics for reward assignments and expirations
4. **Webhook URLs**: Configure provider webhook URLs:
   - Patreon: `https://api.w3champions.com/api/rewards/webhooks/patreon`
   - Ko-Fi: `https://api.w3champions.com/api/rewards/webhooks/kofi`

## API Endpoints

### Management Endpoints
- `GET /api/rewards` - List all rewards
- `POST /api/rewards` - Create new reward
- `PUT /api/rewards/{id}` - Update reward
- `DELETE /api/rewards/{id}` - Delete reward
- `GET /api/rewards/assignments/{userId}` - Get user's rewards
- `POST /api/rewards/mappings` - Configure provider product mappings

### Webhook Endpoints
- `POST /api/rewards/webhooks/{provider}` - Provider webhook endpoint

## Monitoring and Logging

Key metrics to track:
- Webhook processing time
- Reward assignment success/failure rate
- Expiration processing performance
- Provider API response times

Logging points:
- Webhook receipt and validation
- Reward assignment decisions
- Expiration events
- Error conditions