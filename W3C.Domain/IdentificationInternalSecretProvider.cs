using System;
using System.Net.Http.Headers;

namespace W3C.Domain;

/// <summary>
/// Single place that reads the shared <c>IDENTIFICATION_INTERNAL_API_SECRET</c> sent to
/// identification-service's internal endpoints (admin roster). There is deliberately no fallback:
/// when the variable is unset or empty no header is sent, so identification-service rejects the
/// call (fail closed on its side). Startup logs a warning once (see Program.cs).
/// </summary>
public static class IdentificationInternalSecretProvider
{
    public const string VariableName = "IDENTIFICATION_INTERNAL_API_SECRET";
    public const string HeaderName = "x-identification-internal-secret";

    /// <summary>The configured secret, or null when the variable is unset or empty.</summary>
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
