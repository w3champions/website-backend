using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using NUnit.Framework;
using W3ChampionsStatisticService.NetworkDiagnostics.Submission;

namespace WC3ChampionsStatisticService.Tests.NetworkDiagnostics;

[TestFixture]
public class SubmissionBodyReaderTests
{
    private const int Cap = SubmissionValidator.MaxBodyBytes;

    private static Task<(NetDiagSubmission Dto, string Error)> Read(Stream body) =>
        SubmissionBodyReader.ReadAsync(body, Cap, CancellationToken.None);

    private static Stream Plain(string body) => new MemoryStream(Encoding.UTF8.GetBytes(body));

    private static byte[] Gzip(byte[] content)
    {
        var compressed = new MemoryStream();
        using (var gzip = new GZipStream(compressed, CompressionLevel.Fastest, leaveOpen: true)) gzip.Write(content);
        return compressed.ToArray();
    }

    // What the request-decompression middleware hands the controller for Content-Encoding: gzip.
    private static Stream Gunzip(byte[] wire) => new GZipStream(new MemoryStream(wire), CompressionMode.Decompress);

    [Test]
    public async Task ValidJson_Parses()
    {
        var (dto, error) = await Read(Plain(SubmissionFixtures.FullJson));

        Assert.IsNull(error);
        Assert.AreEqual(SubmissionFixtures.MeasurementId, dto.MeasurementId);
    }

    [Test]
    public async Task GzipBody_IsReadThroughTheDecompressionStream()
    {
        var (dto, error) = await Read(Gunzip(Gzip(Encoding.UTF8.GetBytes(SubmissionFixtures.LauncherProducedJson))));

        Assert.IsNull(error);
        Assert.AreEqual(SubmissionFixtures.MeasurementId, dto.MeasurementId);
        Assert.AreEqual(2, dto.Targets.Count);
    }

    [TestCase("", "Missing body")]
    [TestCase("null", "Missing body")]
    [TestCase("{\"measurement_id\": ", "Malformed JSON")]
    [TestCase("{\"restarts\": \"many\"}", "Malformed JSON")]
    [TestCase("[]", "Malformed JSON")]
    public async Task BadBodies(string body, string expected)
    {
        var (dto, error) = await Read(Plain(body));

        Assert.IsNull(dto);
        Assert.AreEqual(expected, error);
    }

    // Content-Encoding: gzip on a body that is not gzip: the decompression stream throws on the first read.
    [Test]
    public async Task NotGzipBehindAGzipStream_IsA400Error_NotAnException()
    {
        var (dto, error) = await Read(Gunzip(Encoding.UTF8.GetBytes(SubmissionFixtures.FullJson)));

        Assert.IsNull(dto);
        Assert.AreEqual("Malformed JSON", error);
    }

    [TestCase(2)]    // cut in the middle of the deflate data
    [TestCase(10)]   // only the gzip header and a little data
    public async Task TruncatedGzip_IsA400Error_NotAnException(int keepOneIn)
    {
        var wire = Gzip(Encoding.UTF8.GetBytes(SubmissionFixtures.FullJson));

        var (dto, error) = await Read(Gunzip(wire[..(wire.Length / keepOneIn)]));

        Assert.IsNull(dto);
        Assert.AreEqual("Malformed JSON", error);
    }

    [Test]
    public async Task GzipBodyLargerThanCap_IsRejectedWithoutReadingItAll()
    {
        // 64 MiB of zeros compresses to ~64 KiB: a cheap decompression bomb.
        var compressed = new MemoryStream();
        await using (var gzip = new GZipStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
        {
            var zeros = new byte[1024 * 1024];
            for (var i = 0; i < 64; i++) await gzip.WriteAsync(zeros);
        }
        compressed.Position = 0;
        var decompressed = new CountingStream(new GZipStream(compressed, CompressionMode.Decompress));

        var (dto, error) = await Read(decompressed);

        Assert.IsNull(dto);
        Assert.AreEqual("payload_too_large", error);
        Assert.AreEqual(Cap + 1, decompressed.BytesRead, "reading stops one byte past the cap");
    }

    [Test]
    public async Task ExactlyAtTheCap_IsAccepted_AsFarAsSizeGoes()
    {
        var (_, error) = await Read(new MemoryStream(JsonOfLength(Cap)));

        Assert.IsNull(error);
    }

    [Test]
    public async Task OneByteOverTheCap_IsRejected()
    {
        var (dto, error) = await Read(new MemoryStream(JsonOfLength(Cap + 1)));

        Assert.IsNull(dto);
        Assert.AreEqual("payload_too_large", error);
    }

    // The server's own request size limit (compressed bytes) surfaces as a 413 exception from the body stream.
    [Test]
    public async Task ServerRequestSizeLimit_IsReportedAsPayloadTooLarge()
    {
        var (dto, error) = await Read(new ThrowingStream(new BadHttpRequestException("Request body too large.", StatusCodes.Status413PayloadTooLarge)));

        Assert.IsNull(dto);
        Assert.AreEqual("payload_too_large", error);
    }

    // Anything else the server reports about the request (body timeout 408, bad chunking 400) keeps its own status.
    [Test]
    public void OtherBadRequestErrors_AreNotSwallowed()
    {
        var timeout = new BadHttpRequestException("Reading the request body timed out.", StatusCodes.Status408RequestTimeout);

        var thrown = Assert.ThrowsAsync<BadHttpRequestException>(() => Read(new ThrowingStream(timeout)));

        Assert.AreEqual(StatusCodes.Status408RequestTimeout, thrown.StatusCode);
    }

    private static byte[] JsonOfLength(int length)
    {
        var body = new byte[length];
        Array.Fill(body, (byte)' ');
        body[0] = (byte)'{';
        body[^1] = (byte)'}';
        return body;
    }

    private sealed class ThrowingStream(Exception error) : ReadOnlyStream
    {
        public override int Read(byte[] buffer, int offset, int count) => throw error;
    }

    private sealed class CountingStream(Stream inner) : ReadOnlyStream
    {
        public long BytesRead { get; private set; }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var n = inner.Read(buffer, offset, count);
            BytesRead += n;
            return n;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var n = await inner.ReadAsync(buffer, cancellationToken);
            BytesRead += n;
            return n;
        }
    }

    private abstract class ReadOnlyStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
