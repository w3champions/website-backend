using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MongoDB.Driver;
using W3ChampionsStatisticService.Ladder;
using W3ChampionsStatisticService.Matches;
using W3ChampionsStatisticService.PadEvents;
using W3ChampionsStatisticService.PlayerProfiles;
using W3ChampionsStatisticService.PlayerStats;
using W3ChampionsStatisticService.PlayerStats.RaceOnMapVersusRaceStats;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.ReadModelBase;
using W3ChampionsStatisticService.Services;
using W3ChampionsStatisticService.W3ChampionsStats;
using W3ChampionsStatisticService.W3ChampionsStats.DistinctPlayersPerDays;
using W3ChampionsStatisticService.W3ChampionsStats.GameLengths;
using W3ChampionsStatisticService.W3ChampionsStats.GamesPerDays;
using W3ChampionsStatisticService.W3ChampionsStats.HourOfPlay;
using W3ChampionsStatisticService.W3ChampionsStats.RaceAndWinStats;

namespace W3ChampionsStatisticService
{
    public class Startup
    {
        private readonly IConfiguration _configuration;

        public Startup(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public void ConfigureServices(IServiceCollection services)
        {
            var appInsightsKey = _configuration.GetValue<string>("appInsights");
            services.AddApplicationInsightsTelemetry(c => c.InstrumentationKey = appInsightsKey?.Replace("'", ""));

            services.AddControllers();

            var doRunAsyncHandler = _configuration.GetValue<string>("startHandlers");
            var mongoConnectionString = _configuration.GetValue<string>("mongoConnectionString") ?? "mongodb://176.28.16.249:3513";
            var mongoClient = new MongoClient(mongoConnectionString.Replace("'", ""));
            services.AddSingleton(mongoClient);

            services.AddMongoDbSetup(mongoClient);

            services.AddSingleton(typeof(TrackingService));
            
            services.AddTransient<IMatchEventRepository, MatchEventRepository>();
            services.AddTransient<IVersionRepository, VersionRepository>();
            services.AddTransient<IMatchRepository, MatchRepository>();
            services.AddTransient<IPlayerRepository, PlayerRepository>();
            services.AddTransient<IRankRepository, RankRepository>();
            services.AddTransient<IPlayerStatsRepository, PlayerStatsRepository>();
            services.AddTransient<IW3StatsRepo, W3StatsRepo>();

            services.AddTransient<InsertMatchEventsCommandHandler>();

            if (doRunAsyncHandler == "true")
            {
                services.AddReadModelService<MatchReadModelHandler>();

                services.AddReadModelService<PlayerModelHandler>();
                services.AddReadModelService<PlayOverviewHandler>();

                services.AddReadModelService<RaceOnMapVersusRaceRatioHandler>();
                services.AddReadModelService<Wc3StatsModelHandler>();
                services.AddReadModelService<GamesPerDayModelHandler>();
                services.AddReadModelService<GameLengthsModelHandler>();
                services.AddReadModelService<DistinctPlayersPerDayHandler>();
                services.AddReadModelService<PlayerWinrateHandler>();
                services.AddReadModelService<HourOfPlayModelHandler>();

                services.AddUnversionesReadModelService<RankHandler>();
            }
        }

        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            // without that, nginx forwarding in docker wont work
            app.UseForwardedHeaders(new ForwardedHeadersOptions
            {
                ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
            });

            app.UseRouting();

            app.UseCors(o => o
                .AllowAnyOrigin()
                .AllowAnyHeader()
                .AllowAnyMethod());
            app.UseEndpoints(endpoints => { endpoints.MapControllers(); });
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

        public static IServiceCollection AddMongoDbSetup(
            this IServiceCollection services,
            IMongoClient mongoClient)
        {
            var db = mongoClient.GetDatabase("W3Champions-Statistic-Service");
            db.GetCollection<MatchFinishedEvent>(nameof(MatchFinishedEvent))
                .Indexes.CreateOne(CreateIndexModel<MatchFinishedEvent>());
            db.GetCollection<MatchStartedEvent>(nameof(MatchStartedEvent))
                .Indexes.CreateOne(CreateIndexModel<MatchStartedEvent>());
            return services;
        }

        private static CreateIndexModel<T> CreateIndexModel<T>()
        {
            var keys = Builders<T>.IndexKeys.Ascending("match.id");
            var indexOptions = new CreateIndexOptions {Unique = true};
            var model = new CreateIndexModel<T>(keys, indexOptions);
            return model;
        }

        public static IServiceCollection AddUnversionesReadModelService<T>(this IServiceCollection services) where T : class, IAsyncUpdatable
        {
            services.AddTransient<T>();
            services.AddSingleton<IHostedService, AsyncServiceBase<T>>();
            return services;
        }
    }
}