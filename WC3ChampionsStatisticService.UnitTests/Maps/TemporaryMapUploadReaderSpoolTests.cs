using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using NUnit.Framework;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using W3ChampionsStatisticService.Maps;

namespace WC3ChampionsStatisticService.Tests.Maps;

/// <summary>
/// Failures while spooling and what they leave behind: body and spool faults, cleanup, and the spool's own
/// hygiene (link refusal, owner-only modes, a cleared copy buffer).
/// </summary>
[TestFixture]
public class TemporaryMapUploadReaderSpoolTests : TemporaryMapUploadReaderTestBase
{
    /// <summary>0755, a directory's mode under the common 022 umask.</summary>
    private const UnixFileMode WorldReadableDirectoryMode = UnixFileMode.UserRead | UnixFileMode.UserWrite |
        UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead |
        UnixFileMode.OtherExecute;

    [Test]
    public void ABodyThatEndsMidFile_SurfacesTheReadError_AndLeavesNoTempFile()
    {
        var (full, contentType) = BuildMultipartBytes(MinimalMetadata("x.w3x"), new byte[100_000]);
        var body = new InterruptedBodyStream(full, interruptAtByte: 60_000, SpoolDirectory, failure: null);

        Assert.ThrowsAsync<IOException>(
            () => Read(body, contentType));

        AssertASpoolExistedAndWasDeleted(body);
    }

    [TestCase("connection-reset")]
    [TestCase("kestrel-body-limit")]
    [TestCase("cancelled")]
    public void ATransportFailureMidFile_PropagatesUnchanged_AndLeavesNoTempFile(string failureKind)
    {
        using var cts = new CancellationTokenSource();
        Exception thrown = failureKind switch
        {
            "connection-reset" => new IOException("The client reset the request stream."),
            "kestrel-body-limit" => new BadHttpRequestException("Request body too large.", StatusCodes.Status413PayloadTooLarge),
            _ => new OperationCanceledException(cts.Token),
        };
        var (full, contentType) = BuildMultipartBytes(MinimalMetadata("x.w3x"), new byte[100_000]);
        var body = new InterruptedBodyStream(full, interruptAtByte: 60_000, SpoolDirectory, failure: () =>
        {
            if (thrown is OperationCanceledException)
            {
                cts.Cancel();
            }

            return thrown;
        });

        var ex = Assert.CatchAsync(() => Read(body, contentType, cancellationToken: cts.Token));

        Assert.That(ex, Is.SameAs(thrown), "the reader must not reclassify transport failures; the controller does");
        AssertASpoolExistedAndWasDeleted(body);
    }

    [Test]
    public async Task TheCopyBuffer_HoldsNoMapBytes_OnceItIsBackInThePool()
    {
        // The buffer comes from the process-wide ArrayPool, so whoever rents it next must not see map bytes.
        var fileBytes = Enumerable.Repeat((byte)0xAB, 100_000).ToArray();
        var (body, contentType) = BuildMultipart(MinimalMetadata("x.w3x"), fileBytes);
        var spool = new BufferRecordingSpoolStream();

        using var upload = await Read(body, contentType, openSpoolFile: _ => spool);

        Assert.That(upload.SizeBytes, Is.EqualTo(fileBytes.Length));
        Assert.That(spool.SourceArrays, Is.Not.Empty, "the reader must write straight from its pooled buffer");
        foreach (var array in spool.SourceArrays)
        {
            Assert.That(array.All(b => b == 0), Is.True, "the pooled buffer must be cleared when it is returned");
        }
    }

    [Test]
    public void ASpoolDirectoryPathHeldByAFile_IsASpoolFault_NotAnIOException()
    {
        Directory.CreateDirectory(TestRoot);
        File.WriteAllText(SpoolDirectory, "a regular file where the spool directory should be");
        var (body, contentType) = BuildMultipart(MinimalMetadata("x.w3x"), Encoding.UTF8.GetBytes("abc"));

        var ex = Assert.ThrowsAsync<TemporaryMapSpoolException>(() => Read(body, contentType));

        Assert.That(ex.InnerException, Is.InstanceOf<IOException>());
        Assert.That(FilesIn(TestRoot), Is.EqualTo(new[] { SpoolDirectory }), "nothing may be spooled");
    }

    [TestCase("disk")]
    [TestCase("access-denied")]
    public void ASpoolFileThatCannotBeCreated_IsASpoolFault(string failureKind)
    {
        var thrown = SpoolFailure(failureKind);
        var (body, contentType) = BuildMultipart(MinimalMetadata("x.w3x"), Encoding.UTF8.GetBytes("abc"));

        var ex = Assert.ThrowsAsync<TemporaryMapSpoolException>(
            () => Read(body, contentType, openSpoolFile: _ => throw thrown));

        Assert.That(ex.InnerException, Is.SameAs(thrown));
        AssertSpoolDirectoryHasNoFiles();
    }

    [TestCase("disk")]
    [TestCase("access-denied")]
    public void ASpoolWriteFailure_IsASpoolFault_AndLeavesNoTempFile(string failureKind)
    {
        var thrown = SpoolFailure(failureKind);
        var (body, contentType) = BuildMultipart(MinimalMetadata("x.w3x"), new byte[100_000]);
        var opened = 0;

        var ex = Assert.ThrowsAsync<TemporaryMapSpoolException>(() => Read(body, contentType, openSpoolFile: path =>
        {
            opened++;
            return new FaultingSpoolStream(File.Create(path), SpoolFailurePoint.Write, thrown);
        }));

        Assert.That(ex.InnerException, Is.SameAs(thrown));
        Assert.That(opened, Is.EqualTo(1), "the spool file must have been created before the write failed");
        AssertSpoolDirectoryHasNoFiles();
    }

    [Test]
    public void ASpoolCloseFailure_IsASpoolFault_AndLeavesNoTempFile()
    {
        // A buffered FileStream reports a full disk when it flushes on close.
        var thrown = SpoolFailure("disk");
        var (body, contentType) = BuildMultipart(MinimalMetadata("x.w3x"), Encoding.UTF8.GetBytes("abc"));

        var ex = Assert.ThrowsAsync<TemporaryMapSpoolException>(() => Read(body, contentType,
            openSpoolFile: path => new FaultingSpoolStream(File.Create(path), SpoolFailurePoint.Close, thrown)));

        Assert.That(ex.InnerException, Is.SameAs(thrown));
        AssertSpoolDirectoryHasNoFiles();
    }

    [Test]
    public void ASpoolFault_IsLoggedOnceAtError_WithItsCause_AndNeverWithTheProof()
    {
        var thrown = SpoolFailure("disk");
        var (body, contentType) = BuildMultipart(MinimalMetadata("x.w3x"), Encoding.UTF8.GetBytes("abc"));
        var sink = new CapturingSink();
        var previousLogger = Log.Logger;
        Log.Logger = new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(sink).CreateLogger();
        try
        {
            // A close failure comes after every byte was hashed: the latest point a proof could leak.
            Assert.ThrowsAsync<TemporaryMapSpoolException>(() => Read(body, contentType,
                openSpoolFile: path => new FaultingSpoolStream(File.Create(path), SpoolFailurePoint.Close, thrown)));
        }
        finally
        {
            Log.Logger = previousLogger;
        }

        var logEvent = sink.Events.Single();
        Assert.That(logEvent.Level, Is.EqualTo(LogEventLevel.Error));
        Assert.That(logEvent.Exception, Is.SameAs(thrown));
        var logged = logEvent.RenderMessage() + string.Join(",", logEvent.Properties.Values);
        Assert.That(logged, Does.Not.Contain(AbcMapProof));
        Assert.That(logged, Does.Not.Contain(MapProof.Hash(AbcMapProof)));
    }

    [Test]
    public void ACancelledSpoolWrite_PropagatesUnchanged_AndIsNotASpoolFault()
    {
        var thrown = new OperationCanceledException();
        var (body, contentType) = BuildMultipart(MinimalMetadata("x.w3x"), new byte[100_000]);

        var ex = Assert.CatchAsync(() => Read(body, contentType,
            openSpoolFile: path => new FaultingSpoolStream(File.Create(path), SpoolFailurePoint.Write, thrown)));

        Assert.That(ex, Is.SameAs(thrown));
        AssertSpoolDirectoryHasNoFiles();
    }

    [Test]
    public void ABodyFailureMidFile_WinsOverASpoolThatAlsoFailsToClose()
    {
        var bodyFailure = new IOException("The client reset the request stream.");
        var (full, contentType) = BuildMultipartBytes(MinimalMetadata("x.w3x"), new byte[100_000]);
        var body = new InterruptedBodyStream(full, interruptAtByte: 60_000, SpoolDirectory, failure: () => bodyFailure);

        var ex = Assert.CatchAsync(() => Read(body, contentType,
            openSpoolFile: path => new FaultingSpoolStream(File.Create(path), SpoolFailurePoint.Close, SpoolFailure("disk"))));

        Assert.That(ex, Is.SameAs(bodyFailure), "a close failure while abandoning the spool must not hide the body failure");
        AssertASpoolExistedAndWasDeleted(body);
    }

    [TestCase("body-failure")]
    [TestCase("file-too-large")]
    [TestCase("spool-write-fault")]
    public void AnAbandonedSpoolFile_IsClosedExactlyOnce(string failureKind)
    {
        // Unix unlinks an open file without complaint, so only the close count shows a leaked handle.
        var (full, contentType) = BuildMultipartBytes(MinimalMetadata("x.w3x"), new byte[100_000]);
        Stream body = failureKind == "body-failure"
            ? new InterruptedBodyStream(full, interruptAtByte: 60_000, SpoolDirectory,
                failure: () => new IOException("The client reset the request stream."))
            : new MemoryStream(full);
        var failAt = failureKind == "spool-write-fault" ? SpoolFailurePoint.Write : SpoolFailurePoint.None;
        FaultingSpoolStream spool = null;

        Assert.CatchAsync(() => Read(body, contentType, maxFileBytes: failureKind == "file-too-large" ? 50_000 : 100_000,
            openSpoolFile: path => spool = new FaultingSpoolStream(File.Create(path), failAt, SpoolFailure("disk"))));

        Assert.That(spool, Is.Not.Null, "the spool file must have been opened");
        Assert.That(spool.CloseCount, Is.EqualTo(1));
        AssertSpoolDirectoryHasNoFiles();
    }

    [TestCase("")]
    [TestCase("/")]
    [TestCase("//")]
    public void ASpoolDirectoryThatIsALink_IsRefused(string trailingSeparators)
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Ignore("Creating a directory symbolic link needs elevation on Windows.");
            return;
        }

        var target = Path.Combine(TestRoot, "elsewhere");
        Directory.CreateDirectory(target);
        File.SetUnixFileMode(target, WorldReadableDirectoryMode);
        Directory.CreateSymbolicLink(SpoolDirectory, target);
        // Trailing separators make a link check resolve through the link, however many there are.
        var spoolDirectory = SpoolDirectory + trailingSeparators;
        var (body, contentType) = BuildMultipart(MinimalMetadata("x.w3x"), Encoding.UTF8.GetBytes("abc"));

        Assert.ThrowsAsync<TemporaryMapSpoolException>(() => TemporaryMapUploadReader.ReadAsync(
            body, contentType, spoolDirectory, TemporaryMapLimits.MaxFileBytes, CancellationToken.None));

        Assert.That(FilesIn(target), Is.Empty, "nothing may be spooled through the link");
        Assert.That(File.GetUnixFileMode(target), Is.EqualTo(WorldReadableDirectoryMode), "the link target must be left untouched");
    }

    [Test]
    public async Task AnExistingSpoolDirectory_IsTightenedToOwnerOnly()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Ignore("Unix permission bits only.");
            return;
        }

        // Left over from an earlier run with default permissions; chmod, unlike mkdir, ignores the umask.
        Directory.CreateDirectory(SpoolDirectory);
        File.SetUnixFileMode(SpoolDirectory, WorldReadableDirectoryMode);
        Assert.That(File.GetUnixFileMode(SpoolDirectory), Is.EqualTo(WorldReadableDirectoryMode), "precondition");
        var (body, contentType) = BuildMultipart(MinimalMetadata("x.w3x"), Encoding.UTF8.GetBytes("abc"));

        using var upload = await Read(body, contentType);

        Assert.That(File.GetUnixFileMode(SpoolDirectory),
            Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute));
    }

    [Test]
    public async Task TheSpoolDirectoryAndFile_AreOwnerOnly()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Ignore("Unix permission bits only.");
            return;
        }

        var (body, contentType) = BuildMultipart(MinimalMetadata("x.w3x"), Encoding.UTF8.GetBytes("abc"));

        using var upload = await Read(body, contentType);

        Assert.That(File.GetUnixFileMode(SpoolDirectory),
            Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute));
        Assert.That(File.GetUnixFileMode(upload.TempFilePath),
            Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite));
    }

    private static Exception SpoolFailure(string failureKind)
        => failureKind == "disk"
            ? new IOException("No space left on device")
            : new UnauthorizedAccessException("Access to the path is denied.");

    private void AssertASpoolExistedAndWasDeleted(InterruptedBodyStream body)
    {
        Assert.That(body.TempFilesAtInterruption, Is.Not.Empty, "the interruption must hit while a spool file exists");
        AssertSpoolDirectoryHasNoFiles();
    }

    private sealed class CapturingSink : ILogEventSink
    {
        public List<LogEvent> Events { get; } = [];

        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }

    /// <summary>An in-memory spool that records which arrays the reader's writes were backed by.</summary>
    private sealed class BufferRecordingSpoolStream : MemoryStream
    {
        public HashSet<byte[]> SourceArrays { get; } = new(ReferenceEqualityComparer.Instance);

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (MemoryMarshal.TryGetArray(buffer, out var segment))
            {
                SourceArrays.Add(segment.Array);
            }

            return base.WriteAsync(buffer, cancellationToken);
        }
    }

    private enum SpoolFailurePoint
    {
        None,
        Write,
        Close,
    }

    /// <summary>
    /// A spool file that fails on its first write, when it is closed, or never, with the given exception,
    /// and counts how often it is closed.
    /// </summary>
    private sealed class FaultingSpoolStream(Stream inner, SpoolFailurePoint failAt, Exception failure) : Stream
    {
        public int CloseCount { get; private set; }

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
        {
            ThrowIfFailingAt(SpoolFailurePoint.Write);
            inner.Write(buffer, offset, count);
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            ThrowIfFailingAt(SpoolFailurePoint.Write);
            return inner.WriteAsync(buffer, cancellationToken);
        }

        public override async ValueTask DisposeAsync()
        {
            CloseCount++;
            await inner.DisposeAsync();
            ThrowIfFailingAt(SpoolFailurePoint.Close);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                CloseCount++;
                inner.Dispose();
            }

            base.Dispose(disposing);
        }

        private void ThrowIfFailingAt(SpoolFailurePoint point)
        {
            if (failAt == point)
            {
                throw failure;
            }
        }
    }

    /// <summary>
    /// Serves <c>data</c> up to <c>interruptAtByte</c>, then either ends cleanly (<c>failure</c> null) or
    /// throws the exception <c>failure</c> returns — a truncated body or a transport failure mid-read.
    /// It records the spool directory's files at that moment, so cleanup assertions cannot pass vacuously.
    /// </summary>
    private sealed class InterruptedBodyStream(byte[] data, int interruptAtByte, string spoolDirectory, Func<Exception> failure) : Stream
    {
        private int _position;

        public string[] TempFilesAtInterruption { get; private set; }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => ReadCore(buffer.AsSpan(offset, count));

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => Task.FromResult(ReadCore(buffer.AsSpan(offset, count)));

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(ReadCore(buffer.Span));

        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        private int ReadCore(Span<byte> destination)
        {
            if (_position >= interruptAtByte)
            {
                TempFilesAtInterruption ??= FilesIn(spoolDirectory);
                if (failure == null)
                {
                    return 0;
                }

                throw failure();
            }

            var count = Math.Min(destination.Length, interruptAtByte - _position);
            data.AsSpan(_position, count).CopyTo(destination);
            _position += count;
            return count;
        }
    }
}
