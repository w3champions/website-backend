using System.Net;
using System.Net.Http;
using Newtonsoft.Json;

namespace W3C.Domain.Common;

/// <summary>
/// Strict reading of an internal service's answer on a status that promises a body (design spec Appendix A.5/A.7).
/// A body that is empty, not JSON, of the wrong shape or without the record the contract pins is an upstream fault:
/// it surfaces as an <see cref="HttpRequestException"/> carrying the response's own status, so callers that map
/// upstream failures by exception type never see a JsonException or a silently empty result instead. The message
/// names the service and the status only; the parser's exception is not attached because its text can quote the body.
/// </summary>
internal static class UpstreamContract
{
    public static T Deserialize<T>(string content, HttpStatusCode statusCode, string service)
        where T : class
    {
        try
        {
            return JsonConvert.DeserializeObject<T>(content ?? "") ?? throw Violation(statusCode, service);
        }
        catch (JsonException)
        {
            throw Violation(statusCode, service);
        }
    }

    public static HttpRequestException Violation(HttpStatusCode statusCode, string service)
        => new($"{service} answered {(int)statusCode} with a body that breaks its contract", null, statusCode);
}
