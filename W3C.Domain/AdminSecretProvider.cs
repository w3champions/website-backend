using System;
using System.Net.Http.Headers;

namespace W3C.Domain;

/// <summary>
/// Single place that reads the shared <c>ADMIN_SECRET</c> sent to the sibling services (update,
/// replay, matchmaking). There is deliberately no fallback value: when the variable is unset or
/// empty no secret is sent at all, so the receiving service rejects the call instead of being
/// handed a publicly known default. Startup logs a warning once (see Program.cs).
/// </summary>
public static class AdminSecretProvider
{
    public const string VariableName = "ADMIN_SECRET";
    public const string HeaderName = "x-admin-secret";

    /// <summary>The configured secret, or null when <c>ADMIN_SECRET</c> is unset or empty.</summary>
    public static string Value
    {
        get
        {
            var secret = Environment.GetEnvironmentVariable(VariableName);
            return string.IsNullOrEmpty(secret) ? null : secret;
        }
    }

    public static bool IsConfigured => Value is not null;

    /// <summary>Adds the secret header, or nothing when no secret is configured.</summary>
    public static void AddTo(HttpRequestHeaders headers)
    {
        var secret = Value;
        if (secret is not null)
        {
            headers.Add(HeaderName, secret);
        }
    }
}
