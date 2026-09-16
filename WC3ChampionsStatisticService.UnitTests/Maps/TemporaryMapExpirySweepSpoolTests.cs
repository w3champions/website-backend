using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
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
        var stale = PlantSpoolFile("stale", Now - StaleAge - TimeSpan.FromHours(1));
        var atTheEdge = PlantSpoolFile("edge", Now - StaleAge);
        var fresh = PlantSpoolFile("fresh", Now - TimeSpan.FromHours(1));

        var report = await CreateSweep(NothingToDo()).RunOnceAsync(Now, CancellationToken.None);

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
        var fresh = PlantSpoolFile("fresh", Now - TimeSpan.FromHours(1));
        Assert.That(DateTime.UtcNow - File.GetLastWriteTimeUtc(fresh), Is.GreaterThan(StaleAge), "precondition: stale by the wall clock");

        var report = await CreateSweep(NothingToDo()).RunOnceAsync(Now, CancellationToken.None);

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

        var report = await CreateSweep(NothingToDo()).RunOnceAsync(Now, CancellationToken.None);

        Assert.That(File.Exists(other), Is.True);
        Assert.That(report.PurgedSpoolFiles, Is.Zero);
    }

    [Test]
    public async Task AMissingSpoolDirectory_IsNotAFailure()
    {
        Assert.That(Directory.Exists(SpoolDirectory), Is.False, "precondition");

        var report = await CreateSweep(NothingToDo()).RunOnceAsync(Now, CancellationToken.None);

        Assert.That(report.PurgedSpoolFiles, Is.Zero);
        Assert.That(report.Failed, Is.Zero);
        Assert.That(Directory.Exists(SpoolDirectory), Is.False, "the purge creates nothing");
    }

    [Test]
    public async Task RunsBeforeThePasses_SoAnUpstreamOutageNeverDelaysIt()
    {
        using var logs = new LogCapture();
        var stale = PlantSpoolFile("stale", Now - StaleAge - TimeSpan.FromHours(1));
        var handler = new ScriptedHttpHandler()
            .On(HttpMethod.Get, "/maps/temporary/expired", HttpStatusCode.BadGateway, "<html>502</html>")
            .On(HttpMethod.Get, "/api/content/maps/files", HttpStatusCode.BadGateway, "<html>502</html>");

        var report = await CreateSweep(handler, logs.CreateLogger<TemporaryMapExpirySweep>()).RunOnceAsync(Now, CancellationToken.None);

        Assert.That(File.Exists(stale), Is.False);
        Assert.That(report.PurgedSpoolFiles, Is.EqualTo(1));
        Assert.That(report.Failed, Is.EqualTo(2), "the two passes failed; the purge did not");
        var lines = logs.Lines();
        Assert.That(Array.FindIndex(lines, l => l.Contains("Purged a stale")), Is.LessThan(Array.FindIndex(lines, l => l.StartsWith("Error", StringComparison.Ordinal))),
            "the purge was done before the first pass failed");
    }

    [Test]
    public async Task ThePurgeLine_CarriesTheFilesLastWrite()
    {
        // S6-L4: read after the delete, the timestamp is the epoch of a file that no longer exists.
        using var logs = new LogCapture();
        PlantSpoolFile("stale", new DateTime(2026, 9, 12, 2, 30, 0, DateTimeKind.Utc));

        await CreateSweep(NothingToDo(), logs.CreateLogger<TemporaryMapExpirySweep>()).RunOnceAsync(Now, CancellationToken.None);

        var line = logs.Lines().Single(l => l.Contains("Purged a stale"));
        Assert.That(line, Does.Contain("LastWriteUtc=\"2026-09-12T02:30:00.0000000Z\""));
    }

    [Test]
    public async Task ALinkedSpoolDirectory_IsRefused_AndThePassesStillRun()
    {
        // S6-L1: the reader refuses to spool through a link; the purge must not delete through one either. The refusal
        // is this run's failure, logged once, and the two passes are not delayed by it.
        if (OperatingSystem.IsWindows())
        {
            Assert.Ignore("Creating a directory symbolic link needs elevation on Windows.");
            return;
        }

        using var logs = new LogCapture();
        var target = Path.Combine(TestRoot, "elsewhere");
        Directory.CreateDirectory(target);
        var planted = Path.Combine(target, "stale" + TemporaryMapUploadReader.SpoolFileExtension);
        File.WriteAllBytes(planted, [1, 2, 3]);
        File.SetLastWriteTimeUtc(planted, Now - StaleAge - TimeSpan.FromHours(1));
        Directory.CreateSymbolicLink(SpoolDirectory, target);
        var handler = NothingToDo();

        var report = await CreateSweep(handler, logs.CreateLogger<TemporaryMapExpirySweep>()).RunOnceAsync(Now, CancellationToken.None);

        Assert.That(File.Exists(planted), Is.True, "nothing is purged through the link");
        Assert.That(report.PurgedSpoolFiles, Is.Zero);
        Assert.That(report.Failed, Is.EqualTo(1));
        Assert.That(handler.CountRequests(HttpMethod.Get, "/maps/temporary/expired"), Is.EqualTo(1), "the expiry pass still ran");
        Assert.That(handler.CountRequests(HttpMethod.Get, "/api/content/maps/files"), Is.EqualTo(1), "and the reconciliation pass");
        var errors = logs.Lines().Where(l => l.StartsWith("Error", StringComparison.Ordinal)).ToArray();
        Assert.That(errors, Has.Length.EqualTo(1), "logged once");
        Assert.That(errors[0], Does.Contain("link"));
    }

    [Test]
    public async Task AFileThePurgeCannotHandle_IsThatFilesFailure_AndTheRestIsStillPurged()
    {
        // Per-file isolation without permission bits, so it runs as root and on Windows too (a read-only directory would
        // not do: the purge applies the reader's rules first, which chmod the directory back to 0700). A listed name no
        // FileInfo accepts fails with an ArgumentException, which is neither an IOException nor an UnauthorizedAccessException.
        using var logs = new LogCapture();
        var first = PlantSpoolFile("first", Now - StaleAge - TimeSpan.FromHours(1));
        var second = PlantSpoolFile("second", Now - StaleAge - TimeSpan.FromHours(2));
        var unhandleable = Path.Combine(SpoolDirectory, "bad\0name" + TemporaryMapUploadReader.SpoolFileExtension);
        var sweep = CreateSweep(NothingToDo(), logs.CreateLogger<TemporaryMapExpirySweep>(), _ => [first, unhandleable, second]);

        var report = await sweep.RunOnceAsync(Now, CancellationToken.None);

        Assert.That(report.PurgedSpoolFiles, Is.EqualTo(2), "the file after the fault was still purged");
        Assert.That(report.Failed, Is.EqualTo(1));
        Assert.That(File.Exists(first) || File.Exists(second), Is.False);
        var warning = logs.Lines().Single(l => l.StartsWith("Warning", StringComparison.Ordinal));
        Assert.That(warning, Does.Contain("ArgumentException").And.Contain("SpoolFile=\"invalid\""));
        Assert.That(warning, Does.Not.Contain("bad"));
    }

    [Test]
    public async Task AFaultWhileListingTheSpoolDirectory_IsCounted_AndBothPassesStillRun()
    {
        // R-Minor5: whatever the purge throws, the passes run. What was purged before the fault stays purged.
        using var logs = new LogCapture();
        var stale = PlantSpoolFile("stale", Now - StaleAge - TimeSpan.FromHours(1));
        var handler = NothingToDo();
        var sweep = CreateSweep(handler, logs.CreateLogger<TemporaryMapExpirySweep>(), _ => ListingThatBreaksAfter(stale));

        var report = await sweep.RunOnceAsync(Now, CancellationToken.None);

        Assert.That(File.Exists(stale), Is.False);
        Assert.That(report.PurgedSpoolFiles, Is.EqualTo(1));
        Assert.That(report.Failed, Is.EqualTo(1));
        Assert.That(handler.CountRequests(HttpMethod.Get, "/maps/temporary/expired"), Is.EqualTo(1), "the expiry pass still ran");
        Assert.That(handler.CountRequests(HttpMethod.Get, "/api/content/maps/files"), Is.EqualTo(1), "and the reconciliation pass");
        Assert.That(logs.Lines().Single(l => l.StartsWith("Error", StringComparison.Ordinal)), Does.Contain("InvalidOperationException"));
    }

    [Test]
    public void ACancelledEnumeration_PropagatesInsteadOfBeingLoggedAndSwallowed()
    {
        // Task 6 Info observation: the purge's catch-alls must let a genuine cancellation through rather than log it as
        // an Error and count it as Failed, which would hide a shutdown behind "retrying next run".
        using var logs = new LogCapture();
        var stale = PlantSpoolFile("stale", Now - StaleAge - TimeSpan.FromHours(1));
        var handler = NothingToDo();
        var sweep = CreateSweep(handler, logs.CreateLogger<TemporaryMapExpirySweep>(), _ => CancelledAfter(stale));

        Assert.ThrowsAsync<OperationCanceledException>(() => sweep.RunOnceAsync(Now, CancellationToken.None));

        Assert.That(File.Exists(stale), Is.False, "the file yielded before the cancellation was still purged");
        Assert.That(logs.Lines().Any(l => l.StartsWith("Error", StringComparison.Ordinal)), Is.False,
            "a cancellation is not this run's failure to log");
    }

    private static IEnumerable<string> ListingThatBreaksAfter(string first)
    {
        yield return first;
        throw new InvalidOperationException("the listing broke");
    }

    private static IEnumerable<string> CancelledAfter(string first)
    {
        yield return first;
        throw new OperationCanceledException();
    }
}
