using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using W3C.Domain.Common.Entities;
using W3C.Domain.Common.Repositories;
using W3C.Domain.Common.Services;

namespace W3ChampionsStatisticService.Common.Services;

/// <summary>
/// Implementation of audit logging service for the entire system
/// </summary>
public class AuditLogService : IAuditLogService
{
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<AuditLogService> _logger;

    public AuditLogService(
        IAuditLogRepository auditLogRepository,
        IHttpContextAccessor httpContextAccessor,
        ILogger<AuditLogService> logger)
    {
        _auditLogRepository = auditLogRepository;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    public async Task LogAdminAction(string battleTag, string action, string entityType, string entityId, 
        object? oldValue = null, object? newValue = null, Dictionary<string, object>? metadata = null)
    {
        await LogAction(battleTag, "ADMIN", action, entityType, entityId, 
            affectedUserId: null, reason: null, oldValue: oldValue, newValue: newValue, metadata: metadata);
    }

    public async Task LogAction(string battleTag, string category, string action, string entityType, string entityId, 
        string? affectedUserId = null, string? reason = null, object? oldValue = null, object? newValue = null, Dictionary<string, object>? metadata = null)
    {
        try
        {
            var entry = CreateBaseEntry(battleTag, action, category, entityType, entityId, metadata);
            
            entry.AffectedUserId = affectedUserId;
            entry.Reason = reason;
            
            if (oldValue != null)
            {
                entry.OldValue = JsonSerializer.Serialize(oldValue);
            }
            
            if (newValue != null)
            {
                entry.NewValue = JsonSerializer.Serialize(newValue);
            }

            await _auditLogRepository.Create(entry);
            
            _logger.LogInformation("Audit log created: Admin {BattleTag} performed {Action} on {EntityType} {EntityId} in category {Category}", 
                battleTag, action, entityType, entityId, category);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create audit log entry for action by {BattleTag}", battleTag);
        }
    }

    public async Task LogUserAction(string battleTag, string action, string userId, string? reason = null, Dictionary<string, object>? metadata = null)
    {
        await LogAction(battleTag, "USER", action, "User", userId, 
            affectedUserId: userId, reason: reason, metadata: metadata);
    }

    public async Task LogProviderAction(string battleTag, string providerId, string action, Dictionary<string, object>? metadata = null)
    {
        try
        {
            var entry = CreateBaseEntry(battleTag, action, "PROVIDER", "ProviderConfiguration", providerId, metadata);
            entry.ProviderId = providerId;

            await _auditLogRepository.Create(entry);
            
            _logger.LogInformation("Audit log created: Admin {BattleTag} performed {Action} on provider {ProviderId}", 
                battleTag, action, providerId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create audit log entry for provider action by {BattleTag}", battleTag);
        }
    }

    public async Task LogSystemAction(string battleTag, string category, string action, string? providerId = null, 
        int? affectedRecords = null, Dictionary<string, object>? metadata = null)
    {
        try
        {
            var entityId = providerId != null ? $"{providerId}_{DateTime.UtcNow:yyyyMMddHHmmss}" : $"system_{DateTime.UtcNow:yyyyMMddHHmmss}";
            var entry = CreateBaseEntry(battleTag, action, category, "SystemAction", entityId, metadata);
            
            entry.ProviderId = providerId;
            
            if (affectedRecords.HasValue)
            {
                entry.Metadata["affected_records"] = affectedRecords.Value;
            }
            
            entry.Metadata["execution_time"] = DateTime.UtcNow;

            await _auditLogRepository.Create(entry);
            
            var logMessage = providerId != null 
                ? "Audit log created: Admin {BattleTag} performed system {Action} on provider {ProviderId}" 
                : "Audit log created: Admin {BattleTag} performed system {Action}";
                
            if (affectedRecords.HasValue && providerId != null)
            {
                _logger.LogInformation(logMessage + " affecting {Count} records", battleTag, action, providerId, affectedRecords.Value);
            }
            else if (providerId != null)
            {
                _logger.LogInformation(logMessage, battleTag, action, providerId);
            }
            else
            {
                _logger.LogInformation("Audit log created: Admin {BattleTag} performed system {Action}", battleTag, action);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create audit log entry for system action by {BattleTag}", battleTag);
        }
    }

    private AuditLogEntry CreateBaseEntry(string battleTag, string action, string category, string entityType, string entityId, 
        Dictionary<string, object>? metadata = null)
    {
        var httpContext = _httpContextAccessor.HttpContext;
        
        return new AuditLogEntry
        {
            AdminBattleTag = battleTag,
            Timestamp = DateTime.UtcNow,
            Action = action,
            Category = category,
            EntityType = entityType,
            EntityId = entityId,
            Metadata = metadata ?? new Dictionary<string, object>(),
            IpAddress = httpContext?.Connection?.RemoteIpAddress?.ToString(),
            UserAgent = httpContext?.Request?.Headers["User-Agent"].ToString()
        };
    }
}