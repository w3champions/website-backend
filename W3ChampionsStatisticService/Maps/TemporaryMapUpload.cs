using System;
using System.IO;
using Serilog;

namespace W3ChampionsStatisticService.Maps;

/// <summary>
/// A spooled upload: the bytes on disk plus the two digests computed while they were written.
/// Disposing deletes the temp file, so callers must wrap this in a <c>using</c>.
/// </summary>
public sealed class TemporaryMapUpload(
    TemporaryMapUploadMetadata metadata,
    string tempFilePath,
    string sha1,
    string mapProof,
    long sizeBytes,
    string extension) : IDisposable
{
    /// <summary>
    /// Buffer size for reading and writing spool files: the <see cref="Stream.CopyToAsync(Stream)"/> default,
    /// which keeps a FileStream's own buffer under the 85 000-byte large-object-heap threshold.
    /// </summary>
    internal const int FileBufferBytes = 81920;

    public TemporaryMapUploadMetadata Metadata { get; } = metadata;

    public string TempFilePath { get; } = tempFilePath;

    /// <summary>Server-computed, lowercase hex. Safe to log.</summary>
    public string Sha1 { get; } = sha1;

    /// <summary>
    /// Server-computed, lowercase hex. SECRET — never log it (§10.3). Keep this type a plain class:
    /// a record's generated ToString would print it wherever the handle is logged or traced.
    /// </summary>
    public string MapProof { get; } = mapProof;

    public long SizeBytes { get; } = sizeBytes;

    /// <summary>".w3x" or ".w3m", lowercased.</summary>
    public string Extension { get; } = extension;

    public Stream OpenRead()
        => new FileStream(TempFilePath, FileMode.Open, FileAccess.Read, FileShare.Read, FileBufferBytes,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

    public void Dispose() => DeleteTempFile(TempFilePath);

    /// <summary>
    /// Deletes a spool file. Never throws: it runs on failure paths whose original exception must win.
    /// Nothing else reclaims the spool directory, so a failed delete is logged. The path is a random
    /// file name and carries no secret.
    /// </summary>
    internal static void DeleteTempFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warning(ex, "Could not delete temporary map upload spool file {TempFilePath}", path);
        }
    }
}
