using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Serilog;

namespace W3C.Domain.ChatService;

/// <summary>
/// Clone of <see cref="RelationshipChangeNotifier"/>'s dispatch discipline — same HMAC scheme, same
/// fire-and-forget <see cref="Task.Run"/>, same 2 attempts at 3 s each, same self-disable and the same
/// never-log-the-secret rule — carrying a battleTag list instead of a relationship triple.
/// </summary>
public class FlairChangeNotifier(IHttpClientFactory httpClientFactory, ChatPingSettings settings) : IFlairChangeNotifier
{
    private const int TimeoutSecondsPerAttempt = 3;
    private const int MaxAttempts = 2; // initial + one retry, matching RelationshipChangeNotifier

    // Chat-service rejects any batch above ChatLimits.InternalMaxMembersPerCall outright, with no
    // partial processing. A clan delete can affect more members than that, so an unchunked send would
    // lose the ENTIRE notification for exactly the largest clans.
    private const int MaxBattleTagsPerRequest = 64;

    private readonly ChatPingSettings _settings = settings;
    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;

    /// <summary>Test seam: the background dispatch started by the most recent call.</summary>
    public Task LastDispatch { get; private set; } = Task.CompletedTask;

    public void NotifyChanged(IReadOnlyCollection<string> battleTags)
    {
        if (!_settings.Enabled) return; // silent no-op; startup already logged once (Program.cs)

        if (battleTags == null) return;

        var usable = battleTags
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (usable.Count == 0) return;

        LastDispatch = Task.Run(() => SendAllAsync(usable));
    }

    private async Task SendAllAsync(List<string> battleTags)
    {
        foreach (var chunk in battleTags.Chunk(MaxBattleTagsPerRequest))
        {
            await SendWithRetryAsync(chunk);
        }
    }

    private async Task SendWithRetryAsync(IReadOnlyCollection<string> battleTags)
    {
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                // Serialize ONCE and reuse the same string for both the signature and the content —
                // signing a re-serialization would produce a valid-looking but rejected request.
                var body = JsonConvert.SerializeObject(new { battleTags });
                var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
                var request = new HttpRequestMessage(HttpMethod.Post,
                    $"{_settings.ChatApiUrl}/internal/profile-changes")
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json")
                };
                request.Headers.Add(ChatInternalApiSigner.TimestampHeaderName, timestamp);
                request.Headers.Add(ChatInternalApiSigner.SignatureHeaderName,
                    ChatInternalApiSigner.CreateSignatureHeaderValue(_settings.Secret, timestamp, body));

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(TimeoutSecondsPerAttempt));
                var response = await _httpClientFactory.CreateClient().SendAsync(request, cts.Token);
                if (response.IsSuccessStatusCode) return;
                // Non-2xx: fall out of the loop body (no throw, no return) and let the for-loop
                // either retry or -- on the last attempt -- fall through to the post-loop log.
            }
            catch (Exception) when (attempt < MaxAttempts)
            {
                // Retry once, immediately — the retry may still succeed, so nothing is logged here.
            }
            catch (Exception e)
            {
                Log.Warning(e, "Flair change-ping failed after {Attempts} attempts for {Count} battleTag(s)",
                    MaxAttempts, battleTags.Count); // never logs the secret, the signature, or the tags
                return;
            }
        }

        Log.Warning("Flair change-ping rejected by chat-service after {Attempts} attempts for {Count} battleTag(s)",
            MaxAttempts, battleTags.Count);
    }
}
