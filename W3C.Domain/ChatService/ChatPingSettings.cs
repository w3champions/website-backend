using System;

namespace W3C.Domain.ChatService;

/// <summary>
/// Settings for the fire-and-forget relationship change-ping client (see
/// <see cref="RelationshipChangeNotifier"/>). Reuses the existing chat-service base URL
/// (<c>CHAT_API</c> — see <see cref="ChatServiceClient"/>) rather than introducing a second env
/// var pointing at the same host. <see cref="Enabled"/> requires both a non-blank base URL and a
/// non-blank shared secret; the secret has no fallback default, so an unset
/// <c>CHAT_INTERNAL_API_SECRET</c> disables ping dispatch entirely (fail-closed, not fail-open).
/// </summary>
public class ChatPingSettings(string chatApiUrl, string secret)
{
    public string ChatApiUrl { get; } = chatApiUrl;
    public string Secret { get; } = secret;
    public bool Enabled { get; } = !string.IsNullOrWhiteSpace(chatApiUrl) && !string.IsNullOrWhiteSpace(secret);

    public static ChatPingSettings FromEnvironment() =>
        new(Environment.GetEnvironmentVariable("CHAT_API") ?? "https://chat-service.test.w3champions.com",
            Environment.GetEnvironmentVariable("CHAT_INTERNAL_API_SECRET"));
}
