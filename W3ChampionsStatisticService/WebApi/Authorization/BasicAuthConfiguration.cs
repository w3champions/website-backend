using AspNetCore.Authentication.Basic;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Security.Cryptography;
using System.Text;
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
                        if (CredentialsMatch(username, password, context.Username, context.Password))
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

    /// <summary>
    /// Constant-time credential check. Both values are hashed first so neither the comparison time nor a
    /// length mismatch reveals anything about the expected credentials.
    /// </summary>
    public static bool CredentialsMatch(string expectedUsername, string expectedPassword, string username, string password) =>
        FixedTimeEquals(expectedUsername, username) & FixedTimeEquals(expectedPassword, password);

    private static bool FixedTimeEquals(string expected, string provided) =>
        CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(expected ?? "")),
            SHA256.HashData(Encoding.UTF8.GetBytes(provided ?? "")));
}
