using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;

namespace W3ChampionsStatisticService.WebApi.Authorization;

public static class BasicAuthConfiguration
{
    public const string ReadMetricsPolicy = "ReadMetrics";

    public static IServiceCollection AddBasicAuthForMetrics(this IServiceCollection services)
    {
        // Add authorization policy for metrics
        services.AddAuthorization(options =>
        {
            options.AddPolicy(ReadMetricsPolicy, policy =>
            {
                policy.Requirements.Add(new MetricsBasicAuthRequirement());
            });
        });

        services.AddSingleton<IAuthorizationHandler, MetricsBasicAuthHandler>();

        return services;
    }
}

public class MetricsBasicAuthRequirement : IAuthorizationRequirement
{
}

public class MetricsBasicAuthHandler : AuthorizationHandler<MetricsBasicAuthRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, MetricsBasicAuthRequirement requirement)
    {
        if (context.Resource is HttpContext httpContext)
        {
            // Get credentials from environment variables or fallback to defaults
            string expectedUsername = Environment.GetEnvironmentVariable("METRICS_ENDPOINT_AUTH_USERNAME") ?? "admin";
            string expectedPassword = Environment.GetEnvironmentVariable("METRICS_ENDPOINT_AUTH_PASSWORD") ?? "admin";

            var authHeader = httpContext.Request.Headers["Authorization"].ToString();
            
            if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
            {
                // No Basic auth header, fail silently without logging warnings about Bearer tokens
                context.Fail();
                return Task.CompletedTask;
            }

            try
            {
                var encodedCredentials = authHeader.Substring("Basic ".Length).Trim();
                var decodedCredentials = Encoding.UTF8.GetString(Convert.FromBase64String(encodedCredentials));
                var credentials = decodedCredentials.Split(':', 2);

                if (credentials.Length == 2 && credentials[0] == expectedUsername && credentials[1] == expectedPassword)
                {
                    // Create and set the user principal on the HttpContext
                    var claims = new[] { new Claim("role", "MetricsReader") };
                    var identity = new ClaimsIdentity(claims, "BasicAuthentication");
                    httpContext.User = new ClaimsPrincipal(identity);
                    context.Succeed(requirement);
                }
                else
                {
                    context.Fail();
                }
            }
            catch
            {
                context.Fail();
            }
        }
        else
        {
            context.Fail();
        }

        return Task.CompletedTask;
    }
}
