using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace W3ChampionsStatisticService.NetworkDiagnostics.Api;

/// <summary>
/// Input checks for the C6 controllers. They do not use [ApiController], so nothing answers a bad query value or a
/// bad body for them: every action asks here and answers 400 {"error": …} itself.
/// </summary>
public static class AdminRequests
{
    /// <summary>A run request with 500 BattleTags and 50 custom targets is about 20 KB.</summary>
    public const int MaxBodyBytes = 256 * 1024;
    private const int MaxReportedKeyLength = 64;
    private const int ReadChunkBytes = 16 * 1024;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Null when every bound value parsed; otherwise 400 naming the first parameter that did not (P8). Without this
    /// an unparsable page, pageSize, from or to would silently run with its default. The name is the C6 camelCase
    /// one: a property of a complex [FromQuery] model reports its error under the property name ("NodeId").
    /// </summary>
    public static NetDiagResult InvalidQuery(ModelStateDictionary modelState)
    {
        if (modelState.ErrorCount == 0) return null;
        var key = modelState.Where(e => e.Value is { Errors.Count: > 0 }).Select(e => e.Key).Order(StringComparer.Ordinal).First();
        if (key.Length > MaxReportedKeyLength) key = key[..MaxReportedKeyLength];
        key = JsonNamingPolicy.CamelCase.ConvertName(key);
        return NetDiagResult.Error(400, $"invalid query parameter: {key}");
    }

    /// <summary>
    /// Reads a camelCase JSON body of at most <see cref="MaxBodyBytes"/>, whatever the content type. An absent body and
    /// a JSON null give a null value (the validator names the problem). Stops reading one byte past the cap.
    /// </summary>
    public static async Task<(T Value, NetDiagResult Error)> ReadJsonBodyAsync<T>(HttpRequest request, CancellationToken cancellationToken)
        where T : class
    {
        if (request.ContentLength > MaxBodyBytes) return (null, TooLarge());

        using var buffer = new MemoryStream();
        var chunk = new byte[ReadChunkBytes];
        while (true)
        {
            var wanted = (int)Math.Min(chunk.Length, MaxBodyBytes + 1 - buffer.Length);
            var read = await request.Body.ReadAsync(chunk.AsMemory(0, wanted), cancellationToken);
            if (read == 0) break;
            buffer.Write(chunk, 0, read);
            if (buffer.Length > MaxBodyBytes) return (null, TooLarge());
        }
        if (buffer.Length == 0) return (null, null);

        try
        {
            buffer.Position = 0;
            return (await JsonSerializer.DeserializeAsync<T>(buffer, Json, cancellationToken), null);
        }
        catch (JsonException)
        {
            return (null, NetDiagResult.Error(400, "invalid_json"));
        }
    }

    private static NetDiagResult TooLarge() => NetDiagResult.Error(400, "payload_too_large");
}
