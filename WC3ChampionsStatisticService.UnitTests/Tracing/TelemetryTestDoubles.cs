using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ApplicationInsights.Channel;

namespace WC3ChampionsStatisticService.Tests.Tracing;

/// <summary>Stands in for an OTLP collector (gRPC or HTTP/protobuf): records each export body and accepts it.</summary>
internal sealed class InMemoryOtlpCollector : HttpMessageHandler
{
    public ConcurrentQueue<byte[]> Requests { get; } = new();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Enqueue(await request.Content!.ReadAsByteArrayAsync(cancellationToken));
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([]) };
        response.Headers.TryAddWithoutValidation("grpc-status", "0");
        return response;
    }
}

/// <summary>An Application Insights channel that keeps every item it is handed, in order.</summary>
internal sealed class CollectingTelemetryChannel : ITelemetryChannel
{
    public ConcurrentQueue<ITelemetry> Items { get; } = new();

    public bool? DeveloperMode { get; set; }

    public string EndpointAddress { get; set; }

    public void Send(ITelemetry item) => Items.Enqueue(item);

    public void Flush()
    {
    }

    public void Dispose()
    {
    }
}
