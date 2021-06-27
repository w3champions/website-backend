using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.OpenApi.Models;
using MongoDB.Driver;
using W3ChampionsStatisticService.Achievements;
using W3ChampionsStatisticService.Admin;
using W3ChampionsStatisticService.Clans;
using W3ChampionsStatisticService.CommonValueObjects;
using W3ChampionsStatisticService.Ladder;
using W3ChampionsStatisticService.Matches;
using W3ChampionsStatisticService.PadEvents;
using W3ChampionsStatisticService.PersonalSettings;
using W3ChampionsStatisticService.PlayerProfiles;
using W3ChampionsStatisticService.PlayerProfiles.GameModeStats;
using W3ChampionsStatisticService.PlayerProfiles.MmrRankingStats;
using W3ChampionsStatisticService.PlayerProfiles.RaceStats;
using W3ChampionsStatisticService.PlayerStats;
using W3ChampionsStatisticService.PlayerStats.HeroStats;
using W3ChampionsStatisticService.PlayerStats.RaceOnMapVersusRaceStats;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.ReadModelBase;
using W3ChampionsStatisticService.Services;
using W3ChampionsStatisticService.Tournaments;
using W3ChampionsStatisticService.W3ChampionsStats;
using W3ChampionsStatisticService.W3ChampionsStats.DistinctPlayersPerDays;
using W3ChampionsStatisticService.W3ChampionsStats.GameLengths;
using W3ChampionsStatisticService.W3ChampionsStats.GamesPerDays;
using W3ChampionsStatisticService.W3ChampionsStats.HeroPlayedStats;
using W3ChampionsStatisticService.W3ChampionsStats.HeroWinrate;
using W3ChampionsStatisticService.W3ChampionsStats.HourOfPlay;
using W3ChampionsStatisticService.W3ChampionsStats.MapsPerSeasons;
using W3ChampionsStatisticService.W3ChampionsStats.MmrDistribution;
using W3ChampionsStatisticService.W3ChampionsStats.OverallRaceAndWinStats;
using W3ChampionsStatisticService.WebApi.ActionFilters;
using W3ChampionsStatisticService.WebApi.ExceptionFilters;

namespace W3ChampionsStatisticService
{
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
            var mongoConnectionString = Environment.GetEnvironmentVariable("MONGO_CONNECTION_STRING")  ?? "mongodb://157.90.1.251:3513"; // "mongodb://localhost:27018";
            var mongoClient = new MongoClient(mongoConnectionString.Replace("'", ""));
            services.AddSingleton(mongoClient);

            services.AddSignalR();

            services.AddSpecialBsonRegistrations();

            services.AddSingleton<TrackingService>();
            services.AddSingleton<PlayerAkaProvider>();
            services.AddSingleton<PersonalSettingsProvider>();

            services.AddTransient<IAchievementRepository, AchievementRepository>();
            services.AddTransient<IMatchEventRepository, MatchEventRepository>();
            services.AddTransient<IVersionRepository, VersionRepository>();
            services.AddTransient<IMatchRepository, MatchRepository>();
            services.AddTransient<TournamentsRepository, TournamentsRepository>();
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
            services.AddTransient<PersonalSettingsCommandHandler>();
            services.AddTransient<MmrDistributionHandler>();
            services.AddTransient<RankQueryHandler>();
            services.AddTransient<GameModeStatQueryHandler>();
            services.AddTransient<IClanRepository, ClanRepository>();
            services.AddTransient<INewsRepository, NewsRepository>();
            services.AddTransient<ILoadingScreenTipsRepository, LoadingScreenTipsRepository>();
            services.AddTransient<ClanCommandHandler>();
            services.AddTransient<AchievementRepositoryHandler>();
            services.AddTransient<CheckIfBattleTagBelongsToAuthCodeFilter>();
            services.AddTransient<InjectActingPlayerFromAuthCodeFilter>();
            services.AddTransient<CheckIfBattleTagIsAdminFilter>();
            services.AddTransient<MatchmakingServiceRepo>();
            services.AddTransient<MatchQueryHandler>();

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

                // General Stats
                services.AddReadModelService<GamesPerDayHandler>();
                services.AddReadModelService<GameLengthStatHandler>();
                services.AddReadModelService<DistinctPlayersPerDayHandler>();
                services.AddReadModelService<HourOfPlayStatHandler>();
                services.AddReadModelService<HeroPlayedStatHandler>();
                services.AddReadModelService<MapsPerSeasonHandler>();

                // Game Balance Stats
                services.AddReadModelService<OverallRaceAndWinStatHandler>();
                services.AddReadModelService<OverallHeroWinRatePerHeroModelHandler>();

                // Ladder Syncs
                services.AddReadModelService<MatchReadModelHandler>();

                // On going matches
                services.AddUnversionedReadModelService<OngoingMatchesHandler>();

                // PlayerAchievements
                services.AddReadModelService<AchievementRepositoryHandler>();

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
            app.UseCors(builder =>
                builder
                    .AllowAnyHeader()
                    .AllowAnyMethod()
                    .SetIsOriginAllowed(_ => true)
                    .AllowCredentials());

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
            });
            app.UseSwagger();
            app.UseSwaggerUI(c =>
            {
                c.SwaggerEndpoint("/swagger/v1/swagger.json", "w3champions");
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
}