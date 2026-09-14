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
using Serilog;

namespace W3ChampionsStatisticService.Maps;

/// <summary>
/// Reads the two-part upload body of design spec Appendix A.3 straight off the wire: the JSON
/// `metadata` part (capped at 64 KiB) then the `mapFile` part, spooled to an owner-only file in
/// <see cref="TemporaryMapLimits.TempUploadDir"/> while sha1 and mapProof are computed incrementally.
/// Nothing is ever held in memory, and a partial spool is deleted before the exception escapes.
/// </summary>
public static class TemporaryMapUploadReader
{
    /// <summary>RFC 2046 §5.1.1: a boundary is 1 to 70 characters.</summary>
    private const int MaxBoundaryLength = 70;

    private const UnixFileMode OwnerOnlyDirectoryMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    private const UnixFileMode OwnerOnlyFileMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    /// <summary>
    /// Closed settings for the client's metadata JSON. They are applied through
    /// <see cref="JsonSerializer.Create(JsonSerializerSettings)"/>, which ignores
    /// <see cref="JsonConvert.DefaultSettings"/>, so no global default can widen what an untrusted part
    /// may do: no type names, <c>$id</c>/<c>$ref</c>/<c>$type</c> read as ordinary (ignored) members,
    /// bounded nesting, and nothing allowed after the document.
    /// </summary>
    private static readonly JsonSerializerSettings MetadataJsonSettings = new()
    {
        TypeNameHandling = TypeNameHandling.None,
        MetadataPropertyHandling = MetadataPropertyHandling.Ignore,
        MaxDepth = 32,
        CheckAdditionalContent = true,
    };

    /// <summary>
    /// Validates the body's shape and spools the map file into <see cref="TemporaryMapLimits.TempUploadDir"/>.
    /// On success the caller owns the returned handle and must dispose it; on any failure no spool file
    /// is left behind.
    /// </summary>
    /// <remarks>
    /// Every failure is exactly one of four kinds, and each needs a different answer:
    /// <list type="number">
    /// <item><see cref="TemporaryMapUploadException"/>: the client sent something A.3 rejects —
    /// <c>400 METADATA</c> for a malformed body (no multipart content type or boundary, a missing,
    /// misnamed or misordered part, multipart headers the <see cref="MultipartReader"/> refuses, or
    /// a metadata part over 64 KiB, not valid JSON, or without an original file name),
    /// <c>400 EXTENSION</c> for a name that is not a .w3x/.w3m map, and <c>413 FILE_TOO_LARGE</c>
    /// for a file over <paramref name="maxFileBytes"/>. Answer with its status and body.</item>
    /// <item><see cref="IOException"/> from the request body itself, propagated unchanged: Kestrel's
    /// <see cref="BadHttpRequestException"/> (its body-size limit is 413, other request errors are
    /// 400), a client reset or abort, or a body that ends before its closing boundary (including an
    /// empty body). The reader cannot tell these apart; the caller can.</item>
    /// <item><see cref="TemporaryMapSpoolException"/>: a local disk fault while spooling — the spool
    /// directory cannot be created or made owner-only, or is a link, or the spool file cannot be
    /// created, written or closed. Already logged at Error. The client did nothing wrong: a bare 500.</item>
    /// <item><see cref="OperationCanceledException"/>, propagated unchanged, from the body or the spool.</item>
    /// </list>
    /// Disk faults are never surfaced as an <see cref="IOException"/>, so that type always means the body.
    /// </remarks>
    public static Task<TemporaryMapUpload> ReadAsync(
        Stream body,
        string contentType,
        CancellationToken cancellationToken,
        long maxFileBytes = TemporaryMapLimits.MaxFileBytes)
        => ReadAsync(body, contentType, TemporaryMapLimits.TempUploadDir, maxFileBytes, cancellationToken);

    /// <summary>
    /// The same read with the spool location made explicit, so tests can use a private directory
    /// and a failing spool file. <paramref name="openSpoolFile"/> defaults to the owner-only file.
    /// </summary>
    internal static async Task<TemporaryMapUpload> ReadAsync(
        Stream body,
        string contentType,
        string spoolDirectory,
        long maxFileBytes,
        CancellationToken cancellationToken,
        Func<string, Stream> openSpoolFile = null)
    {
        try
        {
            return await ReadPartsAsync(
                body, contentType, spoolDirectory, maxFileBytes, openSpoolFile ?? OpenSpoolFile, cancellationToken);
        }
        catch (InvalidDataException)
        {
            // MultipartReader's own format checks: an oversized preamble or header block, too many
            // headers, or a header line without a colon.
            throw Malformed();
        }
    }

    private static async Task<TemporaryMapUpload> ReadPartsAsync(
        Stream body,
        string contentType,
        string spoolDirectory,
        long maxFileBytes,
        Func<string, Stream> openSpoolFile,
        CancellationToken cancellationToken)
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

        var tempFilePath = Path.Combine(PrepareSpoolDirectory(spoolDirectory), $"{Guid.NewGuid():N}.tmp");

        try
        {
            var (sha1, mapProof, sizeBytes) = await SpoolAsync(
                fileSection.Body, tempFilePath, maxFileBytes, openSpoolFile, cancellationToken);
            return new TemporaryMapUpload(metadata, tempFilePath, sha1, mapProof, sizeBytes, extension);
        }
        catch
        {
            TemporaryMapUpload.DeleteTempFile(tempFilePath);
            throw;
        }
    }

    private static async Task<(string Sha1, string MapProof, long SizeBytes)> SpoolAsync(
        Stream source,
        string tempFilePath,
        long maxFileBytes,
        Func<string, Stream> openSpoolFile,
        CancellationToken cancellationToken)
    {
        using var hasher = new MapProofHasher();
        var buffer = ArrayPool<byte>.Shared.Rent(TemporaryMapUpload.FileBufferBytes);

        try
        {
            Stream spool;
            try
            {
                spool = openSpoolFile(tempFilePath);
            }
            catch (Exception ex) when (IsDiskFault(ex))
            {
                throw SpoolFault("The spool file could not be created.", tempFilePath, ex);
            }

            try
            {
                int read;
                while ((read = await source.ReadAsync(buffer.AsMemory(0, TemporaryMapUpload.FileBufferBytes), cancellationToken)) > 0)
                {
                    if (hasher.BytesHashed + read > maxFileBytes)
                    {
                        throw new TemporaryMapUploadException(StatusCodes.Status413PayloadTooLarge, "FILE_TOO_LARGE");
                    }

                    hasher.Append(buffer.AsSpan(0, read));
                    try
                    {
                        await spool.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    }
                    catch (Exception ex) when (IsDiskFault(ex))
                    {
                        throw SpoolFault("The spool file could not be written.", tempFilePath, ex);
                    }
                }
            }
            catch
            {
                await AbandonSpoolAsync(spool, tempFilePath);
                throw;
            }

            try
            {
                // Closing flushes the stream's buffer, so a full disk can surface here too. The handle is
                // released even when the flush fails, and the caller then deletes the file.
                await spool.DisposeAsync();
            }
            catch (Exception ex) when (IsDiskFault(ex))
            {
                throw SpoolFault("The spool file could not be closed.", tempFilePath, ex);
            }
        }
        finally
        {
            // The pool is process-wide: clear the map bytes before another renter can see them.
            ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
        }

        var (sha1, mapProof) = hasher.Finish();
        return (sha1, mapProof, hasher.BytesHashed);
    }

    /// <summary>
    /// Makes the spool directory owner-only (0700) where the OS has Unix modes, and refuses one that is a
    /// link: a pre-planted link in a shared temp directory would redirect the map bytes elsewhere.
    /// Returns the directory path without a trailing separator.
    /// </summary>
    private static string PrepareSpoolDirectory(string spoolDirectory)
    {
        // A trailing separator would make the link check follow the link instead of inspecting it.
        var path = Path.TrimEndingDirectorySeparator(spoolDirectory);
        bool isLink;
        try
        {
            var directory = OperatingSystem.IsWindows()
                ? Directory.CreateDirectory(path)
                : Directory.CreateDirectory(path, OwnerOnlyDirectoryMode);
            isLink = directory.LinkTarget != null;
            if (!isLink && !OperatingSystem.IsWindows())
            {
                // CreateDirectory leaves an existing directory's mode alone. Only the owner may chmod,
                // so this also refuses a directory another user created first.
                File.SetUnixFileMode(path, OwnerOnlyDirectoryMode);
            }
        }
        catch (Exception ex) when (IsDiskFault(ex))
        {
            throw SpoolFault("The spool directory could not be prepared.", path, ex);
        }

        if (isLink)
        {
            throw SpoolFault("The spool directory is a link; refusing to spool through it.", path, null);
        }

        return path;
    }

    private static Stream OpenSpoolFile(string path)
    {
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
            BufferSize = TemporaryMapUpload.FileBufferBytes,
            Options = FileOptions.Asynchronous,
        };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = OwnerOnlyFileMode;
        }

        return new FileStream(path, options);
    }

    /// <summary>
    /// Closes a spool that is being abandoned because of another failure. That failure must win, so a
    /// close error here is logged, not thrown; the file is deleted next either way.
    /// </summary>
    private static async Task AbandonSpoolAsync(Stream spool, string tempFilePath)
    {
        try
        {
            await spool.DisposeAsync();
        }
        catch (Exception ex) when (IsDiskFault(ex))
        {
            Log.Warning(ex, "Could not close an abandoned temporary map spool file {SpoolPath}", tempFilePath);
        }
    }

    private static bool IsDiskFault(Exception exception)
        => exception is IOException or UnauthorizedAccessException;

    private static TemporaryMapSpoolException SpoolFault(string message, string path, Exception cause)
    {
        // The path is a server-chosen directory or a random file name: no proof, token or client data.
        Log.Error(cause, "Temporary map upload spool fault at {SpoolPath}: {SpoolFault}", path, message);
        return new TemporaryMapSpoolException(message, cause);
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
            using var json = new JsonTextReader(new StringReader(Encoding.UTF8.GetString(buffer, 0, total)));
            metadata = JsonSerializer.Create(MetadataJsonSettings).Deserialize<TemporaryMapUploadMetadata>(json);
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
