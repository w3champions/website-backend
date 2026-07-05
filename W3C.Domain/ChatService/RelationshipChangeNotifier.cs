using Newtonsoft.Json;
using Serilog;
using System;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace W3C.Domain.ChatService;

/// <summary>
/// Fire-and-forget, HMAC-signed relationship change-ping client. <see cref="NotifyChange"/> never
/// blocks the caller and never throws: dispatch happens on a background <see cref="Task.Run"/>,
/// and <see cref="SendWithRetryAsync"/> swallows every failure after a bounded best-effort retry
/// (see spec §14). Disabled entirely (silent no-op) when <see cref="ChatPingSettings.Enabled"/> is
/// false, e.g. no <c>CHAT_INTERNAL_API_SECRET</c> configured.
/// </summary>
public class RelationshipChangeNotifier(IHttpClientFactory httpClientFactory, ChatPingSettings settings) : IRelationshipChangeNotifier
{
    private const int TimeoutSecondsPerAttempt = 3;
    private const int MaxAttempts = 2; // initial + one retry (best-effort per spec §14)

    private readonly ChatPingSettings _settings = settings;
    private readonly HttpClient _httpClient = httpClientFactory.CreateClient();

    /// <summary>Test seam: the in-flight (or completed) background dispatch task, assigned by
    /// <see cref="NotifyChange"/> before it returns. <see cref="Task.CompletedTask"/> when disabled
    /// or skipped, so tests can deterministically await completion without sleeps. Harmless in
    /// production -- nothing else reads it.</summary>
    public Task LastDispatch { get; private set; } = Task.CompletedTask;

    public void NotifyChange(RelationshipChangeType type, string actor, string target)
    {
        if (!_settings.Enabled) return; // silent no-op; startup already logged once (Program.cs)

        if (string.IsNullOrWhiteSpace(actor) || string.IsNullOrWhiteSpace(target))
        {
            Log.Warning("Relationship change-ping skipped: blank participant ({Type})", type);
            return;
        }

        LastDispatch = Task.Run(() => SendWithRetryAsync(type, actor, target));
    }

    /// <summary>
    /// Sends the signed change-ping, retrying exactly once (immediately, no backoff) on either a
    /// non-2xx response or a thrown exception (e.g. timeout, connection failure). Never rethrows.
    ///
    /// <para>Catch-structure rationale: the first <c>catch</c> only matches while attempts remain
    /// (<c>attempt &lt; MaxAttempts</c>), so it swallows and retries; on the LAST attempt that
    /// filter is false, so the exception falls through to the second (unconditional) <c>catch</c>,
    /// which logs exactly once and returns. A non-2xx response never throws at all -- it just falls
    /// out of the loop body without returning -- so if every attempt is non-2xx, the loop runs to
    /// completion and the single post-loop <c>Log.Warning</c> fires. These two logging sites are
    /// mutually exclusive by construction (one is inside a catch that always returns, the other is
    /// only reached when the loop finishes without ever returning or catching), so exactly one
    /// warning is ever logged for a given call, regardless of whether the final attempt failed via
    /// a thrown exception or via a rejected response.</para>
    /// </summary>
    public async Task SendWithRetryAsync(RelationshipChangeType type, string actor, string target)
    {
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                var body = JsonConvert.SerializeObject(new { type = ToWireLiteral(type), actor, target });
                var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
                // Not wrapped in `using`: matches the ChatServiceClient idiom in this codebase
                // (HttpRequestMessage is never explicitly disposed there either).
                var request = new HttpRequestMessage(HttpMethod.Post,
                    $"{_settings.ChatApiUrl}/internal/relationship-changes")
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json")
                };
                request.Headers.Add(ChatInternalApiSigner.TimestampHeaderName, timestamp);
                request.Headers.Add(ChatInternalApiSigner.SignatureHeaderName,
                    ChatInternalApiSigner.CreateSignatureHeaderValue(_settings.Secret, timestamp, body));

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(TimeoutSecondsPerAttempt));
                var response = await _httpClient.SendAsync(request, cts.Token);
                if (response.IsSuccessStatusCode) return;
                // Non-2xx: fall out of the loop body (no throw, no return) and let the for-loop
                // either retry or -- on the last attempt -- fall through to the post-loop log.
            }
            catch (Exception) when (attempt < MaxAttempts)
            {
                // Retry once, immediately -- never logs here (the retry may still succeed).
            }
            catch (Exception e)
            {
                Log.Warning(e, "Relationship change-ping failed after {Attempts} attempts: {Type} {Actor}/{Target}",
                    MaxAttempts, type, actor, target); // never logs the secret or the signature
                return;
            }
        }

        Log.Warning("Relationship change-ping rejected by chat-service after {Attempts} attempts: {Type} {Actor}/{Target}",
            MaxAttempts, type, actor, target); // non-2xx path; never logs secret/signature
    }

    private static string ToWireLiteral(RelationshipChangeType type) => type switch
    {
        RelationshipChangeType.Block => "block",
        RelationshipChangeType.Unblock => "unblock",
        RelationshipChangeType.FriendAdd => "friendAdd",
        RelationshipChangeType.FriendRemove => "friendRemove",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
    };
}
