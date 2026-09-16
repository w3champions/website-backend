using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using W3ChampionsStatisticService.WebApi.ExceptionFilters;

namespace WC3ChampionsStatisticService.Tests.WebApi;

/// <summary>
/// A real Kestrel on a loopback port in front of the MVC pipeline as Program.cs configures it — the same two global
/// exception filters in the same order — serving only the controllers a test names, so a round trip exercises the
/// framework behaviour unit tests bypass (the binder, Kestrel's own request rejections, result execution) without
/// pulling every production controller and its dependencies into the container. Dispose stops the host.
/// </summary>
internal sealed class LoopbackMvcHost : IAsyncDisposable
{
    private readonly WebApplication _app;

    private LoopbackMvcHost(WebApplication app)
    {
        _app = app;
        BaseAddress = new Uri(app.Urls.Single());
        Client = new HttpClient { BaseAddress = BaseAddress };
    }

    public Uri BaseAddress { get; }

    /// <summary>A plain client for the host; tests needing other handler settings build their own on <see cref="BaseAddress"/>.</summary>
    public HttpClient Client { get; }

    public IServiceProvider Services => _app.Services;

    /// <summary>
    /// Starts the host with <paramref name="controllers"/> as the only discoverable controllers and whatever
    /// <paramref name="configureServices"/> registers on top of MVC (nothing else from Program.cs is registered).
    /// </summary>
    public static async Task<LoopbackMvcHost> StartAsync(Action<IServiceCollection> configureServices, params Type[] controllers)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddControllers(c =>
            {
                c.Filters.Add<ValidationExceptionFilter>();
                c.Filters.Add<HttpRequestExceptionFilter>();
            })
            .ConfigureApplicationPartManager(manager =>
            {
                manager.ApplicationParts.Clear();
                foreach (var assembly in controllers.Select(t => t.Assembly).Distinct())
                {
                    manager.ApplicationParts.Add(new AssemblyPart(assembly));
                }

                manager.FeatureProviders.Add(new OnlyTheseControllers(controllers));
            });
        configureServices(builder.Services);
        var app = builder.Build();
        app.MapControllers();
        await app.StartAsync();
        return new LoopbackMvcHost(app);
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    /// <summary>Runs after MVC's own provider has listed every controller of the parts and keeps only the named ones.</summary>
    private sealed class OnlyTheseControllers(IEnumerable<Type> keep) : IApplicationFeatureProvider<ControllerFeature>
    {
        private readonly HashSet<Type> _keep = keep.ToHashSet();

        public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
        {
            foreach (var controller in feature.Controllers.Where(c => !_keep.Contains(c.AsType())).ToList())
            {
                feature.Controllers.Remove(controller);
            }
        }
    }
}
