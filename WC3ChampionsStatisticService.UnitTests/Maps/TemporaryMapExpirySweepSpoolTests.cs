using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using W3C.Domain.MatchmakingService;
using W3C.Domain.UpdateService;
using W3ChampionsStatisticService.Maps;

namespace WC3ChampionsStatisticService.Tests.Maps;

/// <summary>
/// The spool purge of <see cref="TemporaryMapExpirySweep"/> (Task 2 L2): a spool file a crash or restart left behind is
/// removed once it is <see cref="TemporaryMapLimits.StaleSpoolFileAgeHours"/> old, judged against the run's clock. Every
/// test uses the private spool directory of the base; the machine-wide one is never touched.
/// </summary>
[TestFixture]
public class TemporaryMapExpirySweepSpoolTests : TemporaryMapUploadServiceTestBase
{
    private static readonly DateTime Now = new(2026, 9, 14, 3, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan StaleAge = TimeSpan.FromHours(TemporaryMapLimits.StaleSpoolFileAgeHours);

    [Test]
    public async Task PurgesSpoolFilesAtLeastTheStaleAgeOld_AndKeepsYoungerOnes()
    {
        var stale = SpoolFile("stale", Now - StaleAge - TimeSpan.FromHours(1));
        var atTheEdge = SpoolFile("edge", Now - StaleAge);
        var fresh = SpoolFile("fresh", Now - TimeSpan.FromHours(1));

        var report = await Sweep(Idle()).RunOnceAsync(Now, CancellationToken.None);

        Assert.That(File.Exists(stale), Is.False, "older than the stale age");
        Assert.That(File.Exists(atTheEdge), Is.False, "exactly the stale age");
        Assert.That(File.Exists(fresh), Is.True, "an upload may still be writing it");
        Assert.That(report.PurgedSpoolFiles, Is.EqualTo(2));
        Assert.That(report.Failed, Is.Zero);
    }

    [Test]
    public async Task JudgesAgeAgainstTheRunsClock_NotTheWallClock()
    {
        // Now is in the past relative to the machine clock; a file written "an hour before Now" is fresh for the run.
        var fresh = SpoolFile("fresh", Now - TimeSpan.FromHours(1));
        Assert.That(DateTime.UtcNow - File.GetLastWriteTimeUtc(fresh), Is.GreaterThan(StaleAge), "precondition: stale by the wall clock");

        var report = await Sweep(Idle()).RunOnceAsync(Now, CancellationToken.None);

        Assert.That(File.Exists(fresh), Is.True);
        Assert.That(report.PurgedSpoolFiles, Is.Zero);
    }

    [Test]
    public async Task LeavesFilesThatAreNotSpoolFilesAlone()
    {
        Directory.CreateDirectory(SpoolDirectory);
        var other = Path.Combine(SpoolDirectory, "notes.txt");
        File.WriteAllText(other, "not a spool file");
        File.SetLastWriteTimeUtc(other, Now - StaleAge - TimeSpan.FromDays(30));

        var report = await Sweep(Idle()).RunOnceAsync(Now, CancellationToken.None);

        Assert.That(File.Exists(other), Is.True);
        Assert.That(report.PurgedSpoolFiles, Is.Zero);
    }

    [Test]
    public async Task AMissingSpoolDirectory_IsNotAFailure()
    {
        Assert.That(Directory.Exists(SpoolDirectory), Is.False, "precondition");

        var report = await Sweep(Idle()).RunOnceAsync(Now, CancellationToken.None);

        Assert.That(report.PurgedSpoolFiles, Is.Zero);
        Assert.That(report.Failed, Is.Zero);
        Assert.That(Directory.Exists(SpoolDirectory), Is.False, "the purge creates nothing");
    }

    [Test]
    public async Task RunsBeforeThePasses_SoAnUpstreamOutageNeverDelaysIt()
    {
        using var logs = new LogCapture();
        var stale = SpoolFile("stale", Now - StaleAge - TimeSpan.FromHours(1));
        var handler = new ScriptedHttpHandler()
            .On(HttpMethod.Get, "/maps/temporary/expired", HttpStatusCode.BadGateway, "<html>502</html>")
            .On(HttpMethod.Get, "/api/content/maps/files", HttpStatusCode.BadGateway, "<html>502</html>");

        var report = await Sweep(handler, logs.CreateLogger<TemporaryMapExpirySweep>()).RunOnceAsync(Now, CancellationToken.None);

        Assert.That(File.Exists(stale), Is.False);
        Assert.That(report.PurgedSpoolFiles, Is.EqualTo(1));
        Assert.That(report.Failed, Is.EqualTo(2), "the two passes failed; the purge did not");
        var lines = logs.Lines();
        Assert.That(Array.FindIndex(lines, l => l.Contains("Purged a stale")), Is.LessThan(Array.FindIndex(lines, l => l.StartsWith("Error", StringComparison.Ordinal))),
            "the purge was done before the first pass failed");
    }

    [Test]
    public async Task AFailedDelete_IsLoggedAndCounted_AndEveryOtherFileIsStillTried()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Ignore("Unix permission bits only.");
            return;
        }

        if (Environment.IsPrivilegedProcess)
        {
            Assert.Ignore("root deletes from a read-only directory regardless.");
            return;
        }

        using var logs = new LogCapture();
        var first = SpoolFile("first", Now - StaleAge - TimeSpan.FromHours(1));
        var second = SpoolFile("second", Now - StaleAge - TimeSpan.FromHours(2));
        var writable = File.GetUnixFileMode(SpoolDirectory);
        File.SetUnixFileMode(SpoolDirectory, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        try
        {
            var report = await Sweep(Idle(), logs.CreateLogger<TemporaryMapExpirySweep>()).RunOnceAsync(Now, CancellationToken.None);

            Assert.That(report.Failed, Is.EqualTo(2), "each file is its own failure; the second was tried after the first failed");
            Assert.That(report.PurgedSpoolFiles, Is.Zero);
            Assert.That(File.Exists(first) && File.Exists(second), Is.True);
            Assert.That(logs.Lines().Count(l => l.StartsWith("Warning", StringComparison.Ordinal) && l.Contains("spool file")), Is.EqualTo(2));
        }
        finally
        {
            File.SetUnixFileMode(SpoolDirectory, writable);
        }
    }

    private string SpoolFile(string name, DateTime lastWriteUtc)
    {
        Directory.CreateDirectory(SpoolDirectory);
        var path = Path.Combine(SpoolDirectory, name + TemporaryMapUploadReader.SpoolFileExtension);
        File.WriteAllBytes(path, [1, 2, 3]);
        File.SetLastWriteTimeUtc(path, lastWriteUtc);
        return path;
    }

    /// <summary>Nothing expired and nothing stored: the two passes have no work.</summary>
    private static ScriptedHttpHandler Idle()
        => new ScriptedHttpHandler()
            .On(HttpMethod.Get, "/maps/temporary/expired", HttpStatusCode.OK, "{\"items\":[]}")
            .On(HttpMethod.Get, "/api/content/maps/files", HttpStatusCode.OK, "{\"files\":[],\"next\":null}");

    private TemporaryMapExpirySweep Sweep(ScriptedHttpHandler handler, ILogger<TemporaryMapExpirySweep> logger = null)
    {
        var factory = new ScriptedHttpHandler.Factory(handler);
        return new TemporaryMapExpirySweep(
            new MatchmakingServiceClient(factory),
            new UpdateServiceClient(factory),
            logger ?? NullLogger<TemporaryMapExpirySweep>.Instance)
        {
            SpoolDirectory = SpoolDirectory,
        };
    }
}
