using System;

namespace W3ChampionsStatisticService.WebApi.ActionFilters;

/// <summary>
/// Settings for the inbound fail-closed shared-secret auth filter (see
/// <see cref="ChatServiceSecretAuthFilter"/>) guarding the endpoint chat-service calls to read a
/// player's friends/blocked lists. An unset <c>CHAT_RELATIONSHIPS_API_SECRET</c> means the
/// endpoint is LOCKED — every request is rejected with 401 — rather than left open. There is no
/// fallback default on purpose. This is a different (but equally safe) response to an unset
/// secret than <see cref="W3C.Domain.ChatService.ChatPingSettings"/> (Task 2's outbound ping
/// secret): that direction fails safe by disabling itself (the app keeps working, no ping is
/// sent), while this direction fails closed by locking (401 to everyone). Both are safe reactions
/// to the same "secret is unset" condition, applied to two different directions of traffic — they
/// are not "opposite polarities" of one mechanism.
/// </summary>
public class ChatRelationshipsAuthSettings(string secret)
{
    public string Secret { get; } = secret;
    public bool Configured => !string.IsNullOrWhiteSpace(Secret);

    public static ChatRelationshipsAuthSettings FromEnvironment() =>
        new(Environment.GetEnvironmentVariable("CHAT_RELATIONSHIPS_API_SECRET"));
}
