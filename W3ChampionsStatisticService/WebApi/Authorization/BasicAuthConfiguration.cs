using AspNetCore.Authentication.Basic;
using Microsoft.Extensions.DependencyInjection;
using System;

namespace W3ChampionsStatisticService.WebApi.Authorization
{
    public static class BasicAuthConfiguration
    {
        public const string ReadMetricsPolicy = "ReadMetrics";

        public static IServiceCollection AddBasicAuthForMetrics(this IServiceCollection services)
        {
            // Get credentials from environment variables or fallback to defaults
            string username = Environment.GetEnvironmentVariable("METRICS_ENDPOINT_AUTH_USERNAME") ?? "admin";
            string password = Environment.GetEnvironmentVariable("METRICS_ENDPOINT_AUTH_PASSWORD") ?? "admin";

            services.AddAuthentication(BasicDefaults.AuthenticationScheme)
                .AddBasic(options =>
                {
                    options.Realm = "W3Champions Metrics";
                    options.Events = new BasicEvents
                    {
                        OnValidateCredentials = async (context) =>
                        {
                            if (context.Username == username && context.Password == password)
                            {
                                var claims = new[] { new System.Security.Claims.Claim("role", "MetricsReader") };
                                context.Principal = new System.Security.Claims.ClaimsPrincipal(
                                    new System.Security.Claims.ClaimsIdentity(claims, context.Scheme.Name));
                                context.Success();
                            }
                        }
                    };
                });

            // Add authorization policy for metrics
            services.AddAuthorization(options =>
            {
                options.AddPolicy(ReadMetricsPolicy, policy =>
                {
                    policy.RequireAuthenticatedUser();
                });
            });

            return services;
        }
    }
}