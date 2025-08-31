using System;
using W3C.Domain.Common.Exceptions;

namespace W3C.Domain.Rewards.Exceptions;

/// <summary>
/// Base exception for all rewards domain-specific exceptions
/// </summary>
public abstract class RewardsDomainException : Exception
{
    protected RewardsDomainException(string message) : base(message) { }
    protected RewardsDomainException(string message, Exception innerException) : base(message, innerException) { }

    /// <summary>
    /// Error code for API responses and logging
    /// </summary>
    public abstract string ErrorCode { get; }
}

/// <summary>
/// Thrown when a reward assignment operation fails
/// </summary>
public class RewardAssignmentException : RewardsDomainException
{
    public override string ErrorCode => "REWARD_ASSIGNMENT_FAILED";

    public string? UserId { get; }
    public string? RewardId { get; }

    public RewardAssignmentException(string message) : base(message) { }

    public RewardAssignmentException(string message, string userId, string rewardId)
        : base(message)
    {
        UserId = userId;
        RewardId = rewardId;
    }

    public RewardAssignmentException(string message, Exception innerException)
        : base(message, innerException) { }
}

/// <summary>
/// Thrown when a reward revocation operation fails
/// </summary>
public class RewardRevocationException : RewardsDomainException
{
    public override string ErrorCode => "REWARD_REVOCATION_FAILED";

    public string? UserId { get; }
    public string? RewardId { get; }

    public RewardRevocationException(string message) : base(message) { }

    public RewardRevocationException(string message, string userId, string rewardId)
        : base(message)
    {
        UserId = userId;
        RewardId = rewardId;
    }

    public RewardRevocationException(string message, Exception innerException)
        : base(message, innerException) { }
}

/// <summary>
/// Thrown when a product mapping operation fails
/// </summary>
public class ProductMappingException : RewardsDomainException
{
    public override string ErrorCode => "PRODUCT_MAPPING_FAILED";

    public string? ProductMappingId { get; }

    public ProductMappingException(string message) : base(message) { }

    public ProductMappingException(string message, string productMappingId)
        : base(message)
    {
        ProductMappingId = productMappingId;
    }

    public ProductMappingException(string message, Exception innerException)
        : base(message, innerException) { }
}

/// <summary>
/// Thrown when provider integration operations fail
/// </summary>
public class ProviderIntegrationException : RewardsDomainException
{
    public override string ErrorCode => "PROVIDER_INTEGRATION_FAILED";

    public string? ProviderId { get; }

    public ProviderIntegrationException(string message) : base(message) { }

    public ProviderIntegrationException(string message, string providerId)
        : base(message)
    {
        ProviderId = providerId;
    }

    public ProviderIntegrationException(string message, Exception innerException)
        : base(message, innerException) { }
}

/// <summary>
/// Thrown when webhook processing fails
/// </summary>
public class WebhookProcessingException : RewardsDomainException
{
    public override string ErrorCode => "WEBHOOK_PROCESSING_FAILED";

    public string? WebhookType { get; }
    public string? PayloadId { get; }

    public WebhookProcessingException(string message) : base(message) { }

    public WebhookProcessingException(string message, string webhookType, string? payloadId = null)
        : base(message)
    {
        WebhookType = webhookType;
        PayloadId = payloadId;
    }

    public WebhookProcessingException(string message, Exception innerException)
        : base(message, innerException) { }
}

/// <summary>
/// Thrown when OAuth operations fail
/// </summary>
public class OAuthException : RewardsDomainException
{
    public override string ErrorCode => "OAUTH_FAILED";

    public string? ProviderId { get; }

    public OAuthException(string message) : base(message) { }

    public OAuthException(string message, string providerId)
        : base(message)
    {
        ProviderId = providerId;
    }

    public OAuthException(string message, Exception innerException)
        : base(message, innerException) { }
}

/// <summary>
/// Thrown when resource is not found
/// </summary>
public class RewardsNotFoundException : RewardsDomainException
{
    public override string ErrorCode => "RESOURCE_NOT_FOUND";

    public string? ResourceType { get; }
    public string? ResourceId { get; }

    public RewardsNotFoundException(string message) : base(message) { }

    public RewardsNotFoundException(string resourceType, string resourceId)
        : base($"{resourceType} with ID '{resourceId}' was not found")
    {
        ResourceType = resourceType;
        ResourceId = resourceId;
    }

    public RewardsNotFoundException(string message, Exception innerException)
        : base(message, innerException) { }
}

/// <summary>
/// Thrown when validation fails
/// </summary>
public class RewardsValidationException : RewardsDomainException
{
    public override string ErrorCode => "VALIDATION_FAILED";

    public string? PropertyName { get; }

    public RewardsValidationException(string message) : base(message) { }

    public RewardsValidationException(string message, string propertyName)
        : base(message)
    {
        PropertyName = propertyName;
    }

    public RewardsValidationException(string message, Exception innerException)
        : base(message, innerException) { }
}

/// <summary>
/// Thrown when concurrency conflict occurs in the rewards domain
/// </summary>
public class RewardsConcurrencyException : ConcurrencyException
{
    public string ErrorCode => "CONCURRENCY_CONFLICT";

    public RewardsConcurrencyException(string message) : base(message) { }

    public RewardsConcurrencyException(string resourceType, string resourceId)
        : base(resourceType, resourceId) { }

    public RewardsConcurrencyException(string resourceType, string resourceId, string customMessage)
        : base(resourceType, resourceId, customMessage) { }

    public RewardsConcurrencyException(string message, Exception innerException)
        : base(message, innerException) { }
}
