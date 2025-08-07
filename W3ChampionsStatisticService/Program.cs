using Microsoft.ApplicationInsights.Extensibility.Implementation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi.Models;
using MongoDB.Driver;
using Prometheus;
using Serilog;
using System;
using System.Collections.Generic;
using System.Net;
using IPNetwork = Microsoft.AspNetCore.HttpOverrides.IPNetwork;

using W3C.Domain.ChatService;
using W3C.Domain.CommonValueObjects;
using W3C.Domain.MatchmakingService;
using W3C.Domain.Repositories;
using W3C.Domain.UpdateService;

using W3ChampionsStatisticService.Admin;
using W3ChampionsStatisticService.Admin.Logs;
using W3ChampionsStatisticService.Cache;
using W3ChampionsStatisticService.Clans;
using W3ChampionsStatisticService.Friends;
using W3ChampionsStatisticService.Heroes;
using W3ChampionsStatisticService.Hubs;
using W3ChampionsStatisticService.Ladder;
using W3ChampionsStatisticService.Matches;
using W3ChampionsStatisticService.PersonalSettings;
using W3ChampionsStatisticService.PlayerProfiles;
using W3ChampionsStatisticService.PlayerProfiles.GameModeStats;
using W3ChampionsStatisticService.PlayerProfiles.MmrRankingStats;
using W3ChampionsStatisticService.PlayerProfiles.RaceStats;
using W3ChampionsStatisticService.PlayerProfiles.War3InfoPlayerAkas;
using W3ChampionsStatisticService.PlayerStats;
using W3ChampionsStatisticService.PlayerStats.GameLengthForPlayerStatistics;
using W3ChampionsStatisticService.PlayerStats.HeroStats;
using W3ChampionsStatisticService.PlayerStats.RaceOnMapVersusRaceStats;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.ReadModelBase;
using W3ChampionsStatisticService.Rewards.Portraits;
using W3ChampionsStatisticService.Services;
using W3ChampionsStatisticService.WebApi.ExceptionFilters;
using W3ChampionsStatisticService.WebApi.ActionFilters;
using W3ChampionsStatisticService.WebApi.Authorization;
using W3ChampionsStatisticService.W3ChampionsStats;
using W3ChampionsStatisticService.W3ChampionsStats.HeroWinrate;
using W3ChampionsStatisticService.W3ChampionsStats.MmrDistribution;
using W3ChampionsStatisticService.W3ChampionsStats.DistinctPlayersPerDays;
using W3ChampionsStatisticService.W3ChampionsStats.GameLengths;
using W3ChampionsStatisticService.W3ChampionsStats.GamesPerDays;
using W3ChampionsStatisticService.W3ChampionsStats.HeroPlayedStats;
using W3ChampionsStatisticService.W3ChampionsStats.PopularHours;
using W3ChampionsStatisticService.W3ChampionsStats.MapsPerSeasons;
using W3ChampionsStatisticService.W3ChampionsStats.OverallRaceAndWinStats;
using W3ChampionsStatisticService.W3ChampionsStats.MatchupLengths;
using Serilog.Events;
using Serilog.Formatting.Json;
using MongoDB.Driver.Core.Extensions.DiagnosticSources;
using W3ChampionsStatisticService.Extensions;
using W3ChampionsStatisticService.Services.Tracing;

const string WEBSITE_BACKEND_HUB_PATH = "/websiteBackendHub";

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(serverOptions =>
{
    serverOptions.Limits.MaxRequestBodySize = 0x8000000; // 128 MiB
});

builder.Services.AddControllers(c =>
{
    c.Filters.Add<ValidationExceptionFilter>();
    c.Filters.Add<HttpRequestExceptionFilter>();
});

// Create logs with format website-backend_yyyyMMdd.log
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .MinimumLevel.Override("AspNetCore.Authentication.Basic.BasicHandler", LogEventLevel.Warning) // Temporarily filter out the Basic auth schema log. We should add central JWT though.
    .MinimumLevel.Override("System.Net.Http.HttpClient", LogEventLevel.Warning) // Filter out verbose HTTP client logs
    .MinimumLevel.Override("System.Net.Http", LogEventLevel.Warning) // Filter out verbose System.Net.Http logs
    .WriteTo.Console(new JsonFormatter(renderMessage: true), restrictedToMinimumLevel: LogEventLevel.Information) // Write to Console to allow log scraping
    .WriteTo.File("Logs/website-backend_.log", rollingInterval: RollingInterval.Day)
    .CreateLogger();
// Tell the AspNetCore host to use Serilog for all logging
builder.Host.UseSerilog();

Log.Information("Starting server.");

// Add telemetry
string appInsightsKey = Environment.GetEnvironmentVariable("APP_INSIGHTS");
builder.Services.AddApplicationInsightsTelemetry(c => c.ConnectionString = "InstrumentationKey=" + appInsightsKey?.Replace("'", ""));

// Add Swagger
builder.Services.AddSwaggerGen(f =>
{
    f.SwaggerDoc("v1", new OpenApiInfo { Title = "w3champions", Version = "v1" });
});

// Configure and add MongoDB
string mongoConnectionString = Environment.GetEnvironmentVariable("MONGO_CONNECTION_STRING") ?? "mongodb://157.90.1.251:3513"; // "mongodb://localhost:27017";
MongoClientSettings mongoSettings = MongoClientSettings.FromConnectionString(mongoConnectionString.Replace("'", ""));
mongoSettings.ServerSelectionTimeout = TimeSpan.FromSeconds(5);
mongoSettings.ConnectTimeout = TimeSpan.FromSeconds(5);

builder.Services.AddW3CTracing(WEBSITE_BACKEND_HUB_PATH, mongoSettings);

var mongoClient = new MongoClient(mongoSettings);
builder.Services.AddSingleton(mongoClient);


// Add SignalR for using websockets
builder.Services.AddSignalR();

builder.Services.AddHttpContextAccessor();

builder.Services.AddHttpClient();

builder.Services.AddSpecialBsonRegistrations();

// Add BasicAuth for metrics endpoint
builder.Services.AddBasicAuthForMetrics();

// Suppress default .NET metrics (system_ and microsoft_aspnetcore_ prefixed metrics)
Metrics.SuppressDefaultMetrics(new SuppressDefaultMetricOptions { SuppressProcessMetrics = false, SuppressDebugMetrics = true, SuppressEventCounters = true, SuppressMeters = true });

// Add Application Insights
builder.Services.AddInterceptedSingleton<ITrackingService, TrackingService>();
string disableTelemetry = Environment.GetEnvironmentVariable("DISABLE_TELEMETRY");
if (disableTelemetry == "true")
{
    TelemetryDebugWriter.IsTracingDisabled = true;
}

builder.Services.AddInterceptedTransient<PlayerAkaProvider>();
builder.Services.AddInterceptedTransient<PersonalSettingsProvider>();
builder.Services.AddInterceptedTransient<IMatchmakingProvider, MatchmakingProvider>();

builder.Services.AddMemoryCache();
builder.Services.AddTransient(typeof(ICachedDataProvider<>), typeof(InMemoryCachedDataProvider<>));
builder.Services.Configure<CacheOptionsFor<SeasonMapInformation>>(x =>
{
    x.CacheDuration = TimeSpan.FromHours(1);
});

builder.Services.Configure<CacheOptionsFor<List<PlayerAka>>>(x =>
{
    x.CacheDuration = TimeSpan.FromHours(1);
});

builder.Services.AddInterceptedTransient<IMatchEventRepository, MatchEventRepository>();
builder.Services.AddInterceptedTransient<IVersionRepository, VersionRepository>();
builder.Services.AddInterceptedTransient<IMatchRepository, MatchRepository>();
builder.Services.AddInterceptedSingleton<IPlayerRepository, PlayerRepository>();
builder.Services.AddInterceptedTransient<IRankRepository, RankRepository>();
builder.Services.AddInterceptedTransient<IPlayerStatsRepository, PlayerStatsRepository>();
builder.Services.AddInterceptedTransient<IW3StatsRepo, W3StatsRepo>();
builder.Services.AddInterceptedTransient<IPatchRepository, PatchRepository>();
builder.Services.AddInterceptedTransient<IAdminRepository, AdminRepository>();
builder.Services.AddInterceptedTransient<IPersonalSettingsRepository, PersonalSettingsRepository>();
builder.Services.AddInterceptedTransient<IW3CAuthenticationService, W3CAuthenticationService>();
builder.Services.AddInterceptedSingleton<IOngoingMatchesCache, OngoingMatchesCache>();
builder.Services.AddInterceptedTransient<HeroStatsQueryHandler>();
builder.Services.AddInterceptedTransient<PortraitCommandHandler>();
builder.Services.AddInterceptedTransient<MmrDistributionHandler>();
builder.Services.AddInterceptedTransient<RankQueryHandler>();
builder.Services.AddInterceptedTransient<GameModeStatQueryHandler>();
builder.Services.AddInterceptedTransient<IClanRepository, ClanRepository>();
builder.Services.AddInterceptedTransient<INewsRepository, NewsRepository>();
builder.Services.AddInterceptedTransient<IPortraitRepository, PortraitRepository>();
builder.Services.AddInterceptedTransient<IInformationMessagesRepository, InformationMessagesRepository>();
builder.Services.AddInterceptedTransient<ClanCommandHandler>();

// Actionfilters
builder.Services.AddInterceptedTransient<BearerCheckIfBattleTagBelongsToAuthFilter>();
builder.Services.AddInterceptedTransient<CheckIfBattleTagIsAdminFilter>();
builder.Services.AddInterceptedTransient<InjectActingPlayerFromAuthCodeFilter>();
builder.Services.AddInterceptedTransient<BearerHasPermissionFilter>();
builder.Services.AddInterceptedTransient<InjectAuthTokenFilter>();

builder.Services.AddInterceptedSingleton<MatchmakingServiceClient>();
builder.Services.AddInterceptedSingleton<UpdateServiceClient>();
builder.Services.AddInterceptedSingleton<ReplayServiceClient>();
builder.Services.AddInterceptedTransient<MatchQueryHandler>();
builder.Services.AddInterceptedSingleton<ChatServiceClient>();
builder.Services.AddInterceptedTransient<PlayerStatisticsService>();
builder.Services.AddInterceptedTransient<PlayerService>();
builder.Services.AddInterceptedTransient<MatchService>();
builder.Services.AddInterceptedTransient<IdentityServiceClient>();
builder.Services.AddInterceptedTransient<ILogsRepository, LogsRepository>();

// Friends
builder.Services.AddInterceptedTransient<IFriendRepository, FriendRepository>();
builder.Services.AddInterceptedTransient<IFriendCommandHandler, FriendCommandHandler>();
builder.Services.AddInterceptedSingleton<IFriendRequestCache, FriendRequestCache>();
builder.Services.AddInterceptedSingleton<FriendListCache>();
builder.Services.AddInterceptedTransient<FriendRepository>();

// Websocket services
builder.Services.AddInterceptedSingleton<ConnectionMapping>();

builder.Services.AddDirectoryBrowser();

string startHandlers = Environment.GetEnvironmentVariable("START_HANDLERS");

if (startHandlers == "true")
{
    // PlayerProfile
    builder.Services.AddMatchFinishedReadModelService<PlayerOverallStatsHandler>();
    builder.Services.AddMatchFinishedReadModelService<PlayOverviewHandler>();
    builder.Services.AddMatchFinishedReadModelService<PlayerWinrateHandler>();

    // PlayerStats
    builder.Services.AddMatchFinishedReadModelService<PlayerRaceOnMapVersusRaceRatioHandler>();
    builder.Services.AddMatchFinishedReadModelService<PlayerHeroStatsHandler>();
    builder.Services.AddMatchFinishedReadModelService<PlayerGameModeStatPerGatewayHandler>();
    builder.Services.AddMatchFinishedReadModelService<PlayerRaceStatPerGatewayHandler>();
    builder.Services.AddMatchFinishedReadModelService<PlayerMmrRpTimelineHandler>();
    builder.Services.AddMatchFinishedReadModelService<GameLengthForPlayerStatisticsHandler>();

    // General Stats
    builder.Services.AddMatchFinishedReadModelService<GamesPerDayHandler>();
    builder.Services.AddMatchFinishedReadModelService<GameLengthStatHandler>();
    builder.Services.AddMatchFinishedReadModelService<MatchupLengthsHandler>();
    builder.Services.AddMatchFinishedReadModelService<DistinctPlayersPerDayHandler>();
    builder.Services.AddMatchFinishedReadModelService<PopularHoursStatHandler>();
    builder.Services.AddMatchFinishedReadModelService<HeroPlayedStatHandler>();
    builder.Services.AddMatchFinishedReadModelService<MapsPerSeasonHandler>();

    // Game Balance Stats
    builder.Services.AddMatchFinishedReadModelService<OverallRaceAndWinStatHandler>();
    builder.Services.AddMatchFinishedReadModelService<OverallHeroWinRatePerHeroModelHandler>();

    // Ladder Syncs
    builder.Services.AddMatchFinishedReadModelService<MatchReadModelHandler>();

    // On going matches
    builder.Services.AddUnversionedReadModelService<StartedMatchIntoOngoingMatchesHandler>();
    builder.Services.AddMatchCanceledReadModelService<OngoingRemovalMatchCanceledHandler>();

    builder.Services.AddUnversionedReadModelService<RankSyncHandler>();
    builder.Services.AddUnversionedReadModelService<LeagueSyncHandler>();
}

var runBackfill = System.Environment.GetEnvironmentVariable("RUN_BACKFILL");

if (runBackfill == "true")
{
    // Not a read model service but uses the same functionality to do async background processing to backfill data.
    builder.Services.AddUnversionedReadModelService<MatchupHeroBackfillService>();
}

var app = builder.Build();

app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
    KnownNetworks = { new IPNetwork(IPAddress.Parse("172.18.0.0"), 16) } // Docker network
});
app.Use(
    (context, next) =>
    {
        // Sets header for api/heroes/filter cache response Vary header
        context.Response.Headers["HeroFilterVersion"] = HeroFilter.AllowedHeroTypes.GetHashCode().ToString();
        return next.Invoke();
    }
);

app.UseRouting();

// Add authentication and authorization middleware
app.UseAuthentication();
app.UseAuthorization();

// Collect metrics for Prometheus
app.UseHttpMetrics();
app.MapMetrics().RequireAuthorization(BasicAuthConfiguration.ReadMetricsPolicy);

app.UseCors(builder =>
    builder
        .AllowAnyHeader()
        .AllowAnyMethod()
        .SetIsOriginAllowed(_ => true)
        .AllowCredentials());

app.MapControllers();

// Use Swagger
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "w3champions");
});

// Configure log path
var fileProvider = new PhysicalFileProvider(System.IO.Path.Combine(app.Environment.ContentRootPath, "Logs"));
var requestPath = "/logs";

// Allow serving files with .log extension
var contentTypeProvider = new FileExtensionContentTypeProvider();
contentTypeProvider.Mappings[".log"] = "text/plain";

// Serve files in the logs directory
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = fileProvider,
    ContentTypeProvider = contentTypeProvider,
    RequestPath = requestPath
});

// Allow browsing the logs directory
app.UseDirectoryBrowser(new DirectoryBrowserOptions
{
    FileProvider = fileProvider,
    RequestPath = requestPath
});

// Add SignalR FriendHub
app.MapHub<WebsiteBackendHub>(WEBSITE_BACKEND_HUB_PATH);

Log.Information("Server started.");
app.Run();
