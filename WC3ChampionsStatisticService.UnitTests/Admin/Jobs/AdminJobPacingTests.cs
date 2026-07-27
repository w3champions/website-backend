using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using W3ChampionsStatisticService.Admin.Jobs;

namespace WC3ChampionsStatisticService.Tests.Admin.Jobs;

/// <summary>
/// Covers how <see cref="AdminJobContext.Pace"/> reacts to the pressure signals. These
/// spin for a little over <see cref="AdminJobContext.SampleInterval"/> because the
/// counters need two readings before a difference means anything.
/// </summary>
[TestFixture]
public class AdminJobPacingTests
{
    private FakePressureProbe _probe;
    private AdminJobContext _context;

    [SetUp]
    public void Setup()
    {
        _probe = new FakePressureProbe();
        _context = new AdminJobContext(
            "paced",
            new FakeAdminJobRepository(),
            new AdminJob { Id = "paced" },
            fallbackDutyCycle: 0.25,
            _probe);
    }

    /// <summary>
    /// Paces in a tight loop until a second reading has been taken. Individual pauses
    /// stay negligible because each batch is near-instant.
    /// </summary>
    private async Task PaceUntilSampled(int samples)
    {
        var deadline = DateTimeOffset.UtcNow + AdminJobContext.SampleInterval * (samples + 1);
        while (_probe.Samples < samples && DateTimeOffset.UtcNow < deadline)
        {
            await _context.Pace(CancellationToken.None);
        }

        Assert.That(_probe.Samples, Is.GreaterThanOrEqualTo(samples), "the probe was never sampled enough times");
    }

    [Test]
    public async Task AHealthyServerReportsNoReasonToSlowDown()
    {
        await PaceUntilSampled(2);

        Assert.That(_context.LastPressureReason, Is.Null);
    }

    [Test]
    public async Task EvictionOnApplicationThreadsIsTreatedAsPressure()
    {
        await PaceUntilSampled(1);

        // Any movement at all on this counter means mongod is conscripting query threads
        // into eviction, so no threshold is applied to it.
        _probe.ApplicationThreadEvictions = 1;
        await PaceUntilSampled(2);

        Assert.That(_context.LastPressureReason, Does.Contain("application threads"));
    }

    [Test]
    public async Task ADirtyCacheOverTheLimitIsTreatedAsPressure()
    {
        _probe.DirtyCacheFraction = 0.15;

        await PaceUntilSampled(2);

        Assert.That(_context.LastPressureReason, Does.Contain("dirty cache"));
    }

    [Test]
    public async Task PressureClearingLetsTheJobSpeedBackUp()
    {
        await PaceUntilSampled(1);
        _probe.DirtyCacheFraction = 0.15;
        await PaceUntilSampled(2);
        Assert.That(_context.LastPressureReason, Is.Not.Null, "precondition: the job should have backed off");

        _probe.DirtyCacheFraction = 0.01;
        await PaceUntilSampled(3);

        Assert.That(_context.LastPressureReason, Is.Null);
    }

    [Test]
    public async Task ASignalThatCannotBeReadDoesNotLookLikeHeadroom()
    {
        _probe.DatabaseAvailable = false;

        // One measurable batch, then a pace: with no signal the 25% fallback applies, so
        // roughly 100ms of work owes roughly 300ms of pause.
        await _context.Pace(CancellationToken.None);
        await Task.Delay(100);

        var start = DateTimeOffset.UtcNow;
        await _context.Pace(CancellationToken.None);
        var paused = DateTimeOffset.UtcNow - start;

        Assert.That(paused, Is.GreaterThan(TimeSpan.FromMilliseconds(150)));
    }
}
