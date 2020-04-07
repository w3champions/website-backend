using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using W3ChampionsStatisticService.Ladder;
using W3ChampionsStatisticService.Matches;
using W3ChampionsStatisticService.MatchEvents;
using W3ChampionsStatisticService.PlayerProfiles;
using W3ChampionsStatisticService.PlayerStats;
using W3ChampionsStatisticService.PlayerStats.RaceOnMapStats;
using W3ChampionsStatisticService.PlayerStats.RaceOnMapVersusRaceStats;
using W3ChampionsStatisticService.PlayerStats.RaceVersusRaceStats;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.ReadModelBase;
using W3ChampionsStatisticService.Services;
using W3ChampionsStatisticService.W3ChampionsStats;
using W3ChampionsStatisticService.W3ChampionsStats.DistinctPlayersPerDays;
using W3ChampionsStatisticService.W3ChampionsStats.GameLengths;
using W3ChampionsStatisticService.W3ChampionsStats.GamesPerDays;
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

            var mongoConnectionString = _configuration.GetValue<string>("mongoConnectionString") ?? "mongodb://176.28.16.249:3513";
            services.AddSingleton(new DbConnctionInfo(mongoConnectionString.Replace("'", "")));
            
            services.AddSingleton(typeof(TrackingService));
            
            services.AddTransient<IMatchEventRepository, MatchEventRepository>();
            services.AddTransient<IVersionRepository, VersionRepository>();
            services.AddTransient<IMatchRepository, MatchRepository>();
            services.AddTransient<IPlayerRepository, PlayerRepository>();
            services.AddTransient<IPlayerStatsRepository, PlayerStatsRepository>();
            services.AddTransient<IW3StatsRepo, W3StatsRepo>();

            services.AddTransient<InsertMatchEventsCommandHandler>();

            services.AddReadModelService<MatchReadModelHandler>();

            services.AddReadModelService<PlayerModelHandler>();
            services.AddReadModelService<PlayOverviewHandler>();

            services.AddReadModelService<RaceOnMapRatioHandler>();
            services.AddReadModelService<RaceOnMapVersusRaceRatioHandler>();
            services.AddReadModelService<RaceVersusRaceRatioHandler>();
            services.AddReadModelService<Wc3StatsModelHandler>();
            services.AddReadModelService<GamesPerDayModelHandler>();
            services.AddReadModelService<GameLengthsModelHandler>();
            services.AddReadModelService<DistinctPlayersPerDayHandler>();
            services.AddReadModelService<PlayerWinrateHandler>();
        }

        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
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
            services.AddSingleton<IHostedService, ReadModelService<T>>();
            return services;
        }
    }
}