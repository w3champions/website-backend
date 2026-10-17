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

# Run against a local MongoDB (see MongoDB Safety)
TEST_MONGO_CONNECTION_STRING=mongodb://127.0.0.1:27017/ dotnet test
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
- `NETDIAG_CORE_SERVERS` - JSON array `[{key,label,ip,tcpPort}]` of core servers measurable by network diagnostics (default `[]`). Malformed JSON, duplicate keys or invalid IPv4/ports stop startup
- `NETDIAG_BASELINES` - JSON array `[{key,label,region,ip,tcpPort}]`; defaults to the built-in public resolvers (`cloudflare`, `google`, `quad9`, `alidns`, `dnspod`, `yandex`)
- `GEOIP_CITY_DB_PATH` / `GEOIP_ASN_DB_PATH` - GeoLite2 files (defaults `/geoip/GeoLite2-City.mmdb`, `/geoip/GeoLite2-ASN.mmdb`), kept fresh by the `geoipupdate` sidecar in docker-compose-files. Missing files never fail startup: results are stored without geo until they appear

## Important Notes

### MongoDB Safety
- Default connection uses test database
- **Never enable read model handlers locally** unless you know what you're doing
- Can overwrite prod/test data if connected to wrong database
- **`IntegrationTestBase` drops the whole database before every test.** Redirect it with `TEST_MONGO_CONNECTION_STRING`; don't edit the source, since a rebase or checkout silently restores the shared default

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
- A download is a ticket (`POST tickets`, bearer) plus a plain `GET download/{ticket}` that streams the ZIP. At most 2 exports stream at the same time.
- Finished downloads go to the `ReplayExports` collection (no ZIP is kept). `ReplayExportIndexService` builds the search indexes 30 s after the start, unless `REPLAY_EXPORT_INDEXES=off` or the disk is short.
- Tests use Mongo2Go (`FullyQualifiedName~ReplayExport`), never a shared database.

### Network diagnostics
- Feature folder `W3ChampionsStatisticService/NetworkDiagnostics/`. Admin API `api/network-diagnostics/*` (`targets`, `runs`, `measurements`, `observations`, `aggregate`) behind `EPermission.Proxies`; a missing permission answers 401. The launcher submits results with `POST api/network-diagnostics/results/{measurementId}` (per-assignment token, never logged and never in an admin response). Requests reach launchers as the hub event `NetworkDiagnosticsRequest`, due slots are pushed by the 5 s `NetworkDiagnosticsDispatcher`.
- Admin reads are limited to 15 s (503 `query_timeout`); aggregates and target-filtered measurement lookups look at the newest 200 000 matching observations and say so with the `X-NetDiag-Truncated: true` header. The `observations` export returns only rows older than 30 s (`NetDiagConstants.ObservationSettleMargin`); unknown query parameter names are ignored without an error.
- Logic lives in pure classes (validators, `SeriesStats`, `ResultBuilder`, `AggregateBuilder`, `AudiencePlanner`) with plain NUnit tests. Mongo behaviour is tested in `*MongoTests` fixtures (Mongo2Go: CI runs them, they do not run on arm64 macs); filter with `--filter "FullyQualifiedName~NetworkDiagnostics"`, never an unfiltered `dotnet test`.
- LLM access: the `network-diagnostics` skill in `.claude/skills/network-diagnostics/`.

### Service Registration
- Services registered in `Program.cs` and feature-specific extension methods
- Rewards services in `RewardServiceExtensions.cs`
- Tracing services in `TracingServiceCollectionExtensions.cs`