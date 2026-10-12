using System;
using System.Collections.Generic;
using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using W3C.Domain;

namespace W3ChampionsStatisticService.Extensions;

/// <summary>
/// Registers every outbound HttpClient with explicit limits. The framework default is a 100 s request
/// timeout and an OS-level connect timeout (~2 min on Linux), so a dead or wedged sibling service held
/// requests open for minutes. Timeouts are sized from observed latencies (see PR description):
/// sibling-service calls answer in well under 1.5 s at p98.
/// </summary>
public static class HttpClientServiceCollectionExtensions
{
    /// <summary>TCP connect budget. Internal docker-network connects take ~1 ms.</summary>
    public static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Recycle pooled connections so DNS is re-resolved: the singleton clients keep their handler for
    /// the process lifetime, and a recreated container comes back with a new IP.
    /// </summary>
    public static readonly TimeSpan PooledConnectionLifetime = TimeSpan.FromMinutes(2);

    /// <summary>Request timeout for any client without an entry in <see cref="Timeouts"/>.</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    public static readonly IReadOnlyDictionary<string, TimeSpan> Timeouts = new Dictionary<string, TimeSpan>
    {
        // Regular calls answer in < 1.5 s (p98), but admin smurf queries have been observed at ~30 s.
        [HttpClientNames.Matchmaking] = TimeSpan.FromSeconds(30),
        [HttpClientNames.MatchmakingLogs] = TimeSpan.FromSeconds(60),
        // On the hot path of player profile requests: fail fast rather than pile requests up.
        [HttpClientNames.Identification] = TimeSpan.FromSeconds(5),
        [HttpClientNames.Chat] = TimeSpan.FromSeconds(10),
        [HttpClientNames.Update] = TimeSpan.FromSeconds(15),
        // Map uploads stream the admin's multipart body (up to the 128 MiB request limit) straight
        // through, so the timeout covers their upload time too: 128 MiB in 15 min needs ~1.2 Mbit/s.
        [HttpClientNames.UpdateUpload] = TimeSpan.FromMinutes(15),
        // Replay downloads: observed p99.9 of the proxying endpoint is ~16 s.
        [HttpClientNames.Replay] = TimeSpan.FromSeconds(30),
        [HttpClientNames.War3Info] = TimeSpan.FromSeconds(15),
        // A buyer waits for the payment page. Stripe.net sends a failed call again (same idempotency key).
        [HttpClientNames.Stripe] = TimeSpan.FromSeconds(20),
    };

    public static IServiceCollection AddW3CHttpClients(this IServiceCollection services)
    {
        // Applies to every client, including typed clients registered elsewhere (Turnstile, Patreon).
        services.ConfigureHttpClientDefaults(builder => builder
            .ConfigurePrimaryHttpMessageHandler(CreatePrimaryHandler)
            .ConfigureHttpClient(client => client.Timeout = DefaultTimeout));

        foreach (var (name, timeout) in Timeouts)
        {
            services.AddHttpClient(name, client => client.Timeout = timeout);
        }

        return services;
    }

    public static SocketsHttpHandler CreatePrimaryHandler() => new()
    {
        ConnectTimeout = ConnectTimeout,
        PooledConnectionLifetime = PooledConnectionLifetime,
    };
}
