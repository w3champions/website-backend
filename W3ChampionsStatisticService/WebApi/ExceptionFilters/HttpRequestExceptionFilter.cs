using System;
using System.Net.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Serilog;

namespace W3ChampionsStatisticService.WebApi.ExceptionFilters;

/// <summary>
/// Maps HttpRequestException to a response. 4xx messages are meant for the caller and are returned as is.
/// Transport failures (no status code: unreachable host, DNS, refused connection) and upstream 5xx answers
/// carry internal host names or raw upstream text, so they are logged in full and answered with a generic body.
/// A timed-out upstream call (TaskCanceledException caused by a TimeoutException) is answered with 504.
/// </summary>
public class HttpRequestExceptionFilter : IExceptionFilter
{
    internal const string UnreachableMessage = "Upstream service unavailable";
    internal const string TimeoutMessage = "Upstream service timed out";
    internal const string UpstreamErrorMessage = "Upstream service error";

    public void OnException(ExceptionContext context)
    {
        var exception = context.Exception;
        if (exception is HttpRequestException httpRequestException)
        {
            if (httpRequestException.StatusCode is { } status && (int)status is >= 400 and < 500)
            {
                Respond(context, (int)status, httpRequestException.Message);
                return;
            }

            if (HasTimeoutCause(httpRequestException))
            {
                RespondGeneric(context, exception, StatusCodes.Status504GatewayTimeout, TimeoutMessage);
            }
            else if (httpRequestException.StatusCode is { } upstreamStatus)
            {
                RespondGeneric(context, exception, (int)upstreamStatus >= 500 ? (int)upstreamStatus : StatusCodes.Status502BadGateway, UpstreamErrorMessage);
            }
            else
            {
                RespondGeneric(context, exception, StatusCodes.Status502BadGateway, UnreachableMessage);
            }
        }
        else if (exception is OperationCanceledException && HasTimeoutCause(exception))
        {
            // HttpClient reports its own timeout as TaskCanceledException with a TimeoutException inside;
            // a client abort has no such cause and is left alone.
            RespondGeneric(context, exception, StatusCodes.Status504GatewayTimeout, TimeoutMessage);
        }
    }

    private static bool HasTimeoutCause(Exception exception)
    {
        for (var inner = exception.InnerException; inner != null; inner = inner.InnerException)
        {
            if (inner is TimeoutException) return true;
        }
        return false;
    }

    private static void RespondGeneric(ExceptionContext context, Exception exception, int statusCode, string message)
    {
        Log.Warning(exception, "Upstream request failed, answering {StatusCode}", statusCode);
        Respond(context, statusCode, message);
    }

    private static void Respond(ExceptionContext context, int statusCode, string message)
    {
        context.Result = new ObjectResult(new ErrorResult(message)) { StatusCode = statusCode };
        context.ExceptionHandled = true;
    }
}
