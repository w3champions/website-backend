using System.Net;
using System.Net.Http;
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

    /// <summary>The exception's status code, or 500 when it has none.</summary>
    public static int StatusCodeOf(HttpRequestException exception)
        => (int)(exception.StatusCode ?? HttpStatusCode.InternalServerError);

    /// <summary>The exception's message, unless it has no status code (see <see cref="TransportFailureMessage"/>).</summary>
    public static string ClientMessageOf(HttpRequestException exception)
        => exception.StatusCode == null ? TransportFailureMessage : exception.Message;

    /// <summary>
    /// Logs an HttpRequestException answered to a client, never with the request URL (it can hold a proofHash). A
    /// status-bearing exception is logged by action and status only, because its message can hold an upstream body:
    /// a 4xx at Warning, anything else at Error. A transport failure is logged at Error with the exception itself.
    /// </summary>
    public static void LogFailure(ILogger logger, HttpRequestException exception, string action)
    {
        if (exception.StatusCode == null)
        {
            logger.LogError(exception, "{Action} could not reach an upstream service and answered 500", action);
            return;
        }

        var statusCode = StatusCodeOf(exception);
        var level = statusCode is >= 400 and < 500 ? LogLevel.Warning : LogLevel.Error;
        logger.Log(level, "{Action} answered {StatusCode} for an HttpRequestException", action, statusCode);
    }
}
