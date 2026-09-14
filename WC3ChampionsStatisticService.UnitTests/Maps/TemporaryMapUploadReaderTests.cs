using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using NUnit.Framework;
using W3ChampionsStatisticService.Maps;

namespace WC3ChampionsStatisticService.Tests.Maps;

[TestFixture]
public class TemporaryMapUploadReaderTests
{
    private const string AbcMapProof = "3b2724682f427727f8fa19236e04d3a5e3bdf218b7fe9c259c202e5264edbf4e";
    private const string AbcSha1 = "a9993e364706816aba3e25717850c26c9cd0d89d";
    private const string Boundary = "boundary-1";

    [Test]
    public async Task ReadsMetadataAndSpoolsFile_ComputingSha1AndProofInOnePass()
    {
        var (body, contentType) = BuildMultipart(
            "{\"sha1\":\"" + AbcSha1 + "\",\"originalFileName\":\"Legion TD (X3).w3x\",\"fileSize\":3," +
            "\"capture\":{\"lobbyMode\":\"mapped-forces\",\"maxTeams\":2,\"slotCount\":16,\"mappedForces\":[]," +
            "\"launcherVersion\":\"3.4.0\",\"capturedAt\":\"2026-09-14T09:10:39Z\"}}",
            Encoding.UTF8.GetBytes("abc"));

        using var upload = await TemporaryMapUploadReader.ReadAsync(body, contentType, CancellationToken.None);

        Assert.That(upload.Sha1, Is.EqualTo(AbcSha1));
        Assert.That(upload.MapProof, Is.EqualTo(AbcMapProof));
        Assert.That(upload.SizeBytes, Is.EqualTo(3));
        Assert.That(upload.Extension, Is.EqualTo(".w3x"));
        Assert.That(upload.Metadata.OriginalFileName, Is.EqualTo("Legion TD (X3).w3x"));
        Assert.That(upload.Metadata.Capture.SlotCount, Is.EqualTo(16));
        Assert.That(upload.Metadata.Capture.LobbyMode, Is.EqualTo("mapped-forces"));
        Assert.That(File.Exists(upload.TempFilePath), Is.True);

        await using var spooled = upload.OpenRead();
        using var reader = new StreamReader(spooled);
        Assert.That(await reader.ReadToEndAsync(), Is.EqualTo("abc"));
    }

    [Test]
    public async Task SpoolsAFileSpanningManyReads_ByteForByte_WithOnePassDigests()
    {
        var fileBytes = new byte[300_000];
        new Random(42).NextBytes(fileBytes);
        var (body, contentType) = BuildMultipart(MinimalMetadata("big.w3m"), fileBytes);

        using var upload = await TemporaryMapUploadReader.ReadAsync(body, contentType, CancellationToken.None);

        var expectedProof = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(MapProof.Prefix).Concat(fileBytes).ToArray())).ToLowerInvariant();
        Assert.That(upload.Sha1, Is.EqualTo(Convert.ToHexString(SHA1.HashData(fileBytes)).ToLowerInvariant()));
        Assert.That(upload.MapProof, Is.EqualTo(expectedProof));
        Assert.That(upload.SizeBytes, Is.EqualTo(fileBytes.Length));
        Assert.That(await File.ReadAllBytesAsync(upload.TempFilePath), Is.EqualTo(fileBytes));
    }

    [Test]
    public async Task DisposingTheUpload_DeletesTheTempFile()
    {
        var (body, contentType) = BuildMultipart(MinimalMetadata("x.w3x"), Encoding.UTF8.GetBytes("abc"));

        var upload = await TemporaryMapUploadReader.ReadAsync(body, contentType, CancellationToken.None);
        var path = upload.TempFilePath;
        upload.Dispose();

        Assert.That(File.Exists(path), Is.False);
    }

    [Test]
    public async Task UploadHandleToString_NeverRevealsTheProof()
    {
        // The tracing interceptor tags intercepted-method arguments with arg.ToString().
        var (body, contentType) = BuildMultipart(MinimalMetadata("x.w3x"), Encoding.UTF8.GetBytes("abc"));

        using var upload = await TemporaryMapUploadReader.ReadAsync(body, contentType, CancellationToken.None);

        Assert.That(upload.ToString(), Does.Not.Contain(AbcMapProof));
    }

    [TestCase("x.zip")]
    [TestCase("x.w3x.exe")]
    [TestCase("x")]
    public void RejectsANonMapExtension_With400Extension(string originalFileName)
    {
        var (body, contentType) = BuildMultipart(MinimalMetadata(originalFileName), Encoding.UTF8.GetBytes("abc"));

        var ex = Assert.ThrowsAsync<TemporaryMapUploadException>(
            () => TemporaryMapUploadReader.ReadAsync(body, contentType, CancellationToken.None));

        Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.Status400BadRequest));
        Assert.That(ex.Code, Is.EqualTo("EXTENSION"));
    }

    [TestCase("x.W3X")]
    [TestCase("x.w3m")]
    public async Task AcceptsBothMapExtensions_CaseInsensitively(string originalFileName)
    {
        var (body, contentType) = BuildMultipart(MinimalMetadata(originalFileName), Encoding.UTF8.GetBytes("abc"));

        using var upload = await TemporaryMapUploadReader.ReadAsync(body, contentType, CancellationToken.None);

        Assert.That(upload.Extension, Is.EqualTo(originalFileName.EndsWith("m", StringComparison.OrdinalIgnoreCase) ? ".w3m" : ".w3x"));
    }

    [Test]
    public void RejectsAFileOverTheCap_With413FileTooLarge_AndLeavesNoTempFile()
    {
        var oversized = new byte[1024];
        var (body, contentType) = BuildMultipart(MinimalMetadata("big.w3x"), oversized);

        var before = SnapshotTempFiles();
        var ex = Assert.ThrowsAsync<TemporaryMapUploadException>(
            () => TemporaryMapUploadReader.ReadAsync(body, contentType, CancellationToken.None, maxFileBytes: 512));

        Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.Status413PayloadTooLarge));
        Assert.That(ex.Code, Is.EqualTo("FILE_TOO_LARGE"));
        Assert.That(ex.Message, Is.EqualTo("FILE_TOO_LARGE"));
        Assert.That(JsonSerializer.Serialize(ex.Body), Is.EqualTo("{\"code\":\"FILE_TOO_LARGE\"}"));
        AssertNoNewTempFiles(before);
    }

    [Test]
    public async Task AcceptsAFileOfExactlyTheCap()
    {
        var (body, contentType) = BuildMultipart(MinimalMetadata("x.w3x"), Encoding.UTF8.GetBytes("abc"));

        using var upload = await TemporaryMapUploadReader.ReadAsync(body, contentType, CancellationToken.None, maxFileBytes: 3);

        Assert.That(upload.SizeBytes, Is.EqualTo(3));
    }

    [Test]
    public void TheProductionFileCap_IsMaxFileBytes()
    {
        var capParameter = typeof(TemporaryMapUploadReader)
            .GetMethod(nameof(TemporaryMapUploadReader.ReadAsync))
            .GetParameters()
            .Single(p => p.Name == "maxFileBytes");

        Assert.That(capParameter.DefaultValue, Is.EqualTo(TemporaryMapLimits.MaxFileBytes));
        Assert.That(TemporaryMapLimits.MaxFileBytes, Is.EqualTo(268_435_456));
    }

    [Test]
    public void RejectsAnOversizedMetadataPart_With400Metadata()
    {
        var huge = "{\"originalFileName\":\"" + new string('a', TemporaryMapLimits.MaxMetadataBytes) + ".w3x\"}";
        var (body, contentType) = BuildMultipart(huge, Encoding.UTF8.GetBytes("abc"));

        var ex = Assert.ThrowsAsync<TemporaryMapUploadException>(
            () => TemporaryMapUploadReader.ReadAsync(body, contentType, CancellationToken.None));

        Assert.That(ex.Code, Is.EqualTo("METADATA"));
    }

    [Test]
    public void RejectsAMetadataPartOneByteOverTheCap_EvenWhenItIsValidJson()
    {
        // Valid JSON whose first MaxMetadataBytes + 1 bytes also parse, so only the size cap rejects it.
        var (body, contentType) = BuildMultipart(
            MetadataOfExactly(TemporaryMapLimits.MaxMetadataBytes) + " ", Encoding.UTF8.GetBytes("abc"));

        var ex = Assert.ThrowsAsync<TemporaryMapUploadException>(
            () => TemporaryMapUploadReader.ReadAsync(body, contentType, CancellationToken.None));

        Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.Status400BadRequest));
        Assert.That(ex.Code, Is.EqualTo("METADATA"));
        Assert.That(TemporaryMapLimits.MaxMetadataBytes, Is.EqualTo(65_536));
    }

    [Test]
    public async Task AcceptsAMetadataPartOfExactlyTheCap()
    {
        var (body, contentType) = BuildMultipart(
            MetadataOfExactly(TemporaryMapLimits.MaxMetadataBytes), Encoding.UTF8.GetBytes("abc"));

        using var upload = await TemporaryMapUploadReader.ReadAsync(body, contentType, CancellationToken.None);

        Assert.That(upload.Metadata.OriginalFileName, Is.EqualTo("x.w3x"));
    }

    [Test]
    public void RejectsPartsInTheWrongOrder_With400Metadata()
    {
        var content = new MultipartFormDataContent(Boundary);
        content.Add(new ByteArrayContent(Encoding.UTF8.GetBytes("abc")), "mapFile", "x.w3x");
        content.Add(new StringContent(MinimalMetadata("x.w3x"), Encoding.UTF8, "application/json"), "metadata");
        var (body, contentType) = Materialise(content);

        var ex = Assert.ThrowsAsync<TemporaryMapUploadException>(
            () => TemporaryMapUploadReader.ReadAsync(body, contentType, CancellationToken.None));

        Assert.That(ex.Code, Is.EqualTo("METADATA"));
    }

    [Test]
    public void RejectsAFirstPartNotNamedMetadata_EvenWhenItHoldsValidMetadata()
    {
        var content = new MultipartFormDataContent(Boundary);
        content.Add(new StringContent(MinimalMetadata("x.w3x"), Encoding.UTF8, "application/json"), "meta");
        content.Add(new ByteArrayContent(Encoding.UTF8.GetBytes("abc")), "mapFile", "x.w3x");
        var (body, contentType) = Materialise(content);

        var ex = Assert.ThrowsAsync<TemporaryMapUploadException>(
            () => TemporaryMapUploadReader.ReadAsync(body, contentType, CancellationToken.None));

        Assert.That(ex.Code, Is.EqualTo("METADATA"));
    }

    [Test]
    public void RejectsABodyWithNoParts_With400Metadata()
    {
        var body = new MemoryStream(Encoding.ASCII.GetBytes("--" + Boundary + "--\r\n"));

        var ex = Assert.ThrowsAsync<TemporaryMapUploadException>(
            () => TemporaryMapUploadReader.ReadAsync(body, $"multipart/form-data; boundary={Boundary}", CancellationToken.None));

        Assert.That(ex.Code, Is.EqualTo("METADATA"));
    }

    [Test]
    public void RejectsAMissingMapFilePart_With400Metadata()
    {
        var content = new MultipartFormDataContent(Boundary);
        content.Add(new StringContent(MinimalMetadata("x.w3x"), Encoding.UTF8, "application/json"), "metadata");
        var (body, contentType) = Materialise(content);

        var before = SnapshotTempFiles();
        var ex = Assert.ThrowsAsync<TemporaryMapUploadException>(
            () => TemporaryMapUploadReader.ReadAsync(body, contentType, CancellationToken.None));

        Assert.That(ex.Code, Is.EqualTo("METADATA"));
        AssertNoNewTempFiles(before);
    }

    [Test]
    public void RejectsASecondPartNotNamedMapFile_With400Metadata()
    {
        var content = new MultipartFormDataContent(Boundary);
        content.Add(new StringContent(MinimalMetadata("x.w3x"), Encoding.UTF8, "application/json"), "metadata");
        content.Add(new ByteArrayContent(Encoding.UTF8.GetBytes("abc")), "file", "x.w3x");
        var (body, contentType) = Materialise(content);

        var ex = Assert.ThrowsAsync<TemporaryMapUploadException>(
            () => TemporaryMapUploadReader.ReadAsync(body, contentType, CancellationToken.None));

        Assert.That(ex.Code, Is.EqualTo("METADATA"));
    }

    [TestCase("{not json")]
    [TestCase("")]
    [TestCase("null")]
    [TestCase("[]")]
    [TestCase("{}")]
    [TestCase("{\"originalFileName\":\"   \"}")]
    [TestCase("{\"originalFileName\":\"x.w3x\",\"fileSize\":\"abc\"}")]
    [TestCase("{\"originalFileName\":\"x.w3x\",\"capture\":{\"maxTeams\":99999999999}}")]
    [TestCase("{\"originalFileName\":\"x.w3x\"} trailing")]
    public void RejectsMalformedMetadataJson_With400Metadata(string metadataJson)
    {
        var (body, contentType) = BuildMultipart(metadataJson, Encoding.UTF8.GetBytes("abc"));

        var ex = Assert.ThrowsAsync<TemporaryMapUploadException>(
            () => TemporaryMapUploadReader.ReadAsync(body, contentType, CancellationToken.None));

        Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.Status400BadRequest));
        Assert.That(ex.Code, Is.EqualTo("METADATA"));
    }

    [TestCase("application/json")]
    [TestCase("text/plain; boundary=boundary-1")]
    [TestCase(null)]
    [TestCase("")]
    [TestCase("multipart/form-data")]
    [TestCase("multipart/form-data; boundary=\"\"")]
    [TestCase("multipart/form-data; boundary=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public void RejectsANonMultipartContentType_With400Metadata(string contentType)
    {
        var ex = Assert.ThrowsAsync<TemporaryMapUploadException>(
            () => TemporaryMapUploadReader.ReadAsync(new MemoryStream(), contentType, CancellationToken.None));

        Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.Status400BadRequest));
        Assert.That(ex.Code, Is.EqualTo("METADATA"));
    }

    [Test]
    public async Task AcceptsABoundaryOfTheRfc2046MaximumLength()
    {
        var boundary = new string('a', 70);
        var content = new MultipartFormDataContent(boundary);
        content.Add(new StringContent(MinimalMetadata("x.w3x"), Encoding.UTF8, "application/json"), "metadata");
        content.Add(new ByteArrayContent(Encoding.UTF8.GetBytes("abc")), "mapFile", "upload.bin");
        var (body, contentType) = Materialise(content);

        using var upload = await TemporaryMapUploadReader.ReadAsync(body, contentType, CancellationToken.None);

        Assert.That(upload.Sha1, Is.EqualTo(AbcSha1));
    }

    [TestCase("Content-Type application/json")]
    [TestCase("X-0: 0\r\nX-1: 1\r\nX-2: 2\r\nX-3: 3\r\nX-4: 4\r\nX-5: 5\r\nX-6: 6\r\nX-7: 7\r\nX-8: 8\r\nX-9: 9\r\nX-10: 10\r\nX-11: 11\r\nX-12: 12\r\nX-13: 13\r\nX-14: 14\r\nX-15: 15\r\nX-16: 16")]
    public void RejectsMultipartHeadersTheMultipartReaderRefuses_With400Metadata(string extraHeaderLines)
    {
        var raw =
            "--" + Boundary + "\r\n" +
            "Content-Disposition: form-data; name=metadata\r\n" +
            extraHeaderLines + "\r\n" +
            "\r\n" +
            MinimalMetadata("x.w3x") + "\r\n" +
            "--" + Boundary + "\r\n" +
            "Content-Disposition: form-data; name=mapFile\r\n" +
            "\r\n" +
            "abc\r\n" +
            "--" + Boundary + "--\r\n";

        var ex = Assert.ThrowsAsync<TemporaryMapUploadException>(
            () => TemporaryMapUploadReader.ReadAsync(
                new MemoryStream(Encoding.ASCII.GetBytes(raw)), $"multipart/form-data; boundary={Boundary}", CancellationToken.None));

        Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.Status400BadRequest));
        Assert.That(ex.Code, Is.EqualTo("METADATA"));
    }

    [Test]
    public void ABodyThatEndsMidFile_SurfacesTheReadError_AndLeavesNoTempFile()
    {
        var (full, contentType) = BuildMultipartBytes(MinimalMetadata("x.w3x"), new byte[100_000]);
        var body = new InterruptedBodyStream(full, interruptAtByte: 60_000, failure: null);

        var before = SnapshotTempFiles();
        Assert.ThrowsAsync<IOException>(
            () => TemporaryMapUploadReader.ReadAsync(body, contentType, CancellationToken.None));

        AssertASpoolExistedAndWasDeleted(before, body);
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
        var body = new InterruptedBodyStream(full, interruptAtByte: 60_000, failure: () =>
        {
            if (thrown is OperationCanceledException)
            {
                cts.Cancel();
            }

            return thrown;
        });

        var before = SnapshotTempFiles();
        var ex = Assert.CatchAsync(() => TemporaryMapUploadReader.ReadAsync(body, contentType, cts.Token));

        Assert.That(ex, Is.SameAs(thrown), "the reader must not reclassify transport failures; the controller does");
        AssertASpoolExistedAndWasDeleted(before, body);
    }

    [Test]
    public void BodyLimitFilter_RaisesTheLimitToTheTransportCeiling()
    {
        var httpContext = new DefaultHttpContext();
        var feature = new FakeMaxBodySizeFeature();
        httpContext.Features.Set<IHttpMaxRequestBodySizeFeature>(feature);

        new TemporaryMapUploadBodyLimitAttribute().OnResourceExecuting(
            CreateResourceContext(httpContext, new List<IValueProviderFactory>()));

        Assert.That(feature.MaxRequestBodySize, Is.EqualTo(TemporaryMapLimits.TransportBodyBytes));
        Assert.That(TemporaryMapLimits.TransportBodyBytes, Is.EqualTo(269_484_032));
        Assert.That(TemporaryMapLimits.TransportBodyBytes - TemporaryMapLimits.MaxFileBytes, Is.EqualTo(1024 * 1024));
    }

    [Test]
    public void BodyLimitFilter_LeavesAReadOnlyLimitUntouched()
    {
        // Kestrel makes the feature read-only once the body has started to be read; its setter then throws.
        var httpContext = new DefaultHttpContext();
        var feature = new FakeMaxBodySizeFeature { IsReadOnly = true };
        httpContext.Features.Set<IHttpMaxRequestBodySizeFeature>(feature);

        Assert.DoesNotThrow(() => new TemporaryMapUploadBodyLimitAttribute().OnResourceExecuting(
            CreateResourceContext(httpContext, new List<IValueProviderFactory>())));

        Assert.That(feature.MaxRequestBodySize, Is.EqualTo(FakeMaxBodySizeFeature.KestrelGlobalLimit));
    }

    [Test]
    public void BodyLimitFilter_ToleratesAServerWithoutTheFeature()
    {
        var httpContext = new DefaultHttpContext();

        Assert.DoesNotThrow(() => new TemporaryMapUploadBodyLimitAttribute().OnResourceExecuting(
            CreateResourceContext(httpContext, new List<IValueProviderFactory>())));
    }

    [Test]
    public void DisableFormValueModelBinding_RemovesEveryFormValueProvider()
    {
        var factories = new List<IValueProviderFactory>
        {
            new FormValueProviderFactory(),
            new FormFileValueProviderFactory(),
            new JQueryFormValueProviderFactory(),
            new QueryStringValueProviderFactory(),
        };

        new DisableFormValueModelBindingAttribute().OnResourceExecuting(
            CreateResourceContext(new DefaultHttpContext(), factories));

        Assert.That(factories, Has.Count.EqualTo(1));
        Assert.That(factories[0], Is.InstanceOf<QueryStringValueProviderFactory>());
    }

    private static string MinimalMetadata(string originalFileName)
        => "{\"sha1\":\"" + AbcSha1 + "\",\"originalFileName\":\"" + originalFileName + "\",\"fileSize\":3}";

    /// <summary>A valid metadata JSON document of exactly <paramref name="byteCount"/> UTF-8 bytes.</summary>
    private static string MetadataOfExactly(int byteCount)
    {
        const string head = "{\"sha1\":\"" + AbcSha1 + "\",\"originalFileName\":\"x.w3x\",\"fileSize\":3,\"pad\":\"";
        const string tail = "\"}";
        var json = head + new string('p', byteCount - head.Length - tail.Length) + tail;
        Assert.That(Encoding.UTF8.GetByteCount(json), Is.EqualTo(byteCount));
        return json;
    }

    private static (Stream Body, string ContentType) BuildMultipart(string metadataJson, byte[] fileBytes)
    {
        var (bytes, contentType) = BuildMultipartBytes(metadataJson, fileBytes);
        return (new MemoryStream(bytes), contentType);
    }

    private static (byte[] Bytes, string ContentType) BuildMultipartBytes(string metadataJson, byte[] fileBytes)
    {
        var content = new MultipartFormDataContent(Boundary);
        content.Add(new StringContent(metadataJson, Encoding.UTF8, "application/json"), "metadata");
        content.Add(new ByteArrayContent(fileBytes), "mapFile", "upload.bin");
        var (body, contentType) = Materialise(content);
        return (((MemoryStream)body).ToArray(), contentType);
    }

    private static (Stream Body, string ContentType) Materialise(MultipartFormDataContent content)
    {
        var buffer = new MemoryStream();
        content.CopyTo(buffer, null, CancellationToken.None);
        buffer.Position = 0;
        return (buffer, content.Headers.ContentType.ToString());
    }

    private static ResourceExecutingContext CreateResourceContext(
        HttpContext httpContext, IList<IValueProviderFactory> valueProviderFactories)
        => new(
            new ActionContext(httpContext, new RouteData(), new ActionDescriptor()),
            new List<IFilterMetadata>(),
            valueProviderFactories);

    private static HashSet<string> SnapshotTempFiles()
        => Directory.Exists(TemporaryMapLimits.TempUploadDir)
            ? new HashSet<string>(Directory.GetFiles(TemporaryMapLimits.TempUploadDir))
            : new HashSet<string>();

    private static void AssertNoNewTempFiles(HashSet<string> before)
    {
        var residual = SnapshotTempFiles();
        residual.ExceptWith(before);
        Assert.That(residual, Is.Empty, "the partial spool must be cleaned up");
    }

    private static void AssertASpoolExistedAndWasDeleted(HashSet<string> before, InterruptedBodyStream body)
    {
        var spooledAtInterruption = new HashSet<string>(body.TempFilesAtInterruption ?? []);
        spooledAtInterruption.ExceptWith(before);
        Assert.That(spooledAtInterruption, Is.Not.Empty, "the interruption must hit while a spool file exists");
        Assert.That(spooledAtInterruption.Where(File.Exists), Is.Empty, "the partial spool must be cleaned up");
        AssertNoNewTempFiles(before);
    }

    private sealed class FakeMaxBodySizeFeature : IHttpMaxRequestBodySizeFeature
    {
        public const long KestrelGlobalLimit = 0x8000000;

        private long? _maxRequestBodySize = KestrelGlobalLimit;

        public bool IsReadOnly { get; init; }

        public long? MaxRequestBodySize
        {
            get => _maxRequestBodySize;
            set => _maxRequestBodySize = IsReadOnly
                ? throw new InvalidOperationException("The maximum request body size cannot be modified after the app has already started reading the request body.")
                : value;
        }
    }

    /// <summary>
    /// Serves <c>data</c> up to <c>interruptAtByte</c>, then either ends cleanly (<c>failure</c> null) or
    /// throws the exception <c>failure</c> returns — a truncated body or a transport failure mid-read.
    /// </summary>
    private sealed class InterruptedBodyStream(byte[] data, int interruptAtByte, Func<Exception> failure) : Stream
    {
        private int _position;

        public HashSet<string> TempFilesAtInterruption { get; private set; }

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
                TempFilesAtInterruption ??= SnapshotTempFiles();
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
