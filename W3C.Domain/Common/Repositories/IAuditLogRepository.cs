using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using W3C.Domain.Common.Entities;

namespace W3C.Domain.Common.Repositories;

/// <summary>
/// Repository for audit log entries
/// </summary>
public interface IAuditLogRepository
{
    /// <summary>
    /// Create a new audit log entry
    /// </summary>
    Task Create(AuditLogEntry entry);

    /// <summary>
    /// Get audit logs for a specific admin
    /// </summary>
    Task<List<AuditLogEntry>> GetByAdmin(string battleTag, DateTime? fromDate = null, DateTime? toDate = null, int limit = 100);

    /// <summary>
    /// Get audit logs for a specific user (affected user)
    /// </summary>
    Task<List<AuditLogEntry>> GetByAffectedUser(string userId, DateTime? fromDate = null, DateTime? toDate = null, int limit = 100);

    /// <summary>
    /// Get audit logs by category
    /// </summary>
    Task<List<AuditLogEntry>> GetByCategory(string category, DateTime? fromDate = null, DateTime? toDate = null, int limit = 100);

    /// <summary>
    /// Get recent audit logs
    /// </summary>
    Task<List<AuditLogEntry>> GetRecent(int limit = 100);

    /// <summary>
    /// Search audit logs by entity type and ID
    /// </summary>
    Task<List<AuditLogEntry>> GetByEntity(string entityType, string entityId);
}
