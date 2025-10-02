#nullable enable

using System.Collections.Generic;
using System.Threading.Tasks;

namespace W3C.Domain.Common.Services;

/// <summary>
/// Service for logging audit events throughout the system
/// </summary>
public interface IAuditLogService
{
    /// <summary>
    /// Log an admin action for audit purposes
    /// </summary>
    Task LogAdminAction(string battleTag, string action, string entityType, string entityId, object? oldValue = null, object? newValue = null, string? reason = null, Dictionary<string, object>? metadata = null);

    /// <summary>
    /// Log an action with category for better organization
    /// </summary>
    Task LogAction(string battleTag, string category, string action, string entityType, string entityId, string? affectedUserId = null, string? reason = null, object? oldValue = null, object? newValue = null, Dictionary<string, object>? metadata = null);

    /// <summary>
    /// Log a user-related action
    /// </summary>
    Task LogUserAction(string battleTag, string action, string userId, string? reason = null, Dictionary<string, object>? metadata = null);

    /// <summary>
    /// Log a provider integration action
    /// </summary>
    Task LogProviderAction(string battleTag, string providerId, string action, Dictionary<string, object>? metadata = null);

    /// <summary>
    /// Log a system reconciliation/drift action
    /// </summary>
    Task LogSystemAction(string battleTag, string category, string action, string? providerId = null, int? affectedRecords = null, Dictionary<string, object>? metadata = null);
}
