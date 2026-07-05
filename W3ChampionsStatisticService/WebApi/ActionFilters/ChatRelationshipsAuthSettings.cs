using System;

namespace W3ChampionsStatisticService.WebApi.ActionFilters;

/// <summary>
/// Settings for the inbound fail-closed shared-secret auth filter (see
/// <see cref="ChatServiceSecretAuthFilter"/>) guarding the endpoint chat-service calls to read a
/// player's friends/blocked lists. Unlike <see cref="W3C.Domain.ChatService.ChatPingSettings"/>
/// (Task 2's outbound ping secret, which fails safely-disabled when unset), this secret has the
/// OPPOSITE polarity: an unset <c>CHAT_RELATIONSHIPS_API_SECRET</c> means the endpoint is LOCKED —
/// every request is rejected — rather than left open. There is no fallback default on purpose.
/// </summary>
public class ChatRelationshipsAuthSettings(string secret)
{
    public string Secret { get; } = secret;
    public bool Configured => !string.IsNullOrWhiteSpace(Secret);

    public static ChatRelationshipsAuthSettings FromEnvironment() =>
        new(Environment.GetEnvironmentVariable("CHAT_RELATIONSHIPS_API_SECRET"));
}
