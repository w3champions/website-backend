using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;
using W3ChampionsStatisticService.NetworkDiagnostics.Delivery;

namespace WC3ChampionsStatisticService.Tests.NetworkDiagnostics;

[TestFixture]
public class NetworkDiagnosticsDispatcherTests
{
    [Test]
    public async Task KeepsTickingAfterAThrowingTick_AndStopsCleanly()
    {
        var secondTick = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var scheduler = new Mock<INetworkDiagnosticsScheduler>();
        scheduler.Setup(s => s.TickAsync(It.IsAny<CancellationToken>())).Returns(() =>
        {
            if (Interlocked.Increment(ref calls) == 1) throw new InvalidOperationException("boom");
            secondTick.TrySetResult();
            return Task.CompletedTask;
        });
        var dispatcher = new NetworkDiagnosticsDispatcher(scheduler.Object, TimeProvider.System,
            NullLogger<NetworkDiagnosticsDispatcher>.Instance, TimeSpan.FromMilliseconds(10));

        await dispatcher.StartAsync(CancellationToken.None);
        var finished = await Task.WhenAny(secondTick.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        await dispatcher.StopAsync(CancellationToken.None);

        Assert.AreSame(secondTick.Task, finished, "the dispatcher must survive a throwing tick");
        Assert.IsTrue(dispatcher.ExecuteTask!.IsCompletedSuccessfully);
    }

    [Test]
    public async Task ATickThatFailsWhileStopping_DoesNotFaultTheService()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var scheduler = new Mock<INetworkDiagnosticsScheduler>();
        scheduler.Setup(s => s.TickAsync(It.IsAny<CancellationToken>())).Returns(async (CancellationToken ct) =>
        {
            started.TrySetResult();
            await Task.Delay(Timeout.Infinite, ct).ContinueWith(_ => { });
            throw new InvalidOperationException("failed during shutdown");
        });
        var dispatcher = new NetworkDiagnosticsDispatcher(scheduler.Object, TimeProvider.System,
            NullLogger<NetworkDiagnosticsDispatcher>.Instance, TimeSpan.FromMilliseconds(10));

        await dispatcher.StartAsync(CancellationToken.None);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await dispatcher.StopAsync(CancellationToken.None);

        Assert.IsTrue(dispatcher.ExecuteTask!.IsCompletedSuccessfully);
    }
}
