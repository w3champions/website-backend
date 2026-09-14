using System;
using System.Buffers;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;
using Newtonsoft.Json;

namespace W3ChampionsStatisticService.Maps;

/// <summary>
/// Reads the two-part upload body of design spec Appendix A.3 straight off the wire: the JSON
/// `metadata` part (capped at 64 KiB) then the `mapFile` part, spooled to
/// <see cref="TemporaryMapLimits.TempUploadDir"/> while sha1 and mapProof are computed incrementally.
/// Nothing is ever held in memory, and a partial spool is deleted before the exception escapes.
/// </summary>
public static class TemporaryMapUploadReader
{
    private const int CopyBufferBytes = 81920;

    /// <summary>RFC 2046 §5.1.1: a boundary is 1 to 70 characters.</summary>
    private const int MaxBoundaryLength = 70;

    /// <summary>
    /// Validates the body's shape and spools the map file. On success the caller owns the returned
    /// handle and must dispose it; on any failure no spool file is left behind.
    /// </summary>
    /// <exception cref="TemporaryMapUploadException">
    /// <c>400 METADATA</c> for a malformed body: no multipart content type or boundary, a missing or
    /// misnamed or misordered part, multipart headers the <see cref="MultipartReader"/> refuses, or a
    /// metadata part over 64 KiB, not valid JSON, or without an original file name.
    /// <c>400 EXTENSION</c> when the original file name is not a .w3x/.w3m map.
    /// <c>413 FILE_TOO_LARGE</c> when the map file exceeds <paramref name="maxFileBytes"/>.
    /// </exception>
    /// <remarks>
    /// Failures of the body stream itself propagate unchanged: an <see cref="IOException"/> (including
    /// Kestrel's <see cref="BadHttpRequestException"/> for its body-size limit, a client reset, and a
    /// body that ends before the closing boundary) or an <see cref="OperationCanceledException"/>.
    /// The reader cannot tell a transport limit or a client abort from a truncated body; the caller
    /// can, so it maps them.
    /// </remarks>
    public static async Task<TemporaryMapUpload> ReadAsync(
        Stream body,
        string contentType,
        CancellationToken cancellationToken,
        long maxFileBytes = TemporaryMapLimits.MaxFileBytes)
    {
        try
        {
            return await ReadPartsAsync(body, contentType, maxFileBytes, cancellationToken);
        }
        catch (InvalidDataException)
        {
            // MultipartReader's own format checks: an oversized preamble or header block, too many
            // headers, or a header line without a colon.
            throw Malformed();
        }
    }

    private static async Task<TemporaryMapUpload> ReadPartsAsync(
        Stream body, string contentType, long maxFileBytes, CancellationToken cancellationToken)
    {
        var reader = new MultipartReader(GetBoundary(contentType), body);

        var metadata = await ReadMetadataAsync(reader, cancellationToken);
        if (!TemporaryMapNaming.TryGetExtension(metadata.OriginalFileName, out var extension))
        {
            throw new TemporaryMapUploadException(StatusCodes.Status400BadRequest, "EXTENSION");
        }

        var fileSection = await reader.ReadNextSectionAsync(cancellationToken);
        if (fileSection == null || !IsPartNamed(fileSection, "mapFile"))
        {
            throw Malformed();
        }

        Directory.CreateDirectory(TemporaryMapLimits.TempUploadDir);
        var tempFilePath = Path.Combine(TemporaryMapLimits.TempUploadDir, $"{Guid.NewGuid():N}.tmp");

        try
        {
            var (sha1, mapProof, sizeBytes) = await SpoolAsync(fileSection.Body, tempFilePath, maxFileBytes, cancellationToken);
            return new TemporaryMapUpload(metadata, tempFilePath, sha1, mapProof, sizeBytes, extension);
        }
        catch
        {
            TemporaryMapUpload.DeleteTempFile(tempFilePath);
            throw;
        }
    }

    private static async Task<(string Sha1, string MapProof, long SizeBytes)> SpoolAsync(
        Stream source, string tempFilePath, long maxFileBytes, CancellationToken cancellationToken)
    {
        using var hasher = new MapProofHasher();
        var buffer = ArrayPool<byte>.Shared.Rent(CopyBufferBytes);

        try
        {
            await using var file = new FileStream(tempFilePath, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, CopyBufferBytes, FileOptions.Asynchronous);

            int read;
            while ((read = await source.ReadAsync(buffer.AsMemory(0, CopyBufferBytes), cancellationToken)) > 0)
            {
                if (hasher.BytesHashed + read > maxFileBytes)
                {
                    throw new TemporaryMapUploadException(StatusCodes.Status413PayloadTooLarge, "FILE_TOO_LARGE");
                }

                hasher.Append(buffer.AsSpan(0, read));
                await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        var (sha1, mapProof) = hasher.Finish();
        return (sha1, mapProof, hasher.BytesHashed);
    }

    private static async Task<TemporaryMapUploadMetadata> ReadMetadataAsync(
        MultipartReader reader, CancellationToken cancellationToken)
    {
        var section = await reader.ReadNextSectionAsync(cancellationToken);
        if (section == null || !IsPartNamed(section, "metadata"))
        {
            throw Malformed();
        }

        // One byte over the cap is enough to reject without buffering the rest.
        var buffer = new byte[TemporaryMapLimits.MaxMetadataBytes + 1];
        var total = 0;
        int read;
        while (total < buffer.Length &&
               (read = await section.Body.ReadAsync(buffer.AsMemory(total, buffer.Length - total), cancellationToken)) > 0)
        {
            total += read;
        }

        if (total > TemporaryMapLimits.MaxMetadataBytes)
        {
            throw Malformed();
        }

        TemporaryMapUploadMetadata metadata;
        try
        {
            metadata = JsonConvert.DeserializeObject<TemporaryMapUploadMetadata>(
                Encoding.UTF8.GetString(buffer, 0, total));
        }
        catch (JsonException)
        {
            throw Malformed();
        }

        if (metadata == null || string.IsNullOrWhiteSpace(metadata.OriginalFileName))
        {
            throw Malformed();
        }

        return metadata;
    }

    private static bool IsPartNamed(MultipartSection section, string expected)
    {
        if (!ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var disposition))
        {
            return false;
        }

        var name = HeaderUtilities.RemoveQuotes(disposition.Name);
        return name.HasValue && string.Equals(name.Value, expected, StringComparison.Ordinal);
    }

    private static string GetBoundary(string contentType)
    {
        if (string.IsNullOrEmpty(contentType) ||
            !MediaTypeHeaderValue.TryParse(contentType, out var mediaType) ||
            !mediaType.MediaType.HasValue ||
            !mediaType.MediaType.Value.StartsWith("multipart/", StringComparison.OrdinalIgnoreCase))
        {
            throw Malformed();
        }

        var boundary = HeaderUtilities.RemoveQuotes(mediaType.Boundary);
        if (!boundary.HasValue || string.IsNullOrWhiteSpace(boundary.Value) || boundary.Value.Length > MaxBoundaryLength)
        {
            throw Malformed();
        }

        return boundary.Value;
    }

    private static TemporaryMapUploadException Malformed()
        => new(StatusCodes.Status400BadRequest, "METADATA");
}
