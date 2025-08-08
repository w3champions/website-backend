# Rewards Management System Architecture

## Overview

The Rewards Management System is a flexible, provider-agnostic system for managing user rewards acquired through various payment providers (Patreon, Ko-Fi, etc.). The system supports both permanent rewards and time-limited rewards that can be refreshed through recurring memberships or additional purchases.

## Core Components

### 1. Domain Layer (`W3C.Domain/Rewards`)

#### Core Entities
- **Reward**: Base abstract class for all reward types
- **RewardInstance**: A specific instance of a reward assigned to a user
- **RewardProvider**: Abstract base for payment provider integrations
- **RewardAssignment**: Links a reward to a user with metadata

#### Value Objects
- **RewardId**: Unique identifier for reward types
- **ProviderId**: Unique identifier for payment providers
- **RewardDuration**: Represents reward validity period
- **RewardStatus**: Active, Expired, Revoked, Pending

### 2. Application Layer (`W3ChampionsStatisticService/Rewards`)

#### Services
- **RewardService**: Core business logic for reward management
- **RewardAssignmentService**: Handles assignment and expiration logic
- **RewardSyncService**: Synchronizes rewards with provider webhooks

#### Handlers
- **RewardWebhookHandler**: Processes incoming webhook events
- **RewardExpirationHandler**: Background service for expiration checks

### 3. Infrastructure Layer

#### Repositories
- **RewardRepository**: MongoDB persistence for rewards
- **RewardAssignmentRepository**: User-reward assignment persistence
- **RewardProviderConfigRepository**: Provider configuration storage

#### Provider Integrations
- **PatreonProvider**: Patreon-specific webhook handling
- **KoFiProvider**: Ko-Fi-specific webhook handling

## Architecture Patterns

### Provider Abstraction

```
IRewardProvider (Interface)
├── PatreonRewardProvider
├── KoFiRewardProvider
└── [Future providers...]
```

Each provider implements:
- Webhook signature validation
- Event parsing and normalization
- Provider-specific reward mapping

### Reward Module System

Rewards are implemented as pluggable modules:
- Each module implements `IRewardModule` interface
- Modules are registered at startup
- Configuration maps provider products to modules
- Modules can have parameters (e.g., portrait ID, badge type)

### Event-Driven Processing

1. **Webhook Reception**: Provider sends webhook to endpoint
2. **Validation**: Signature verification and event validation
3. **Normalization**: Convert to internal reward event format
4. **Processing**: Apply business rules and assign rewards
5. **Notification**: Update user profile and send notifications

## Data Flow

```
Provider Webhook → Webhook Controller → Provider Adapter
                                      ↓
                              Reward Service
                                      ↓
                        Reward Assignment Service
                                      ↓
                              Database Update
                                      ↓
                           User Profile Update
```

## Key Design Decisions

1. **Provider Agnostic Core**: Core reward logic is independent of payment providers
2. **Module-Based Rewards**: Rewards are programmable components with parameters
3. **Event Sourcing Pattern**: Webhook events are stored for audit and replay
4. **Expiration Management**: Background service handles time-based expiration
5. **Idempotent Operations**: Webhook processing is idempotent to handle retries

## Database Schema

### Rewards Collection
```json
{
  "_id": "reward_id",
  "name": "Premium Portrait Pack",
  "type": "portrait",
  "module": "PortraitRewardModule",
  "parameters": {
    "portraitIds": [101, 102, 103]
  },
  "duration": {
    "type": "permanent|temporary",
    "days": 30
  }
}
```

### RewardAssignments Collection
```json
{
  "_id": "assignment_id",
  "userId": "user#123",
  "rewardId": "reward_id",
  "providerId": "patreon",
  "providerReference": "patreon_order_123",
  "status": "active",
  "assignedAt": "2024-01-01T00:00:00Z",
  "expiresAt": "2024-02-01T00:00:00Z",
  "metadata": {}
}
```

### ProviderConfigurations Collection
```json
{
  "_id": "provider_config_id",
  "providerId": "patreon",
  "webhookSecret": "encrypted_secret",
  "productMappings": [
    {
      "providerProductId": "tier_1",
      "rewardId": "premium_portrait_pack",
      "duration": "monthly"
    }
  ]
}
```

## Security Considerations

1. **Webhook Validation**: All webhooks are validated using provider-specific signatures
2. **Idempotency Keys**: Prevent duplicate reward assignments
3. **Audit Logging**: All reward operations are logged for audit
4. **Encryption**: Sensitive provider data is encrypted at rest
5. **Rate Limiting**: Webhook endpoints are rate-limited

## Extensibility

The system is designed for easy extension:
- New providers can be added by implementing `IRewardProvider`
- New reward types can be added by implementing `IRewardModule`
- Provider mappings are managed via API, not code changes
- Module parameters allow flexible reward configuration