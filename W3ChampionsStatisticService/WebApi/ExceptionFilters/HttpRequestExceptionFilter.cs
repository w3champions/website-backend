using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Net.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace W3ChampionsStatisticService.WebApi.ExceptionFilters;

public class HttpRequestExceptionFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        // Ensure that we're propagating HttpRequestException if it bubbles up.
        if (context.Exception is HttpRequestException httpRequestException)
        {
            // If HttpRequestException.StatusCode is null, default to 500
            var statusCode = httpRequestException.StatusCode ?? HttpStatusCode.InternalServerError;
            var errorResponse = new ErrorResult(httpRequestException.Message);

            context.Result = new ObjectResult(errorResponse)
            {
                StatusCode = (int)statusCode
            };
            context.ExceptionHandled = true;
            return;
        }
    }
}
