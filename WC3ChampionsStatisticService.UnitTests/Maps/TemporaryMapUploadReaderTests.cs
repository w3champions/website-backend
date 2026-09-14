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
using NUnit.Framework;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using W3ChampionsStatisticService.Maps;

namespace WC3ChampionsStatisticService.Tests.Maps;

[TestFixture]
public class TemporaryMapUploadReaderTests
{
    private const string AbcMapProof = "3b2724682f427727f8fa19236e04d3a5e3bdf218b7fe9c259c202e5264edbf4e";
    private const string AbcSha1 = "a9993e364706816aba3e25717850c26c9cd0d89d";
    private const string Boundary = "boundary-1";

    private string _testRoot;
    private string _spoolDirectory;

    [SetUp]
    public void SetUp()
    {
        // A unique spool directory per test: the machine-wide TempUploadDir is shared with every other
        // process, so assertions about its contents would be neither reliable nor meaningful.
        _testRoot = Path.Combine(Path.GetTempPath(), "w3c-map-upload-tests", Guid.NewGuid().ToString("N"));
        _spoolDirectory = Path.Combine(_testRoot, "spool");
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_testRoot))
        {
            Directory.Delete(_testRoot, recursive: true);
        }
    }

    [Test]
    public async Task ReadsMetadataAndSpoolsFile_ComputingSha1AndProofInOnePass()
    {
        var (body, contentType) = BuildMultipart(
            "{\"sha1\":\"" + AbcSha1 + "\",\"originalFileName\":\"Legion TD (X3).w3x\",\"fileSize\":3," +
            "\"capture\":{\"lobbyMode\":\"mapped-forces\",\"maxTeams\":2,\"slotCount\":16,\"mappedForces\":[]," +
            "\"launcherVersion\":\"3.4.0\",\"capturedAt\":\"2026-09-14T09:10:39Z\"}}",
            Encoding.UTF8.GetBytes("abc"));

        using var upload = await Read(body, contentType);

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

        using var upload = await Read(body, contentType);

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

        var upload = await Read(body, contentType);
        var path = upload.TempFilePath;
        upload.Dispose();

        Assert.That(File.Exists(path), Is.False);
    }

    [Test]
    public async Task UploadHandleToString_NeverRevealsTheProof()
    {
        // The tracing interceptor tags intercepted-method arguments with arg.ToString().
        var (body, contentType) = BuildMultipart(MinimalMetadata("x.w3x"), Encoding.UTF8.GetBytes("abc"));

        using var upload = await Read(body, contentType);

        Assert.That(upload.ToString(), Does.Not.Contain(AbcMapProof));
    }

    [TestCase("x.zip")]
    [TestCase("x.w3x.exe")]
    [TestCase("x")]
    public void RejectsANonMapExtension_With400Extension(string originalFileName)
    {
        var (body, contentType) = BuildMultipart(MinimalMetadata(originalFileName), Encoding.UTF8.GetBytes("abc"));

        var ex = Assert.ThrowsAsync<TemporaryMapUploadException>(
            () => Read(body, contentType));

        Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.Status400BadRequest));
        Assert.That(ex.Code, Is.EqualTo("EXTENSION"));
    }

    [TestCase("x.W3X")]
    [TestCase("x.w3m")]
    public async Task AcceptsBothMapExtensions_CaseInsensitively(string originalFileName)
    {
        var (body, contentType) = BuildMultipart(MinimalMetadata(originalFileName), Encoding.UTF8.GetBytes("abc"));

        using var upload = await Read(body, contentType);

        Assert.That(upload.Extension, Is.EqualTo(originalFileName.EndsWith("m", StringComparison.OrdinalIgnoreCase) ? ".w3m" : ".w3x"));
    }

    [Test]
    public void RejectsAFileOverTheCap_With413FileTooLarge_AndLeavesNoTempFile()
    {
        var oversized = new byte[1024];
        var (body, contentType) = BuildMultipart(MinimalMetadata("big.w3x"), oversized);

        var ex = Assert.ThrowsAsync<TemporaryMapUploadException>(
            () => Read(body, contentType, maxFileBytes: 512));

        Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.Status413PayloadTooLarge));
        Assert.That(ex.Code, Is.EqualTo("FILE_TOO_LARGE"));
        Assert.That(ex.Message, Is.EqualTo("FILE_TOO_LARGE"));
        Assert.That(JsonSerializer.Serialize(ex.Body), Is.EqualTo("{\"code\":\"FILE_TOO_LARGE\"}"));
        AssertSpoolDirectoryHasNoFiles();
    }

    [Test]
    public async Task AcceptsAFileOfExactlyTheCap()
    {
        var (body, contentType) = BuildMultipart(MinimalMetadata("x.w3x"), Encoding.UTF8.GetBytes("abc"));

        using var upload = await Read(body, contentType, maxFileBytes: 3);

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
            () => Read(body, contentType));

        Assert.That(ex.Code, Is.EqualTo("METADATA"));
    }

    [Test]
    public void RejectsAMetadataPartOneByteOverTheCap_EvenWhenItIsValidJson()
    {
        // Valid JSON whose first MaxMetadataBytes + 1 bytes also parse, so only the size cap rejects it.
        var (body, contentType) = BuildMultipart(
            MetadataOfExactly(TemporaryMapLimits.MaxMetadataBytes) + " ", Encoding.UTF8.GetBytes("abc"));

        var ex = Assert.ThrowsAsync<TemporaryMapUploadException>(
            () => Read(body, contentType));

        Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.Status400BadRequest));
        Assert.That(ex.Code, Is.EqualTo("METADATA"));
        Assert.That(TemporaryMapLimits.MaxMetadataBytes, Is.EqualTo(65_536));
    }

    [Test]
    public async Task AcceptsAMetadataPartOfExactlyTheCap()
    {
        var (body, contentType) = BuildMultipart(
            MetadataOfExactly(TemporaryMapLimits.MaxMetadataBytes), Encoding.UTF8.GetBytes("abc"));

        using var upload = await Read(body, contentType);

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
            () => Read(body, contentType));

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
            () => Read(body, contentType));

        Assert.That(ex.Code, Is.EqualTo("METADATA"));
    }

    [Test]
    public void RejectsABodyWithNoParts_With400Metadata()
    {
        var body = new MemoryStream(Encoding.ASCII.GetBytes("--" + Boundary + "--\r\n"));

        var ex = Assert.ThrowsAsync<TemporaryMapUploadException>(
            () => Read(body, $"multipart/form-data; boundary={Boundary}"));

        Assert.That(ex.Code, Is.EqualTo("METADATA"));
    }

    [Test]
    public void RejectsAMissingMapFilePart_With400Metadata()
    {
        var content = new MultipartFormDataContent(Boundary);
        content.Add(new StringContent(MinimalMetadata("x.w3x"), Encoding.UTF8, "application/json"), "metadata");
        var (body, contentType) = Materialise(content);

        var ex = Assert.ThrowsAsync<TemporaryMapUploadException>(
            () => Read(body, contentType));

        Assert.That(ex.Code, Is.EqualTo("METADATA"));
        AssertSpoolDirectoryHasNoFiles();
    }

    [Test]
    public void RejectsASecondPartNotNamedMapFile_With400Metadata()
    {
        var content = new MultipartFormDataContent(Boundary);
        content.Add(new StringContent(MinimalMetadata("x.w3x"), Encoding.UTF8, "application/json"), "metadata");
        content.Add(new ByteArrayContent(Encoding.UTF8.GetBytes("abc")), "file", "x.w3x");
        var (body, contentType) = Materialise(content);

        var ex = Assert.ThrowsAsync<TemporaryMapUploadException>(
            () => Read(body, contentType));

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
            () => Read(body, contentType));

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
            () => Read(new MemoryStream(), contentType));

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

        using var upload = await Read(body, contentType);

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
            () => Read(
                new MemoryStream(Encoding.ASCII.GetBytes(raw)), $"multipart/form-data; boundary={Boundary}"));

        Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.Status400BadRequest));
        Assert.That(ex.Code, Is.EqualTo("METADATA"));
    }

    [Test]
    public void ABodyThatEndsMidFile_SurfacesTheReadError_AndLeavesNoTempFile()
    {
        var (full, contentType) = BuildMultipartBytes(MinimalMetadata("x.w3x"), new byte[100_000]);
        var body = new InterruptedBodyStream(full, interruptAtByte: 60_000, _spoolDirectory, failure: null);

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
        var body = new InterruptedBodyStream(full, interruptAtByte: 60_000, _spoolDirectory, failure: () =>
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
    public async Task ThePublicOverload_SpoolsIntoTempUploadDir_WithTheCallersCap()
    {
        // Production wiring only; nothing here asserts on the shared directory's other contents.
        var (body, contentType) = BuildMultipart(MinimalMetadata("x.w3x"), Encoding.UTF8.GetBytes("abc"));
        using (var upload = await TemporaryMapUploadReader.ReadAsync(body, contentType, CancellationToken.None, maxFileBytes: 3))
        {
            Assert.That(Path.GetDirectoryName(upload.TempFilePath), Is.EqualTo(TemporaryMapLimits.TempUploadDir));
        }

        var (oversizedBody, oversizedContentType) = BuildMultipart(MinimalMetadata("x.w3x"), Encoding.UTF8.GetBytes("abc"));
        var ex = Assert.ThrowsAsync<TemporaryMapUploadException>(
            () => TemporaryMapUploadReader.ReadAsync(oversizedBody, oversizedContentType, CancellationToken.None, maxFileBytes: 2));

        Assert.That(ex.Code, Is.EqualTo("FILE_TOO_LARGE"));
    }

    [Test]
    public void ASpoolDirectoryPathHeldByAFile_IsASpoolFault_NotAnIOException()
    {
        Directory.CreateDirectory(_testRoot);
        File.WriteAllText(_spoolDirectory, "a regular file where the spool directory should be");
        var (body, contentType) = BuildMultipart(MinimalMetadata("x.w3x"), Encoding.UTF8.GetBytes("abc"));

        var ex = Assert.ThrowsAsync<TemporaryMapSpoolException>(() => Read(body, contentType));

        Assert.That(ex.InnerException, Is.InstanceOf<IOException>());
        Assert.That(FilesIn(_testRoot), Is.EqualTo(new[] { _spoolDirectory }), "nothing may be spooled");
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
        var body = new InterruptedBodyStream(full, interruptAtByte: 60_000, _spoolDirectory, failure: () => bodyFailure);

        var ex = Assert.CatchAsync(() => Read(body, contentType,
            openSpoolFile: path => new FaultingSpoolStream(File.Create(path), SpoolFailurePoint.Close, SpoolFailure("disk"))));

        Assert.That(ex, Is.SameAs(bodyFailure), "a close failure while abandoning the spool must not hide the body failure");
        AssertASpoolExistedAndWasDeleted(body);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ASpoolDirectoryThatIsALink_IsRefused(bool withTrailingSeparator)
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Ignore("Creating a directory symbolic link needs elevation on Windows.");
            return;
        }

        var target = Path.Combine(_testRoot, "elsewhere");
        Directory.CreateDirectory(target);
        Directory.CreateSymbolicLink(_spoolDirectory, target);
        var spoolDirectory = withTrailingSeparator ? _spoolDirectory + Path.DirectorySeparatorChar : _spoolDirectory;
        var (body, contentType) = BuildMultipart(MinimalMetadata("x.w3x"), Encoding.UTF8.GetBytes("abc"));

        Assert.ThrowsAsync<TemporaryMapSpoolException>(() => TemporaryMapUploadReader.ReadAsync(
            body, contentType, spoolDirectory, TemporaryMapLimits.MaxFileBytes, CancellationToken.None));

        Assert.That(FilesIn(target), Is.Empty, "nothing may be spooled through the link");
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

        Assert.That(File.GetUnixFileMode(_spoolDirectory),
            Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute));
        Assert.That(File.GetUnixFileMode(upload.TempFilePath),
            Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite));
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

    private Task<TemporaryMapUpload> Read(
        Stream body,
        string contentType,
        long maxFileBytes = TemporaryMapLimits.MaxFileBytes,
        Func<string, Stream> openSpoolFile = null,
        CancellationToken cancellationToken = default)
        => TemporaryMapUploadReader.ReadAsync(body, contentType, _spoolDirectory, maxFileBytes, cancellationToken, openSpoolFile);

    private static Exception SpoolFailure(string failureKind)
        => failureKind == "disk"
            ? new IOException("No space left on device")
            : new UnauthorizedAccessException("Access to the path is denied.");

    private static string[] FilesIn(string directory)
        => Directory.Exists(directory) ? Directory.GetFiles(directory) : [];

    private void AssertSpoolDirectoryHasNoFiles()
        => Assert.That(FilesIn(_spoolDirectory), Is.Empty, "the partial spool must be cleaned up");

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

    private enum SpoolFailurePoint
    {
        Write,
        Close,
    }

    /// <summary>A spool file that fails on its first write, or when it is closed, with the given exception.</summary>
    private sealed class FaultingSpoolStream(Stream inner, SpoolFailurePoint failAt, Exception failure) : Stream
    {
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
            await inner.DisposeAsync();
            ThrowIfFailingAt(SpoolFailurePoint.Close);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
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
