using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.OpenApi.Models;
using MongoDB.Driver;
using Prometheus;
using System;
using System.Collections.Generic;
using W3C.Domain.ChatService;
using W3C.Domain.CommonValueObjects;
using W3C.Domain.MatchmakingService;
using W3C.Domain.Repositories;
using W3C.Domain.UpdateService;
using W3ChampionsStatisticService.Admin;
using W3ChampionsStatisticService.Cache;
using W3ChampionsStatisticService.Clans;
using W3ChampionsStatisticService.Friends;
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
using W3ChampionsStatisticService.W3ChampionsStats;
using W3ChampionsStatisticService.W3ChampionsStats.DistinctPlayersPerDays;
using W3ChampionsStatisticService.W3ChampionsStats.GameLengths;
using W3ChampionsStatisticService.W3ChampionsStats.GamesPerDays;
using W3ChampionsStatisticService.W3ChampionsStats.HeroPlayedStats;
using W3ChampionsStatisticService.W3ChampionsStats.HeroWinrate;
using W3ChampionsStatisticService.W3ChampionsStats.PopularHours;
using W3ChampionsStatisticService.W3ChampionsStats.MapsPerSeasons;
using W3ChampionsStatisticService.W3ChampionsStats.MmrDistribution;
using W3ChampionsStatisticService.W3ChampionsStats.OverallRaceAndWinStats;
using W3ChampionsStatisticService.WebApi.ActionFilters;
using W3ChampionsStatisticService.WebApi.ExceptionFilters;
using Microsoft.Extensions.FileProviders;
using Microsoft.AspNetCore.StaticFiles;
using W3ChampionsStatisticService.Admin.Permissions;
using W3ChampionsStatisticService.Admin.Logs;
using W3ChampionsStatisticService.W3ChampionsStats.MatchupLengths;

namespace W3ChampionsStatisticService;

public class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        var appInsightsKey = Environment.GetEnvironmentVariable("APP_INSIGHTS");
        services.AddApplicationInsightsTelemetry(c => c.InstrumentationKey = appInsightsKey?.Replace("'", ""));

        services.AddControllers(c =>
        {
            c.Filters.Add<ValidationExceptionFilter>();
        });

        services.AddSwaggerGen(f => {
            f.SwaggerDoc("v1", new OpenApiInfo { Title = "w3champions", Version = "v1"});
        });

        var startHandlers = Environment.GetEnvironmentVariable("START_HANDLERS");
        var mongoConnectionString = Environment.GetEnvironmentVariable("MONGO_CONNECTION_STRING")  ?? "mongodb://157.90.1.251:3513"; // "mongodb://localhost:27017";

        var mongoSettings = MongoClientSettings.FromConnectionString(mongoConnectionString.Replace("'", ""));
        mongoSettings.ServerSelectionTimeout = TimeSpan.FromSeconds(5);
        mongoSettings.ConnectTimeout = TimeSpan.FromSeconds(5);

        var mongoClient = new MongoClient(mongoSettings);
        services.AddSingleton(mongoClient);

        services.AddSignalR();
        services.AddHttpClient();

        services.AddSpecialBsonRegistrations();

        services.AddSingleton<TrackingService>();
        services.AddTransient<PlayerAkaProvider>();
        services.AddTransient<PersonalSettingsProvider>();
        services.AddTransient<MatchmakingProvider>();

        services.AddMemoryCache();
        services.AddTransient(typeof(ICachedDataProvider<>), typeof(InMemoryCachedDataProvider<>));
        services.Configure<CacheOptionsFor<SeasonMapInformation>>(
            x =>
            {
                x.CacheDuration = TimeSpan.FromHours(1);
            });

        services.Configure<CacheOptionsFor<List<PlayerAka>>>(
            x =>
            {
                x.CacheDuration = TimeSpan.FromHours(1);
            });

        services.AddTransient<IMatchEventRepository, MatchEventRepository>();
        services.AddTransient<IVersionRepository, VersionRepository>();
        services.AddTransient<IMatchRepository, MatchRepository>();
        services.AddSingleton<IPlayerRepository, PlayerRepository>();
        services.AddTransient<IRankRepository, RankRepository>();
        services.AddTransient<IPlayerStatsRepository, PlayerStatsRepository>();
        services.AddTransient<IW3StatsRepo, W3StatsRepo>();
        services.AddTransient<IPatchRepository, PatchRepository>();
        services.AddTransient<IAdminRepository, AdminRepository>();
        services.AddTransient<IPersonalSettingsRepository, PersonalSettingsRepository>();
        services.AddTransient<IW3CAuthenticationService, W3CAuthenticationService>();
        services.AddSingleton<IOngoingMatchesCache, OngoingMatchesCache>();
        services.AddTransient<HeroStatsQueryHandler>();
        services.AddTransient<PortraitCommandHandler>();
        services.AddTransient<MmrDistributionHandler>();
        services.AddTransient<RankQueryHandler>();
        services.AddTransient<GameModeStatQueryHandler>();
        services.AddTransient<IClanRepository, ClanRepository>();
        services.AddTransient<INewsRepository, NewsRepository>();
        services.AddTransient<IPortraitRepository, PortraitRepository>();
        services.AddTransient<IInformationMessagesRepository, InformationMessagesRepository>();
        services.AddTransient<ClanCommandHandler>();

        // Actionfilters
        services.AddTransient<BearerCheckIfBattleTagBelongsToAuthFilter>();
        services.AddTransient<CheckIfBattleTagIsAdminFilter>();
        services.AddTransient<InjectActingPlayerFromAuthCodeFilter>();
        services.AddTransient<BearerHasPermissionFilter>();
        services.AddTransient<InjectAuthTokenFilter>();

        services.AddSingleton<MatchmakingServiceClient>();
        services.AddSingleton<UpdateServiceClient>();
        services.AddSingleton<ReplayServiceClient>();
        services.AddTransient<MatchQueryHandler>();
        services.AddSingleton<ChatServiceClient>();
        services.AddTransient<IFriendRepository, FriendRepository>();
        services.AddTransient<PlayerStatisticsService>();
        services.AddTransient<PlayerService>();
        services.AddTransient<W3StatsService>();
        services.AddTransient<IPermissionsRepository, PermissionsRepository>();
        services.AddTransient<ILogsRepository, LogsRepository>();

        services.AddDirectoryBrowser();

        if (startHandlers == "true")
        {
            // PlayerProfile
            services.AddReadModelService<PlayerOverallStatsHandler>();
            services.AddReadModelService<PlayOverviewHandler>();
            services.AddReadModelService<PlayerWinrateHandler>();

            // PlayerStats
            services.AddReadModelService<PlayerRaceOnMapVersusRaceRatioHandler>();
            services.AddReadModelService<PlayerHeroStatsHandler>();
            services.AddReadModelService<PlayerGameModeStatPerGatewayHandler>();
            services.AddReadModelService<PlayerRaceStatPerGatewayHandler>();
            services.AddReadModelService<PlayerMmrRpTimelineHandler>();
            services.AddReadModelService<GameLengthForPlayerStatisticsHandler>();

            // General Stats
            services.AddReadModelService<GamesPerDayHandler>();
            services.AddReadModelService<GameLengthStatHandler>();
            services.AddReadModelService<MatchupLengthsHandler>();
            services.AddReadModelService<DistinctPlayersPerDayHandler>();
            services.AddReadModelService<PopularHoursStatHandler>();
            services.AddReadModelService<HeroPlayedStatHandler>();
            services.AddReadModelService<MapsPerSeasonHandler>();

            // Game Balance Stats
            services.AddReadModelService<OverallRaceAndWinStatHandler>();
            services.AddReadModelService<OverallHeroWinRatePerHeroModelHandler>();

            // Ladder Syncs
            services.AddReadModelService<MatchReadModelHandler>();

            // On going matches
            services.AddUnversionedReadModelService<OngoingMatchesHandler>();

            services.AddUnversionedReadModelService<RankSyncHandler>();
            services.AddUnversionedReadModelService<LeagueSyncHandler>();
        }
    }

    public void Configure(
        IApplicationBuilder app,
        IWebHostEnvironment env)
    {
        // without that, nginx forwarding in docker wont work
        app.UseForwardedHeaders(new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
        });
        app.UseRouting();
        app.UseHttpMetrics();
        app.UseCors(builder =>
            builder
                .AllowAnyHeader()
                .AllowAnyMethod()
                .SetIsOriginAllowed(_ => true)
                .AllowCredentials());

        app.UseEndpoints(endpoints =>
        {
            endpoints.MapControllers();
            endpoints.MapMetrics();
        });
        app.UseSwagger();
        app.UseSwaggerUI(c =>
        {
            c.SwaggerEndpoint("/swagger/v1/swagger.json", "w3champions");
        });

        // Configure log path
        var fileProvider = new PhysicalFileProvider(System.IO.Path.Combine(env.ContentRootPath, "Logs"));
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
    }
}

public static class ReadModelExtensions
{
    public static IServiceCollection AddReadModelService<T>(this IServiceCollection services) where T : class, IReadModelHandler
    {
        services.AddTransient<T>();
        services.AddTransient<ReadModelHandler<T>>();
        services.AddSingleton<IHostedService, AsyncServiceBase<ReadModelHandler<T>>>();
        return services;
    }

    public static IServiceCollection AddUnversionedReadModelService<T>(this IServiceCollection services) where T : class, IAsyncUpdatable
    {
        services.AddTransient<T>();
        services.AddSingleton<IHostedService, AsyncServiceBase<T>>();
        return services;
    }
}
