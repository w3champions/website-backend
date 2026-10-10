using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WC3ChampionsStatisticService.Tests.Admin;

internal sealed class StubHttpClientFactory(HttpClient client) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => client;
}

/// <summary>Records every request and answers with a fixed status code and JSON body.</summary>
internal sealed class StubMatchmakingHandler(HttpStatusCode statusCode = HttpStatusCode.OK, string responseBody = "") : HttpMessageHandler
{
    public List<HttpRequestMessage> Requests { get; } = [];
    public List<string> RequestBodies { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        RequestBodies.Add(request.Content == null ? "" : await request.Content.ReadAsStringAsync(cancellationToken));

        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(responseBody, Encoding.UTF8, "application/json"),
        };
    }
}

/// <summary>Fails every request the way HttpClient does when matchmaking is unreachable or times out.</summary>
internal sealed class ThrowingMatchmakingHandler(Exception exception) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromException<HttpResponseMessage>(exception);
}
