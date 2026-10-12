# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Related Repositories

- **Frontend**: `../website` - Vue 3 frontend that consumes APIs from this backend
- **Launcher**: `../launcher-e` - Desktop application using WebsiteBackendHub SignalR and REST APIs
  
When making API changes, ensure compatibility with both frontend and launcher clients.

## Development Commands

### Build and Run
```bash
# Build the solution
dotnet build

# Run the service
dotnet run --project W3ChampionsStatisticService

# Run with specific environment
dotnet run --project W3ChampionsStatisticService --environment Development
```

### Testing
```bash
# Run all tests
dotnet test

# Run specific test project
dotnet test WC3ChampionsStatisticService.UnitTests

# Run with verbosity
dotnet test --logger "console;verbosity=detailed"
```

### Code Quality
```bash
# Format code (verify no changes needed)
dotnet format --verify-no-changes --verbosity diagnostic

# Format code (apply changes)
dotnet format
```

### Docker
```bash
# Build Docker image
docker build -t w3champions-backend .

# Run with MongoDB connection
docker run -e MONGO_CONNECTION_STRING="mongodb://localhost:27017" w3champions-backend
```

## Architecture Overview

### Solution Structure
- **W3ChampionsStatisticService** - Main ASP.NET Core web service
- **W3C.Domain** - Domain models and repository interfaces
- **W3C.Contracts** - Shared contracts and DTOs
- **WC3ChampionsStatisticService.UnitTests** - Unit and integration tests

### Key Technologies
- **.NET 8.0** with ASP.NET Core
- **MongoDB** for persistence (`MONGO_CONNECTION_STRING` is required, no default)
- **SignalR** for real-time communication (WebsiteBackendHub at `/websiteBackendHub`)
- **Outbound HTTP** only via `IHttpClientFactory` named clients (`W3C.Domain/HttpClientNames.cs`), registered with explicit timeouts in `Extensions/HttpClientServiceCollectionExtensions.cs`; never `new HttpClient()`, and put per-call headers (e.g. the admin secret) on the request, not on `DefaultRequestHeaders`
- **JWT Bearer Authentication** with Battle.net OAuth
- **Serilog** for structured logging
- **Prometheus** metrics
- **Swagger** API documentation

### Core Domain Areas

#### Read Model System
- Event-driven architecture processing MatchFinishedEvents from matchmaking service
- Handlers transform events into website-specific read models
- **CAUTION**: Read model handling is OFF by default locally to prevent data corruption
- Each handler tracked in `HandlerVersions` collection with 5-second retry on failure

#### Authentication & Authorization
- JWT Bearer tokens from Battle.net OAuth
- Admin permissions checked via `CheckIfBattleTagIsAdminFilter`
- Acting player injection via `InjectActingPlayerAuthCodeAttribute`
- Basic auth for service-to-service communication

#### Major Feature Areas
1. **Player Profiles & Statistics** - MMR, race stats, game history
2. **Ladder & Rankings** - Season-based rankings, leagues, country rankings
3. **Matches** - Match history, ongoing matches, replays
4. **Clans** - Clan management, memberships
5. **Personal Settings** - User preferences, portraits, avatars
6. **Rewards System** - Patreon/Ko-Fi integration, portrait rewards
7. **Admin Tools** - User management, logs, cloud storage
8. **Real-time Features** - Chat, friend lists via SignalR Hub

### Database Collections Pattern
- Repository pattern with MongoDB implementation
- Base repository: `MongoDbRepositoryBase<T>`
- Collections named after domain entities (e.g., `PlayerWinrate`, `Clans`)
- Handler versions tracked in `HandlerVersions` collection

### API Controllers Location
- Main controllers in `/W3ChampionsStatisticService/[Feature]/`
- Rewards controllers in `/W3ChampionsStatisticService/Rewards/Controllers/`
- Admin features in `/W3ChampionsStatisticService/Admin/`

### Environment Variables
- `MONGO_CONNECTION_STRING` - MongoDB connection (required)
- `APP_INSIGHTS` - Application Insights key
- `IS_DEVELOPMENT` - Disables read model handlers when true
- `REWARD_PATREON_CLIENT_ID` - Patreon OAuth client ID
- `REWARD_PATREON_CLIENT_SECRET` - Patreon OAuth client secret
- `REPLAY_EXPORT_INDEXES` - `off` skips the background build of the replay export indexes
- `STRIPE_SECRET_KEY` - Stripe secret key of the replay store (`sk_test_…` for test mode, `sk_live_…` in production). Without it, the store shows its prices but takes no payment (`checkout` answers 503)
- `STRIPE_WEBHOOK_SECRET` - `whsec_…` of the Stripe webhook endpoint `api/replay-store/stripe/webhook`
- `STRIPE_API_BASE` - only for local tests against a stand-in of the Stripe API

## Important Notes

### MongoDB Safety
- Default connection uses test database
- **Never enable read model handlers locally** unless you know what you're doing
- Can overwrite prod/test data if connected to wrong database

### Integration Points
- WebsiteBackendHub provides SignalR connection for real-time updates
- Multiple consumers: website frontend, launcher, potentially other services
- Breaking API changes require coordination across repositories

### Commercial license and commercial events
- `Admin/CommercialLicenseController.cs` (tagged accounts) and `Admin/CommercialEventsController.cs` (allocations, events, people, games, audit, role hints) proxy the matchmaking admin API behind `EPermission.CommercialLicense`. matchmaking-service owns all data and rules; the system is documented in `../matchmaking-service/docs/commercial-events.md`.
- Battle-tag route values are named `targetBattleTag`, because `BearerHasPermissionFilter` overwrites any action argument named `battleTag` with the acting admin, which is forwarded as `actingBattleTag`. Path values are re-encoded with `Uri.EscapeDataString`.
- Commercial-events endpoints answer matchmaking 4xx errors with matchmaking's JSON body verbatim and 5xx errors with `{"error":"Matchmaking service error","code":"INTERNAL"}` (`MatchmakingPassthroughExceptionFilter`). Other endpoints use `HandleMMError`.

### Replay export
- `Admin/ReplayExport/` is the admin bulk download of replays (permission `Replays`). Mongo `Matchup` selects the games; replay-service builds each .w3g, as for the single-replay download.
- A download is a ticket (`POST tickets`, bearer) plus a plain `GET download/{ticket}` that streams the ZIP. The admin export and the store each have 2 slots, and a ticket downloads only on the route of the controller that gave it (`Scope`), so buyers cannot take admin slots; one user runs one download at a time, and a new ticket replaces the user's unused one. A client slower than 16 KB/s loses the download.
- Finished downloads go to the `ReplayExports` collection (no ZIP is kept). `ReplayExportIndexService` builds the search indexes 30 s after the start, unless `REPLAY_EXPORT_INDEXES=off` or the disk is short.
- Tests use Mongo2Go (`FullyQualifiedName~ReplayExport`), never a shared database.

### Replay store
- `ReplayStore/` is the replay store (`api/replay-store`, page `/replays` on the website): a signed-in user searches like the admin export and pays with Stripe Checkout (hosted page) before the download. `ReplayExportControllerBase` holds the search, ticket and ZIP code that both controllers share.
- Not open to all users yet: `[BearerSignedIn(OnlyWithPermission = StorePreviewPermission)]` lets only admins with the Replays permission in (others get 403). For the launch, remove `OnlyWithPermission` and open `useReplayStoreAccess` on the website.
- Price: `ReplayExportPricing` ($0.02 per replay, $5.00 minimum, USD, at most 5,000 per purchase). The server sets the price; only games with a replay (`FloMatchId > 0`) count. At most 10 payment pages per user and hour; at most 4 searches at the same time.
- Flow: `POST checkout` saves a `ReplayOrders` document (Pending) and opens the Stripe session (32 min; Stripe's minimum is 30). Stripe sends the buyer back to `/replays?order=…&session_id=…`; the page calls `POST orders/{id}/confirm`, and the webhook (`checkout.session.completed`, `…async_payment_succeeded`, `…async_payment_failed`, `…expired`) does the same when the buyer does not come back. Both ask Stripe for the session, check amount, currency and session id, and save the purchase once: a `ReplayExports` record with `_id` = order id and `Charged = true` (a duplicate key means it is saved already).
- A purchase downloads again at no cost from `GET purchases` / `POST purchases/{id}/tickets`. The buyer sees only their own downloads of it, not those of admins.
- `options` needs no sign-in, so it is cached for 10 minutes per game mode (the map names read the matches of the season).
- The webhook reads only `type` and `data.object.id` (after `EventUtility.ValidateSignature`), so the API version of the Stripe endpoint does not matter. `confirm` asks Stripe at most once per order in 2 s.
- Payment methods come from the Stripe Dashboard (cards, wallets, PayPal for accounts in Europe); the code sets none. The return URL is the request's Origin when it is `https://w3champions.com`, `https://*.w3champions.com` or a loopback address.
- Stripe calls go through the named HttpClient `HttpClientNames.Stripe` (20 s). Tests: `FullyQualifiedName~ReplayStore` (Mongo2Go and a fake `IReplayPayments`).

### Service Registration
- Services registered in `Program.cs` and feature-specific extension methods
- Rewards services in `RewardServiceExtensions.cs`
- Tracing services in `TracingServiceCollectionExtensions.cs`