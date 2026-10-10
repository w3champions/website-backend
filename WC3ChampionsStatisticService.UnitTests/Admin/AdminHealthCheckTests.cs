using Microsoft.AspNetCore.Mvc;
using NUnit.Framework;
using W3ChampionsStatisticService.Admin;

namespace WC3ChampionsStatisticService.Tests.Admin;

[TestFixture]
public class AdminHealthCheckTests
{
    [Test]
    public void HealthCheck_IsDependencyFree()
    {
        // The container healthcheck probes this endpoint; it must answer from the process alone
        // (no Mongo, no sibling service), so a slow dependency never marks the container unhealthy.
        var controller = new AdminController(null, null, null, null, null, null, null);

        Assert.That(controller.HealthCheck(), Is.InstanceOf<OkResult>());
    }
}
