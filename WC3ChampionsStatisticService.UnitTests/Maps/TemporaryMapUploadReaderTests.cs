using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using W3ChampionsStatisticService.Maps;

namespace WC3ChampionsStatisticService.Tests.Maps;

/// <summary>A well-formed upload: what is spooled, the digests computed on the way, and the upload handle.</summary>
[TestFixture]
public class TemporaryMapUploadReaderTests : TemporaryMapUploadReaderTestBase
{
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
}
