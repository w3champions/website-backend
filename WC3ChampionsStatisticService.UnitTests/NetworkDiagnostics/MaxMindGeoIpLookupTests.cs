using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using W3ChampionsStatisticService.NetworkDiagnostics.GeoIp;

namespace WC3ChampionsStatisticService.Tests.NetworkDiagnostics;

[TestFixture]
public class MaxMindGeoIpLookupTests
{
    private static readonly string FixtureDir = Path.Combine(TestContext.CurrentContext.TestDirectory, "NetworkDiagnostics", "GeoIp");
    private string _dir;
    private FixedTimeProvider _time;
    private ListLogger<MaxMindGeoIpLookup> _log;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "netdiag-geoip-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _time = new FixedTimeProvider(new DateTimeOffset(2026, 10, 12, 12, 0, 0, TimeSpan.Zero));
        _log = new ListLogger<MaxMindGeoIpLookup>();
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_dir, recursive: true);

    private string City => Path.Combine(_dir, "GeoLite2-City.mmdb");
    private string Asn => Path.Combine(_dir, "GeoLite2-ASN.mmdb");

    private void CopyFixtures()
    {
        File.Copy(Path.Combine(FixtureDir, "GeoLite2-City-Test.mmdb"), City, overwrite: true);
        File.Copy(Path.Combine(FixtureDir, "GeoLite2-ASN-Test.mmdb"), Asn, overwrite: true);
    }

    /// <summary>What the geoipupdate sidecar does: write a new file next to the old one, then rename it over.</summary>
    private void ReplaceByRename(string target, string content, DateTime? mtime = null)
    {
        var temp = target + ".tmp";
        File.Copy(content, temp, overwrite: true);
        if (mtime != null) File.SetLastWriteTimeUtc(temp, mtime.Value);
        File.Move(temp, target, overwrite: true);
    }

    private MaxMindGeoIpLookup Create() => new(City, Asn, _time, _log);

    [Test]
    public void ResolvesCityAndAsn()
    {
        CopyFixtures();
        using var lookup = Create();

        var geo = lookup.Lookup(["81.2.69.160", "1.128.0.0", "10.0.0.1"]);

        Assert.AreEqual("GB", geo["81.2.69.160"].Country);
        Assert.AreEqual("London", geo["81.2.69.160"].City);
        Assert.AreEqual(51.5142, geo["81.2.69.160"].Lat);
        Assert.AreEqual(-0.0931, geo["81.2.69.160"].Lon);
        Assert.IsNotNull(geo["81.2.69.160"].GeoDbBuild);
        Assert.AreEqual(1221, geo["1.128.0.0"].Asn);
        Assert.AreEqual("Telstra Pty Ltd", geo["1.128.0.0"].AsnOrg);
        Assert.IsNull(geo["10.0.0.1"]);
    }

    [Test]
    public void InvalidIp_IsANullEntryNotAnException()
    {
        CopyFixtures();
        using var lookup = Create();

        var geo = lookup.Lookup(["not-an-ip", "::1"]);

        Assert.IsNull(geo["not-an-ip"]);
        Assert.IsNull(geo["::1"]);
    }

    [Test]
    public void MissingFiles_ReturnNullsAndWarnOnce()
    {
        using var lookup = Create();

        Assert.IsNull(lookup.Lookup(["81.2.69.160"])["81.2.69.160"]);
        _time.Now += TimeSpan.FromSeconds(61);
        Assert.IsNull(lookup.Lookup(["81.2.69.160"])["81.2.69.160"]);

        Assert.AreEqual(1, _log.Count(LogLevel.Warning));
    }

    [Test]
    public void MissingThenAppearing_ReloadsWithoutRestart()
    {
        using var lookup = Create();
        Assert.IsNull(lookup.Lookup(["81.2.69.160"])["81.2.69.160"]);

        CopyFixtures();
        _time.Now += TimeSpan.FromSeconds(30);
        Assert.IsNull(lookup.Lookup(["81.2.69.160"])["81.2.69.160"], "no disk check inside the recheck interval");

        _time.Now += TimeSpan.FromSeconds(31);
        Assert.AreEqual("GB", lookup.Lookup(["81.2.69.160"])["81.2.69.160"].Country);
    }

    [Test]
    public void ChangedMtime_SwapsTheReader()
    {
        CopyFixtures();
        using var lookup = Create();
        Assert.AreEqual(1221, lookup.Lookup(["1.128.0.0"])["1.128.0.0"].Asn);

        File.Delete(Asn);
        File.Copy(Path.Combine(FixtureDir, "GeoLite2-City-Test.mmdb"), Asn); // a City DB at the ASN path: ASN lookups now fail
        File.SetLastWriteTimeUtc(Asn, DateTime.UtcNow.AddMinutes(5));
        _time.Now += TimeSpan.FromSeconds(61);

        Assert.IsNull(lookup.Lookup(["1.128.0.0"])["1.128.0.0"]?.Asn);
    }

    [Test]
    public void RenameReplacement_WithSameMtimeButDifferentSize_SwapsTheReader()
    {
        CopyFixtures();
        using var lookup = Create();
        Assert.AreEqual(1221, lookup.Lookup(["1.128.0.0"])["1.128.0.0"].Asn);

        var sameMtime = File.GetLastWriteTimeUtc(Asn);
        ReplaceByRename(Asn, Path.Combine(FixtureDir, "GeoLite2-City-Test.mmdb"), sameMtime);
        _time.Now += TimeSpan.FromSeconds(61);

        Assert.IsNull(lookup.Lookup(["1.128.0.0"])["1.128.0.0"]?.Asn);
    }

    [Test]
    public void CorruptReplacement_KeepsThePreviousReader_AndLogsInsteadOfThrowing()
    {
        CopyFixtures();
        using var lookup = Create();
        Assert.AreEqual(1221, lookup.Lookup(["1.128.0.0"])["1.128.0.0"].Asn);

        File.WriteAllText(Asn, "this is not an mmdb file");
        File.SetLastWriteTimeUtc(Asn, DateTime.UtcNow.AddMinutes(5));
        _time.Now += TimeSpan.FromSeconds(61);

        Assert.AreEqual(1221, lookup.Lookup(["1.128.0.0"])["1.128.0.0"].Asn);
        Assert.AreEqual(1, _log.Count(LogLevel.Warning));
    }

    [Test]
    public void Lookups_RunningWhileFilesAreSwapped_NeverThrow()
    {
        CopyFixtures();
        using var lookup = Create();
        var stop = false;
        var readers = Task.Run(() =>
        {
            while (!Volatile.Read(ref stop))
            {
                _time.Now += TimeSpan.FromSeconds(61);
                Assert.AreEqual("GB", lookup.Lookup(["81.2.69.160"])["81.2.69.160"].Country);
            }
        });

        for (var i = 0; i < 20; i++)
        {
            ReplaceByRename(City, Path.Combine(FixtureDir, "GeoLite2-City-Test.mmdb"), DateTime.UtcNow.AddMinutes(i + 1));
        }
        Volatile.Write(ref stop, true);

        Assert.DoesNotThrowAsync(async () => await readers);
    }
}

public sealed class ListLogger<T> : ILogger<T>
{
    private readonly List<(LogLevel Level, string Message)> _entries = [];
    public IDisposable BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
    {
        lock (_entries) _entries.Add((logLevel, formatter(state, exception) + (exception == null ? "" : "\n" + exception)));
    }
    public int Count(LogLevel level) { lock (_entries) return _entries.FindAll(e => e.Level == level).Count; }
    /// <summary>Every rendered message (and exception) logged so far; used to prove secrets are never logged.</summary>
    public string AllText { get { lock (_entries) return string.Join("\n", _entries.ConvertAll(e => e.Message)); } }
}
