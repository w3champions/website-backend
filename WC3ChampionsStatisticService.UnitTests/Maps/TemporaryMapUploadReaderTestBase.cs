using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using W3ChampionsStatisticService.Maps;

namespace WC3ChampionsStatisticService.Tests.Maps;

/// <summary>
/// Shared by the <see cref="TemporaryMapUploadReader"/> fixtures: a private spool directory per test
/// (the machine-wide TempUploadDir is shared with every other process), multipart body builders, and the
/// reader call and cleanup assertion that use that directory.
/// </summary>
public abstract class TemporaryMapUploadReaderTestBase
{
    protected const string AbcMapProof = "3b2724682f427727f8fa19236e04d3a5e3bdf218b7fe9c259c202e5264edbf4e";
    protected const string AbcSha1 = "a9993e364706816aba3e25717850c26c9cd0d89d";
    protected const string Boundary = "boundary-1";

    protected string TestRoot { get; private set; }

    protected string SpoolDirectory { get; private set; }

    [SetUp]
    public void SetUp()
    {
        // A unique spool directory per test: the machine-wide TempUploadDir is shared with every other
        // process, so assertions about its contents would be neither reliable nor meaningful.
        TestRoot = Path.Combine(Path.GetTempPath(), "w3c-map-upload-tests", Guid.NewGuid().ToString("N"));
        SpoolDirectory = Path.Combine(TestRoot, "spool");
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(TestRoot))
        {
            Directory.Delete(TestRoot, recursive: true);
        }
    }

    protected Task<TemporaryMapUpload> Read(
        Stream body,
        string contentType,
        long maxFileBytes = TemporaryMapLimits.MaxFileBytes,
        Func<string, Stream> openSpoolFile = null,
        CancellationToken cancellationToken = default)
        => TemporaryMapUploadReader.ReadAsync(body, contentType, SpoolDirectory, maxFileBytes, cancellationToken, openSpoolFile);

    protected static string MinimalMetadata(string originalFileName)
        => "{\"sha1\":\"" + AbcSha1 + "\",\"originalFileName\":\"" + originalFileName + "\",\"fileSize\":3}";

    protected static (Stream Body, string ContentType) BuildMultipart(string metadataJson, byte[] fileBytes)
    {
        var (bytes, contentType) = BuildMultipartBytes(metadataJson, fileBytes);
        return (new MemoryStream(bytes), contentType);
    }

    protected static (byte[] Bytes, string ContentType) BuildMultipartBytes(string metadataJson, byte[] fileBytes)
    {
        var content = new MultipartFormDataContent(Boundary);
        content.Add(new StringContent(metadataJson, Encoding.UTF8, "application/json"), "metadata");
        content.Add(new ByteArrayContent(fileBytes), "mapFile", "upload.bin");
        var (body, contentType) = Materialise(content);
        return (((MemoryStream)body).ToArray(), contentType);
    }

    protected static (Stream Body, string ContentType) Materialise(MultipartFormDataContent content)
    {
        var buffer = new MemoryStream();
        content.CopyTo(buffer, null, CancellationToken.None);
        buffer.Position = 0;
        return (buffer, content.Headers.ContentType.ToString());
    }

    protected static string[] FilesIn(string directory)
        => Directory.Exists(directory) ? Directory.GetFiles(directory) : [];

    protected void AssertSpoolDirectoryHasNoFiles()
        => Assert.That(FilesIn(SpoolDirectory), Is.Empty, "the partial spool must be cleaned up");
}
