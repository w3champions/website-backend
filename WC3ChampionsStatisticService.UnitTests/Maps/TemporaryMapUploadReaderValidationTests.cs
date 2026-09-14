using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using NUnit.Framework;
using W3ChampionsStatisticService.Maps;

namespace WC3ChampionsStatisticService.Tests.Maps;

/// <summary>
/// What the reader rejects and where each limit sits: the map extension, the file and metadata caps, the
/// body's multipart shape and the metadata JSON.
/// </summary>
[TestFixture]
public class TemporaryMapUploadReaderValidationTests : TemporaryMapUploadReaderTestBase
{
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
    public void RejectsAFileOverTheCapInTotal_EvenWhenEveryReadIsUnderIt()
    {
        // Each read is at most 80 KiB, so only the running total can cross a 200 000-byte cap.
        var (body, contentType) = BuildMultipart(MinimalMetadata("big.w3x"), new byte[300_000]);
        string spoolPath = null;

        var ex = Assert.ThrowsAsync<TemporaryMapUploadException>(() => Read(body, contentType, maxFileBytes: 200_000,
            openSpoolFile: path => File.Create(spoolPath = path)));

        Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.Status413PayloadTooLarge));
        Assert.That(ex.Code, Is.EqualTo("FILE_TOO_LARGE"));
        Assert.That(spoolPath, Is.Not.Null, "the spool file must have been created before the cap was crossed");
        AssertSpoolDirectoryHasNoFiles();
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

    [Test]
    public async Task AcceptsMetadataNestedToTheDepthLimit()
    {
        var (body, contentType) = BuildMultipart(MetadataNestedTo(32), Encoding.UTF8.GetBytes("abc"));

        using var upload = await Read(body, contentType);

        Assert.That(upload.Metadata.OriginalFileName, Is.EqualTo("x.w3x"));
    }

    [Test]
    public void RejectsMetadataNestedPastTheDepthLimit_With400Metadata()
    {
        var (body, contentType) = BuildMultipart(MetadataNestedTo(33), Encoding.UTF8.GetBytes("abc"));

        var ex = Assert.ThrowsAsync<TemporaryMapUploadException>(() => Read(body, contentType));

        Assert.That(ex.Code, Is.EqualTo("METADATA"));
    }

    [TestCase("{\"$ref\":\"1\",\"originalFileName\":\"x.w3x\"}")]
    [TestCase("{\"$type\":\"System.Uri, System.Private.Uri\",\"originalFileName\":\"x.w3x\"}")]
    public async Task JsonReferenceAndTypeProperties_AreIgnoredLikeAnyUnknownMember(string metadataJson)
    {
        var (body, contentType) = BuildMultipart(metadataJson, Encoding.UTF8.GetBytes("abc"));

        using var upload = await Read(body, contentType);

        Assert.That(upload.Metadata, Is.TypeOf<TemporaryMapUploadMetadata>());
        Assert.That(upload.Metadata.OriginalFileName, Is.EqualTo("x.w3x"));
    }

    [Test]
    public async Task GlobalJsonDefaults_DoNotReachTheMetadataParser()
    {
        var previousDefaults = Newtonsoft.Json.JsonConvert.DefaultSettings;
        Newtonsoft.Json.JsonConvert.DefaultSettings = () => new Newtonsoft.Json.JsonSerializerSettings
        {
            Converters = { new RefusingMetadataConverter() },
        };
        try
        {
            var (body, contentType) = BuildMultipart(MinimalMetadata("x.w3x"), Encoding.UTF8.GetBytes("abc"));

            using var upload = await Read(body, contentType);

            Assert.That(upload.Metadata.OriginalFileName, Is.EqualTo("x.w3x"));
        }
        finally
        {
            Newtonsoft.Json.JsonConvert.DefaultSettings = previousDefaults;
        }
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

    /// <summary>Valid metadata whose deepest value sits inside <paramref name="containers"/> JSON containers, the root object included.</summary>
    private static string MetadataNestedTo(int containers)
        => "{\"originalFileName\":\"x.w3x\",\"pad\":" + new string('[', containers - 1) + new string(']', containers - 1) + "}";

    /// <summary>A valid metadata JSON document of exactly <paramref name="byteCount"/> UTF-8 bytes.</summary>
    private static string MetadataOfExactly(int byteCount)
    {
        const string head = "{\"sha1\":\"" + AbcSha1 + "\",\"originalFileName\":\"x.w3x\",\"fileSize\":3,\"pad\":\"";
        const string tail = "\"}";
        var json = head + new string('p', byteCount - head.Length - tail.Length) + tail;
        Assert.That(Encoding.UTF8.GetByteCount(json), Is.EqualTo(byteCount));
        return json;
    }

    /// <summary>Stands in for a global Json.NET default that would change how metadata is read.</summary>
    private sealed class RefusingMetadataConverter : Newtonsoft.Json.JsonConverter
    {
        public override bool CanConvert(Type objectType) => objectType == typeof(TemporaryMapUploadMetadata);

        public override object ReadJson(
            Newtonsoft.Json.JsonReader reader, Type objectType, object existingValue, Newtonsoft.Json.JsonSerializer serializer)
            => throw new Newtonsoft.Json.JsonSerializationException("A global default converter reached the metadata parser.");

        public override void WriteJson(Newtonsoft.Json.JsonWriter writer, object value, Newtonsoft.Json.JsonSerializer serializer)
            => throw new NotSupportedException();
    }
}
