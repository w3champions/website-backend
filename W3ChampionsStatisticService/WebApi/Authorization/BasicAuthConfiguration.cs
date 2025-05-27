using AspNetCore.Authentication.Basic;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading.Tasks;

namespace W3ChampionsStatisticService.WebApi.Authorization;

public static class BasicAuthConfiguration
{
    public const string ReadMetricsPolicy = "ReadMetrics";
    public const string BasicAuthScheme = "BasicAuthentication";

    public static IServiceCollection AddBasicAuthForMetrics(this IServiceCollection services)
    {
        // Get credentials from environment variables or fallback to defaults
        string username = Environment.GetEnvironmentVariable("METRICS_ENDPOINT_AUTH_USERNAME") ?? "admin";
        string password = Environment.GetEnvironmentVariable("METRICS_ENDPOINT_AUTH_PASSWORD") ?? "admin";

        // Add BasicAuth as a named scheme, not the default
        services.AddAuthentication()
            .AddBasic(BasicAuthScheme, options =>
            {
                options.Realm = "W3Champions Metrics";
                options.Events = new BasicEvents
                {
                    OnValidateCredentials = (context) =>
                    {
                        if (context.Username == username && context.Password == password)
                        {
                            var claims = new[] { new System.Security.Claims.Claim("role", "MetricsReader") };
                            context.Principal = new System.Security.Claims.ClaimsPrincipal(
                                new System.Security.Claims.ClaimsIdentity(claims, context.Scheme.Name));
                            context.Success();
                        }
                        else
                        {
                            context.Fail("Invalid username or password");
                        }
                        return Task.CompletedTask;
                    }
                };
            });

        // Add authorization policy for metrics that requires the BasicAuth scheme
        services.AddAuthorization(options =>
        {
            options.AddPolicy(ReadMetricsPolicy, policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.AddAuthenticationSchemes(BasicAuthScheme);
            });
        });

        return services;
    }
}
