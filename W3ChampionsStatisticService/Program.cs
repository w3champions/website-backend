using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Prometheus;
using Serilog;

namespace W3ChampionsStatisticService;

public class Program
{
    public static void Main(string[] args)
    {
        // Create logs with format website-backend_yyyyMMdd.log
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.File("Logs/website-backend_.log", rollingInterval: RollingInterval.Day)
            .CreateLogger();

        Log.Information("Starting server.");

        CreateHostBuilder(args).Build().Run();
    }

    public static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .ConfigureWebHostDefaults(webBuilder =>
            {
                webBuilder.ConfigureKestrel(serverOptions =>
                {
                    serverOptions.Limits.MaxRequestBodySize = 100_000_000;
                })
                .UseStartup<Startup>();
            });
}
