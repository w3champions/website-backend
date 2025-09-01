#nullable enable

using System;

namespace W3C.Domain.Common.Exceptions;

/// <summary>
/// Base exception for concurrency conflicts throughout the system
/// </summary>
public class ConcurrencyException : Exception
{
    public string? ResourceType { get; }
    public string? ResourceId { get; }

    public ConcurrencyException(string message) : base(message) { }

    public ConcurrencyException(string resourceType, string resourceId)
        : base($"Concurrency conflict detected for {resourceType} with ID '{resourceId}'. The resource was modified by another process.")
    {
        ResourceType = resourceType;
        ResourceId = resourceId;
    }

    public ConcurrencyException(string resourceType, string resourceId, string customMessage)
        : base(customMessage)
    {
        ResourceType = resourceType;
        ResourceId = resourceId;
    }

    public ConcurrencyException(string message, Exception innerException)
        : base(message, innerException) { }
}
