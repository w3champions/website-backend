using System;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using W3C.Domain.Rewards.Exceptions;

namespace W3ChampionsStatisticService.Rewards.Middleware;

/// <summary>
/// Middleware to handle rewards domain exceptions and convert them to appropriate HTTP responses
/// </summary>
public class RewardsExceptionHandlingMiddleware(RequestDelegate next, ILogger<RewardsExceptionHandlingMiddleware> logger)
{
    private readonly RequestDelegate _next = next;
    private readonly ILogger<RewardsExceptionHandlingMiddleware> _logger = logger;

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var response = context.Response;
        response.ContentType = "application/json";

        var errorResponse = exception switch
        {
            RewardsNotFoundException notFoundEx => new ErrorResponse
            {
                StatusCode = (int)HttpStatusCode.NotFound,
                ErrorCode = notFoundEx.ErrorCode,
                Message = notFoundEx.Message,
                Details = new { ResourceType = notFoundEx.ResourceType, ResourceId = notFoundEx.ResourceId }
            },
            RewardsValidationException validationEx => new ErrorResponse
            {
                StatusCode = (int)HttpStatusCode.BadRequest,
                ErrorCode = validationEx.ErrorCode,
                Message = validationEx.Message,
                Details = new { PropertyName = validationEx.PropertyName }
            },
            RewardsConcurrencyException concurrencyEx => new ErrorResponse
            {
                StatusCode = (int)HttpStatusCode.Conflict,
                ErrorCode = concurrencyEx.ErrorCode,
                Message = concurrencyEx.Message,
                Details = new { ResourceType = concurrencyEx.ResourceType, ResourceId = concurrencyEx.ResourceId }
            },
            OAuthException oauthEx => new ErrorResponse
            {
                StatusCode = (int)HttpStatusCode.BadRequest,
                ErrorCode = oauthEx.ErrorCode,
                Message = oauthEx.Message,
                Details = new { ProviderId = oauthEx.ProviderId }
            },
            RewardAssignmentException assignmentEx => new ErrorResponse
            {
                StatusCode = (int)HttpStatusCode.BadRequest,
                ErrorCode = assignmentEx.ErrorCode,
                Message = assignmentEx.Message,
                Details = new { UserId = assignmentEx.UserId, RewardId = assignmentEx.RewardId }
            },
            RewardRevocationException revocationEx => new ErrorResponse
            {
                StatusCode = (int)HttpStatusCode.BadRequest,
                ErrorCode = revocationEx.ErrorCode,
                Message = revocationEx.Message,
                Details = new { UserId = revocationEx.UserId, RewardId = revocationEx.RewardId }
            },
            ProductMappingException mappingEx => new ErrorResponse
            {
                StatusCode = (int)HttpStatusCode.BadRequest,
                ErrorCode = mappingEx.ErrorCode,
                Message = mappingEx.Message,
                Details = new { ProductMappingId = mappingEx.ProductMappingId }
            },
            ProviderIntegrationException providerEx => new ErrorResponse
            {
                StatusCode = (int)HttpStatusCode.BadRequest,
                ErrorCode = providerEx.ErrorCode,
                Message = providerEx.Message,
                Details = new { ProviderId = providerEx.ProviderId }
            },
            WebhookProcessingException webhookEx => new ErrorResponse
            {
                StatusCode = (int)HttpStatusCode.BadRequest,
                ErrorCode = webhookEx.ErrorCode,
                Message = webhookEx.Message,
                Details = new { WebhookType = webhookEx.WebhookType, PayloadId = webhookEx.PayloadId }
            },
            RewardsDomainException domainEx => new ErrorResponse
            {
                StatusCode = (int)HttpStatusCode.BadRequest,
                ErrorCode = domainEx.ErrorCode,
                Message = domainEx.Message
            },
            _ => new ErrorResponse
            {
                StatusCode = (int)HttpStatusCode.InternalServerError,
                ErrorCode = "INTERNAL_SERVER_ERROR",
                Message = "An internal server error occurred"
            }
        };

        response.StatusCode = errorResponse.StatusCode;

        // Log the exception with appropriate level
        if (errorResponse.StatusCode >= 500)
        {
            _logger.LogError(exception, "Internal server error occurred: {Message}", exception.Message);
        }
        else if (exception is RewardsDomainException)
        {
            _logger.LogWarning(exception, "Domain exception occurred: {ErrorCode} - {Message}", 
                ((RewardsDomainException)exception).ErrorCode, exception.Message);
        }
        else
        {
            _logger.LogInformation(exception, "Client error occurred: {Message}", exception.Message);
        }

        var jsonResponse = JsonSerializer.Serialize(errorResponse, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        await response.WriteAsync(jsonResponse);
    }
}

/// <summary>
/// Standardized error response structure
/// </summary>
public class ErrorResponse
{
    public int StatusCode { get; set; }
    public string ErrorCode { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public object? Details { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}