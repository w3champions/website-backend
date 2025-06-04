using System.Net;
using System.Net.Http;

namespace W3ChampionsStatisticService.WebApi.ExceptionFilters;

/// <summary>
/// Helper class for throwing HttpRequestException with common status codes.
/// </summary>
public static class HttpExceptionHelper
{
    public static HttpRequestException BadRequest(string message) => 
        new(message, null, HttpStatusCode.BadRequest);
    
    public static HttpRequestException Unauthorized(string message) => 
        new(message, null, HttpStatusCode.Unauthorized);
    
    public static HttpRequestException Forbidden(string message) => 
        new(message, null, HttpStatusCode.Forbidden);
    
    public static HttpRequestException NotFound(string message) => 
        new(message, null, HttpStatusCode.NotFound);
    
    public static HttpRequestException Conflict(string message) => 
        new(message, null, HttpStatusCode.Conflict);
    
    public static HttpRequestException InternalServerError(string message) => 
        new(message, null, HttpStatusCode.InternalServerError);
    
    public static HttpRequestException ServiceUnavailable(string message) => 
        new(message, null, HttpStatusCode.ServiceUnavailable);
} 