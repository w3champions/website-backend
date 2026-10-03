using System.Net.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;

namespace W3ChampionsStatisticService.WebApi.ExceptionFilters;

public class HttpRequestExceptionFilter(ILogger<HttpRequestExceptionFilter> logger) : IExceptionFilter
{
    /// <summary>
    /// The answer to an HttpRequestException without a status code: a transport failure (connection refused, DNS,
    /// TLS) whose own message names the upstream host and port, which no response body may carry.
    /// </summary>
    public const string TransportFailureMessage = "An upstream service could not be reached.";

    public void OnException(ExceptionContext context)
    {
        // Ensure that we're propagating HttpRequestException if it bubbles up.
        if (context.Exception is HttpRequestException httpRequestException)
        {
            LogFailure(logger, httpRequestException, context.ActionDescriptor.DisplayName);
            var errorResponse = new ErrorResult(ClientMessageOf(httpRequestException));

            context.Result = new ObjectResult(errorResponse)
            {
                StatusCode = StatusCodeOf(httpRequestException)
            };
            context.ExceptionHandled = true;
            return;
        }
    }

    /// <summary>
    /// The exception's status code when it is an error status (400 or above), otherwise 502 Bad Gateway: an exception
    /// without a status is a transport failure, and one below 400 carries an upstream success whose body breaks its
    /// contract (see UpstreamContract) or an unfollowed redirect. Neither may be answered as a success or a redirect.
    /// </summary>
    public static int StatusCodeOf(HttpRequestException exception)
        => exception.StatusCode is { } status && (int)status >= StatusCodes.Status400BadRequest
            ? (int)status
            : StatusCodes.Status502BadGateway;

    /// <summary>The exception's message, unless it has no status code (see <see cref="TransportFailureMessage"/>).</summary>
    public static string ClientMessageOf(HttpRequestException exception)
        => exception.StatusCode == null ? TransportFailureMessage : exception.Message;

    /// <summary>
    /// Logs an HttpRequestException answered to a client, never with the request URL or its headers: a query can carry
    /// a credential (the hub's access_token; the pre-check's proofHash before revision 10 moved it to the x-proof-hash
    /// header). A status-bearing exception is logged by action and statuses only, because its message can hold an upstream body:
    /// a 4xx at Warning, anything else at Error, and a status below 400 next to the 502 answered for it. A transport
    /// failure is logged at Error with the exception itself.
    /// </summary>
    public static void LogFailure(ILogger logger, HttpRequestException exception, string action)
    {
        var statusCode = StatusCodeOf(exception);
        if (exception.StatusCode == null)
        {
            logger.LogError(exception, "{Action} could not reach an upstream service and answered {StatusCode}", action, statusCode);
            return;
        }

        if ((int)exception.StatusCode < StatusCodes.Status400BadRequest)
        {
            logger.LogError("{Action} answered {StatusCode} for an HttpRequestException carrying {UpstreamStatusCode}",
                action, statusCode, (int)exception.StatusCode);
            return;
        }

        var level = statusCode is >= 400 and < 500 ? LogLevel.Warning : LogLevel.Error;
        logger.Log(level, "{Action} answered {StatusCode} for an HttpRequestException", action, statusCode);
    }
}
