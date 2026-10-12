using System;
using System.Buffers;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace W3ChampionsStatisticService.NetworkDiagnostics.Submission;

/// <summary>
/// Reads the (already decompressed) C5 body up to maxBytes and parses it with <see cref="SubmissionJson.Options"/>.
/// It never asks the stream for more than one byte past the cap, so a decompression bomb is inflated only that far.
/// Every body problem comes back as an error text for a 400; nothing about the body throws.
/// </summary>
public static class SubmissionBodyReader
{
    public const string TooLarge = "payload_too_large";
    public const string Missing = "Missing body";
    public const string Malformed = "Malformed JSON";

    private const int ChunkBytes = 81920;

    public static async Task<(NetDiagSubmission Dto, string Error)> ReadAsync(Stream body, int maxBytes, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = ArrayPool<byte>.Shared.Rent(ChunkBytes);
        try
        {
            while (true)
            {
                var wanted = (int)Math.Min(ChunkBytes, maxBytes + 1L - buffer.Length);
                var read = await body.ReadAsync(chunk.AsMemory(0, wanted), cancellationToken);
                if (read == 0) break;
                buffer.Write(chunk, 0, read);
                if (buffer.Length > maxBytes) return (null, TooLarge);
            }
        }
        catch (InvalidDataException)
        {
            // Content-Encoding: gzip on a body that is not (valid) gzip.
            return (null, Malformed);
        }
        catch (BadHttpRequestException e) when (e.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            // The server's request size limit on the compressed bytes.
            return (null, TooLarge);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(chunk);
        }

        if (buffer.Length == 0) return (null, Missing);
        buffer.Position = 0;
        try
        {
            var dto = await JsonSerializer.DeserializeAsync<NetDiagSubmission>(buffer, SubmissionJson.Options, cancellationToken);
            return dto == null ? (null, Missing) : (dto, null);
        }
        catch (JsonException)
        {
            return (null, Malformed);
        }
    }
}
