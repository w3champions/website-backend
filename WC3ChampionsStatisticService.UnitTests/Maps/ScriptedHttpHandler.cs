using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WC3ChampionsStatisticService.Tests.Maps;

/// <summary>
/// Hand-rolled HttpMessageHandler in the style of AdminWarningsControllerTests.CapturingHandler,
/// extended with per-route scripting so one handler can stand in for BOTH internal services at once
/// (matchmaking and update-service paths are disjoint, so routes match on path, never on host — the
/// base URLs are static readonly fields read once at type-init and cannot be varied from a test).
/// </summary>
internal sealed class ScriptedHttpHandler : HttpMessageHandler
{
    private readonly List<(Func<HttpRequestMessage, bool> Match, Func<HttpRequestMessage, Task<HttpResponseMessage>> Respond)> _routes = [];

    public List<HttpRequestMessage> Requests { get; } = [];

    public List<string> RequestBodies { get; } = [];

    public ScriptedHttpHandler On(HttpMethod method, string pathContains, HttpStatusCode status, string jsonBody = "{}")
        => On(r => r.Method == method && r.RequestUri!.PathAndQuery.Contains(pathContains, StringComparison.Ordinal),
              _ => Json(status, jsonBody));

    public ScriptedHttpHandler On(
        Func<HttpRequestMessage, bool> match,
        Func<HttpRequestMessage, HttpResponseMessage> respond)
        => OnAsync(match, r => Task.FromResult(respond(r)));

    /// <summary>
    /// A responder that awaits: the one way to hold a request in flight without blocking the caller's thread, which a
    /// BackgroundService's StartAsync needs (it runs ExecuteAsync synchronously up to its first real await).
    /// </summary>
    public ScriptedHttpHandler OnAsync(
        Func<HttpRequestMessage, bool> match,
        Func<HttpRequestMessage, Task<HttpResponseMessage>> respond)
    {
        _routes.Add((match, respond));
        return this;
    }

    /// <summary>Scripts consecutive responses for the same route, so retry branches can be exercised.</summary>
    public ScriptedHttpHandler OnSequence(HttpMethod method, string pathContains, params (HttpStatusCode Status, string Body)[] responses)
    {
        var index = 0;
        return On(
            r => r.Method == method && r.RequestUri!.PathAndQuery.Contains(pathContains, StringComparison.Ordinal),
            _ =>
            {
                var response = responses[Math.Min(index, responses.Length - 1)];
                index++;
                return Json(response.Status, response.Body);
            });
    }

    public int CountRequests(HttpMethod method, string pathContains)
        => Requests.FindAll(r => r.Method == method && r.RequestUri!.PathAndQuery.Contains(pathContains, StringComparison.Ordinal)).Count;

    public HttpRequestMessage LastRequest(HttpMethod method, string pathContains)
        => Requests.FindLast(r => r.Method == method && r.RequestUri!.PathAndQuery.Contains(pathContains, StringComparison.Ordinal));

    public string LastBody(HttpMethod method, string pathContains)
    {
        for (var i = Requests.Count - 1; i >= 0; i--)
        {
            if (Requests[i].Method == method && Requests[i].RequestUri!.PathAndQuery.Contains(pathContains, StringComparison.Ordinal))
            {
                return RequestBodies[i];
            }
        }

        return null;
    }

    /// <summary>A response with <paramref name="body"/> as its content, labelled application/json whatever it holds.</summary>
    public static HttpResponseMessage Json(HttpStatusCode status, string body)
        => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        RequestBodies.Add(request.Content == null ? "" : await request.Content.ReadAsStringAsync(cancellationToken));

        foreach (var route in _routes)
        {
            if (route.Match(request))
            {
                return await route.Respond(request);
            }
        }

        // Unscripted call: fail loudly rather than silently returning something plausible.
        return Json(HttpStatusCode.NotImplemented,
            "{\"message\":\"unscripted route " + request.Method + " " + request.RequestUri!.PathAndQuery + "\"}");
    }

    /// <summary>
    /// Returns a NEW client over the shared handler on every call, like the real IHttpClientFactory: a
    /// client whose Timeout is set after it has sent a request throws, so one shared instance would
    /// break code that configures a fresh client per call. The handler is never disposed with a client.
    /// </summary>
    internal sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        /// <summary>Every client handed out, in creation order.</summary>
        public List<HttpClient> CreatedClients { get; } = [];

        public HttpClient CreateClient(string name)
        {
            var client = new HttpClient(handler, disposeHandler: false);
            CreatedClients.Add(client);
            return client;
        }
    }
}
