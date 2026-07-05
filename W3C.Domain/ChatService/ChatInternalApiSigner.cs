using System;
using System.Security.Cryptography;
using System.Text;

namespace W3C.Domain.ChatService;

/// <summary>Signs wb→chat internal-API requests per the pinned C7 scheme (see test vector block).
/// Pure/static; the secret is a raw UTF-8 key; the MAC is over the EXACT raw body string that will
/// be sent — callers must serialize once and pass the same string to the HTTP content.
///
/// <para>Interop note (uppercase hex): <see cref="Convert.ToHexString(byte[])"/> always emits
/// UPPERCASE hex. This is safe cross-repo — chat-service's counterpart <c>HmacSignatureVerifier</c>
/// hex-decodes the received signature case-insensitively (<c>Convert.FromHexString</c>) before a
/// constant-time byte comparison, so casing never affects verification. Do not "fix" this to
/// lowercase; the pinned vectors below are compared case-insensitively for the same reason.</para>
/// </summary>
public static class ChatInternalApiSigner
{
    public const string TimestampHeaderName = "X-W3C-Webhook-Timestamp";
    public const string SignatureHeaderName = "X-W3C-Signature";

    public static string CreateSignatureHeaderValue(string secret, string timestamp, string rawBody)
    {
        ArgumentException.ThrowIfNullOrEmpty(secret);
        ArgumentException.ThrowIfNullOrEmpty(timestamp);

        var key = Encoding.UTF8.GetBytes(secret);
        var message = Encoding.UTF8.GetBytes($"v1.{timestamp}.{rawBody ?? string.Empty}");
        return $"v1={Convert.ToHexString(HMACSHA256.HashData(key, message))}";
    }
}
