# Rewards Management System - API Reference

## Webhook Endpoints (Public)

### Patreon Webhook
```
POST /api/rewards/webhooks/patreon
Headers:
  X-Patreon-Signature: [signature]
Body: Patreon webhook payload
```

### Ko-Fi Webhook
```
POST /api/rewards/webhooks/kofi
Content-Type: application/x-www-form-urlencoded
Body: data=[Ko-Fi JSON payload]
```

## Management Endpoints (Admin Only)

All management endpoints require admin authentication.

### Rewards

#### List All Rewards
```
GET /api/rewards
Response: Array of Reward objects
```

#### Get Reward by ID
```
GET /api/rewards/{rewardId}
Response: Reward object
```

#### Create Reward
```
POST /api/rewards
Body: {
  "name": "Premium Portrait Pack",
  "description": "Special portraits for supporters",
  "type": "Portrait",
  "moduleId": "portrait_reward",
  "parameters": {
    "portraitIds": [101, 102, 103]
  },
  "duration": {
    "type": "Permanent",
    "value": 0
  }
}
```

#### Update Reward
```
PUT /api/rewards/{rewardId}
Body: {
  "name": "Updated name",
  "isActive": false
}
```

#### Delete Reward
```
DELETE /api/rewards/{rewardId}
```

### User Assignments

#### Get User's Reward Assignments
```
GET /api/rewards/assignments/{userId}
Response: Array of RewardAssignment objects
```

### Provider Configuration

#### List Provider Configurations
```
GET /api/rewards/providers
Response: Array of ProviderConfiguration objects
```

#### Add Product Mapping
```
POST /api/rewards/providers/{providerId}/mappings
Body: {
  "providerProductId": "tier_1",
  "providerProductName": "Bronze Tier",
  "rewardId": "reward_123",
  "type": "Recurring"
}
```

#### Remove Product Mapping
```
DELETE /api/rewards/providers/{providerId}/mappings/{productId}
```

## Data Models

### Reward
```json
{
  "id": "string",
  "name": "string",
  "description": "string",
  "type": "Portrait|Badge|Title|Cosmetic|Feature|Other",
  "moduleId": "string",
  "parameters": {},
  "duration": {
    "type": "Permanent|Days|Months|Years",
    "value": 0
  },
  "isActive": true,
  "createdAt": "2024-01-01T00:00:00Z",
  "updatedAt": "2024-01-01T00:00:00Z"
}
```

### RewardAssignment
```json
{
  "id": "string",
  "userId": "string",
  "rewardId": "string",
  "providerId": "string",
  "providerReference": "string",
  "status": "Pending|Active|Expired|Revoked|Failed",
  "assignedAt": "2024-01-01T00:00:00Z",
  "expiresAt": "2024-02-01T00:00:00Z",
  "revokedAt": null,
  "revokedReason": null,
  "metadata": {}
}
```

### ProviderConfiguration
```json
{
  "id": "string",
  "providerId": "string",
  "providerName": "string",
  "isActive": true,
  "settings": {},
  "productMappings": [
    {
      "providerProductId": "string",
      "providerProductName": "string",
      "rewardId": "string",
      "type": "OneTime|Recurring|Tiered",
      "additionalParameters": {}
    }
  ],
  "createdAt": "2024-01-01T00:00:00Z",
  "updatedAt": "2024-01-01T00:00:00Z"
}
```

## Integration Flow

1. **Setup Provider Configuration**
   - Configure webhook secrets in environment variables
   - Add provider configuration via API

2. **Create Rewards**
   - Create reward definitions with appropriate modules
   - Set parameters for each reward type

3. **Map Products to Rewards**
   - Use product mapping endpoints to link provider products to rewards
   - Configure tiered rewards for different subscription levels

4. **Webhook Processing**
   - Provider sends webhook to appropriate endpoint
   - System validates signature/token
   - Parses event and assigns rewards automatically
   - Sends announcements for donations/subscriptions

5. **Monitor Assignments**
   - Check user assignments via API
   - Monitor expiration processing
   - Review logs for webhook processing

## Environment Variables

```bash
# Patreon Configuration
PATREON_WEBHOOK_SECRET=your_patreon_webhook_secret

# Ko-Fi Configuration
KOFI_VERIFICATION_TOKEN=your_kofi_verification_token

# MongoDB Connection
MONGO_CONNECTION_STRING=mongodb://localhost:27017
```

## Security Notes

- All webhook endpoints validate signatures/tokens
- Management endpoints require admin authentication
- Webhook processing is idempotent (prevents duplicate rewards)
- Sensitive configuration stored as environment variables
- All reward operations are logged for audit