using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using W3ChampionsStatisticService.Matches;
using W3ChampionsStatisticService.MatchEvents;
using W3ChampionsStatisticService.MongoDb;
using W3ChampionsStatisticService.Ports;

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
            services.AddControllers();
            var mongoConnectionString = _configuration.GetValue<string>("mongoConnectionString");

            services.AddSingleton(new DbConnctionInfo(mongoConnectionString?.Replace("'", "")));

            services.AddTransient<IMatchEventRepository, MatchEventRepository>();
            services.AddTransient<IVersionRepository, VersionRepository>();
            services.AddTransient<IMatchRepository, MatchRepository>();

            services.AddTransient<InsertMatchEventsCommandHandler>();
            services.AddTransient<InsertMatchCommandHandler>();
        }

        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseHttpsRedirection();
            app.UseRouting();
            app.UseEndpoints(endpoints => { endpoints.MapControllers(); });
        }
    }
}