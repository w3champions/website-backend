# Cloudflare Turnstile Integration

This document describes the Cloudflare Turnstile captcha integration for protecting endpoints from abuse.

## Configuration

### Backend
Set the Turnstile secret key environment variable:
```bash
export TURNSTILE_SECRET_KEY="your-secret-key-here"
```

If this environment variable is not set, Turnstile verification will be disabled and all requests will pass through.

### Frontend
Set the Turnstile site key in `public/env.js`:
```javascript
window._env_ = {
  // ... other config
  TURNSTILE_SITE_KEY: "your-site-key-here",
};
```

## Protected Endpoints

Currently, the following endpoints are protected with Turnstile:
- `GET /api/replays/{gameId}` - Download replay by game ID
- `GET /api/replays/by-flo-id/{floMatchId}` - Download replay by FLO match ID

## How It Works

### Backend Flow
1. Client sends request with `X-Turnstile-Token` header
2. `TurnstileVerificationFilter` intercepts the request
3. Token is verified with Cloudflare's API
4. Successful verifications are cached for 5 minutes
5. Invalid tokens return 401 Unauthorized
6. Service errors return 503 Service Unavailable

### Frontend Flow
1. User clicks download button
2. TurnstileService checks if enabled
3. If enabled, shows Turnstile widget
4. User completes challenge
5. Token is sent with download request
6. File downloads on success

## Adding Turnstile to New Endpoints

### Backend
Add the `[TurnstileVerification]` attribute to any controller method:

```csharp
[HttpGet("protected-endpoint")]
[TurnstileVerification]
public async Task<IActionResult> ProtectedEndpoint()
{
    // Your code here
}
```

### Frontend
Use the TurnstileService to get a token before making API calls:

```typescript
import { TurnstileService } from "@/services/TurnstileService";

const turnstileService = TurnstileService.getInstance();

// Check if Turnstile is enabled
if (turnstileService.isEnabled()) {
  // Get token (will show widget if needed)
  const token = await turnstileService.getToken("action-name");
  
  if (token) {
    // Make API call with token
    const response = await fetch(url, {
      headers: {
        "X-Turnstile-Token": token,
      },
    });
  }
}
```

## Logging

The backend logs all Turnstile verification attempts including:
- Endpoint being accessed
- Client IP address
- User agent
- Verification result

Failed verifications and missing tokens are logged at the Warning level.
Service errors are logged at the Error level.

## Development Mode

When `TURNSTILE_SECRET_KEY` is not set, Turnstile verification is automatically disabled, allowing for easier local development and testing.

## Troubleshooting

### "Turnstile token missing from request"
- Ensure the frontend is sending the `X-Turnstile-Token` header
- Check that Turnstile is enabled in the frontend (site key is set)

### "Turnstile verification failed"
- Token may be expired (tokens are valid for 5 minutes)
- Token may have already been used
- User may have failed the challenge

### "Unable to verify captcha at this time"
- Cloudflare's API may be unavailable
- Network issues between server and Cloudflare
- Invalid secret key configuration