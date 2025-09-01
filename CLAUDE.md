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
- **WC3ChampionsStatisticService.Tests** - Unit and integration tests

### Key Technologies
- **.NET 8.0** with ASP.NET Core
- **MongoDB** for persistence (default test DB: `mongodb://157.90.1.251:3513`)
- **SignalR** for real-time communication (WebsiteBackendHub at `/websiteBackendHub`)
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

## Important Notes

### MongoDB Safety
- Default connection uses test database
- **Never enable read model handlers locally** unless you know what you're doing
- Can overwrite prod/test data if connected to wrong database

### Integration Points
- WebsiteBackendHub provides SignalR connection for real-time updates
- Multiple consumers: website frontend, launcher, potentially other services
- Breaking API changes require coordination across repositories

### Service Registration
- Services registered in `Program.cs` and feature-specific extension methods
- Rewards services in `RewardServiceExtensions.cs`
- Tracing services in `TracingServiceCollectionExtensions.cs`