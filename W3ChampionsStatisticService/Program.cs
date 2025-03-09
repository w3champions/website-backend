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

using W3C.Domain.ChatService;
using W3C.Domain.CommonValueObjects;
using W3C.Domain.MatchmakingService;
using W3C.Domain.Repositories;
using W3C.Domain.UpdateService;

using W3ChampionsStatisticService.Admin;
using W3ChampionsStatisticService.Admin.Logs;
using W3ChampionsStatisticService.Admin.Permissions;
using W3ChampionsStatisticService.Cache;
using W3ChampionsStatisticService.Clans;
using W3ChampionsStatisticService.Friends;
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

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(serverOptions =>
{
    serverOptions.Limits.MaxRequestBodySize = 0x8000000; // 128 MiB
});

builder.Services.AddControllers(c =>
{
    c.Filters.Add<ValidationExceptionFilter>();
});

// Create logs with format website-backend_yyyyMMdd.log
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .WriteTo.File("Logs/website-backend_.log", rollingInterval: RollingInterval.Day)
    .CreateLogger();

Log.Information("Starting server.");

// Add telemetry
string appInsightsKey = Environment.GetEnvironmentVariable("APP_INSIGHTS");
builder.Services.AddApplicationInsightsTelemetry(c => c.InstrumentationKey = appInsightsKey?.Replace("'", ""));

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
var mongoClient = new MongoClient(mongoSettings);
builder.Services.AddSingleton(mongoClient);

// Add SignalR for using websockets
builder.Services.AddSignalR();
builder.Services.AddHttpContextAccessor();

builder.Services.AddHttpClient();

builder.Services.AddSpecialBsonRegistrations();

// Add Application Insights
builder.Services.AddSingleton<TrackingService>();
string disableTelemetry = Environment.GetEnvironmentVariable("DISABLE_TELEMETRY");
if (disableTelemetry == "true")
{
    TelemetryDebugWriter.IsTracingDisabled = true;
}

builder.Services.AddTransient<PlayerAkaProvider>();
builder.Services.AddTransient<PersonalSettingsProvider>();
builder.Services.AddTransient<MatchmakingProvider>();

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

builder.Services.AddTransient<IMatchEventRepository, MatchEventRepository>();
builder.Services.AddTransient<IVersionRepository, VersionRepository>();
builder.Services.AddTransient<IMatchRepository, MatchRepository>();
builder.Services.AddSingleton<IPlayerRepository, PlayerRepository>();
builder.Services.AddTransient<IRankRepository, RankRepository>();
builder.Services.AddTransient<IPlayerStatsRepository, PlayerStatsRepository>();
builder.Services.AddTransient<IW3StatsRepo, W3StatsRepo>();
builder.Services.AddTransient<IPatchRepository, PatchRepository>();
builder.Services.AddTransient<IAdminRepository, AdminRepository>();
builder.Services.AddTransient<IPersonalSettingsRepository, PersonalSettingsRepository>();
builder.Services.AddTransient<IW3CAuthenticationService, W3CAuthenticationService>();
builder.Services.AddSingleton<IOngoingMatchesCache, OngoingMatchesCache>();
builder.Services.AddTransient<HeroStatsQueryHandler>();
builder.Services.AddTransient<PortraitCommandHandler>();
builder.Services.AddTransient<MmrDistributionHandler>();
builder.Services.AddTransient<RankQueryHandler>();
builder.Services.AddTransient<GameModeStatQueryHandler>();
builder.Services.AddTransient<IClanRepository, ClanRepository>();
builder.Services.AddTransient<INewsRepository, NewsRepository>();
builder.Services.AddTransient<IPortraitRepository, PortraitRepository>();
builder.Services.AddTransient<IInformationMessagesRepository, InformationMessagesRepository>();
builder.Services.AddTransient<ClanCommandHandler>();

// Actionfilters
builder.Services.AddTransient<BearerCheckIfBattleTagBelongsToAuthFilter>();
builder.Services.AddTransient<CheckIfBattleTagIsAdminFilter>();
builder.Services.AddTransient<InjectActingPlayerFromAuthCodeFilter>();
builder.Services.AddTransient<BearerHasPermissionFilter>();
builder.Services.AddTransient<InjectAuthTokenFilter>();

builder.Services.AddSingleton<MatchmakingServiceClient>();
builder.Services.AddSingleton<UpdateServiceClient>();
builder.Services.AddSingleton<ReplayServiceClient>();
builder.Services.AddTransient<MatchQueryHandler>();
builder.Services.AddSingleton<ChatServiceClient>();
builder.Services.AddTransient<IFriendRepository, FriendRepository>();
builder.Services.AddTransient<PlayerStatisticsService>();
builder.Services.AddTransient<PlayerService>();
builder.Services.AddTransient<MatchService>();
builder.Services.AddTransient<IPermissionsRepository, PermissionsRepository>();
builder.Services.AddTransient<ILogsRepository, LogsRepository>();

// Websocket services
builder.Services.AddSingleton<ConnectionMapping>();
builder.Services.AddSingleton<FriendRequestCache, FriendRequestCache>();
builder.Services.AddTransient<FriendRepository>();

builder.Services.AddDirectoryBrowser();

string startHandlers = Environment.GetEnvironmentVariable("START_HANDLERS");

if (startHandlers == "true")
{
    // PlayerProfile
    builder.Services.AddReadModelService<PlayerOverallStatsHandler>();
    builder.Services.AddReadModelService<PlayOverviewHandler>();
    builder.Services.AddReadModelService<PlayerWinrateHandler>();

    // PlayerStats
    builder.Services.AddReadModelService<PlayerRaceOnMapVersusRaceRatioHandler>();
    builder.Services.AddReadModelService<PlayerHeroStatsHandler>();
    builder.Services.AddReadModelService<PlayerGameModeStatPerGatewayHandler>();
    builder.Services.AddReadModelService<PlayerRaceStatPerGatewayHandler>();
    builder.Services.AddReadModelService<PlayerMmrRpTimelineHandler>();
    builder.Services.AddReadModelService<GameLengthForPlayerStatisticsHandler>();

    // General Stats
    builder.Services.AddReadModelService<GamesPerDayHandler>();
    builder.Services.AddReadModelService<GameLengthStatHandler>();
    builder.Services.AddReadModelService<MatchupLengthsHandler>();
    builder.Services.AddReadModelService<DistinctPlayersPerDayHandler>();
    builder.Services.AddReadModelService<PopularHoursStatHandler>();
    builder.Services.AddReadModelService<HeroPlayedStatHandler>();
    builder.Services.AddReadModelService<MapsPerSeasonHandler>();

    // Game Balance Stats
    builder.Services.AddReadModelService<OverallRaceAndWinStatHandler>();
    builder.Services.AddReadModelService<OverallHeroWinRatePerHeroModelHandler>();

    // Ladder Syncs
    builder.Services.AddReadModelService<MatchReadModelHandler>();

    // On going matches
    builder.Services.AddUnversionedReadModelService<OngoingMatchesHandler>();

    builder.Services.AddUnversionedReadModelService<RankSyncHandler>();
    builder.Services.AddUnversionedReadModelService<LeagueSyncHandler>();
}

var app = builder.Build();

app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});

app.UseRouting();

// Collect metrics for Prometheus
app.UseHttpMetrics();
app.MapMetrics();

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
app.MapHub<WebsiteBackendHub>("/websiteBackendHub");

app.Run();
