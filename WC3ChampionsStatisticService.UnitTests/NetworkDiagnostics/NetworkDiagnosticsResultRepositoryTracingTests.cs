using System.Linq;
using System.Reflection;
using NUnit.Framework;
using W3C.Domain.Tracing;
using W3ChampionsStatisticService.NetworkDiagnostics.Persistence;

namespace WC3ChampionsStatisticService.Tests.NetworkDiagnostics;

[TestFixture]
public class NetworkDiagnosticsResultRepositoryTracingTests
{
    // TracingInterceptor reads parameter attributes from the proxied interface method (invocation.Method),
    // so [NoTrace] must sit on the interface: the result document carries client IPs and must not reach traces (P13).
    [Test]
    public void InsertResultAsync_ResultParameter_IsNoTraceOnTheInterface()
    {
        var parameter = typeof(INetworkDiagnosticsResultRepository)
            .GetMethod(nameof(INetworkDiagnosticsResultRepository.InsertResultAsync))!
            .GetParameters().Single();

        Assert.IsNotNull(parameter.GetCustomAttribute<NoTraceAttribute>());
    }

    // The admin filters carry the BattleTag and IPs asked for; they stay out of trace tags as well.
    [TestCase(nameof(INetworkDiagnosticsResultRepository.ListMeasurementsAsync))]
    [TestCase(nameof(INetworkDiagnosticsResultRepository.MeasurementIdsAsync))]
    public void AdminQueries_FilterParameter_IsNoTraceOnTheInterface(string method)
    {
        var parameter = typeof(INetworkDiagnosticsResultRepository).GetMethod(method)!.GetParameters().Single(p => p.Name == "filter");

        Assert.IsNotNull(parameter.GetCustomAttribute<NoTraceAttribute>());
    }
}
