using System;
using System.Text.Json.Serialization;
using W3C.Contracts.Admin.Permission;

namespace W3ChampionsStatisticService.Admin.Jobs;

/// <summary>A job definition merged with its current state, as the admin UI needs it.</summary>
public class AdminJobDto
{
    public string Key { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public EPermission RequiredPermission { get; set; }
    public bool RequiresConfirmation { get; set; }

    // As a name, not an index: the admin UI's status enum mirrors these as strings.
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AdminJobStatus Status { get; set; }
    public AdminJobProgress Progress { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public long? DurationMs { get; set; }
    public long ItemsProcessed { get; set; }
    public string TriggeredBy { get; set; }
    public int RunCount { get; set; }
    public string Error { get; set; }

    /// <summary>Whether a resume point exists, without exposing its job-defined shape.</summary>
    public bool HasCheckpoint { get; set; }

    public static AdminJobDto From(IAdminJob definition, AdminJob state) => new()
    {
        Key = definition.Key,
        Name = definition.Name,
        Description = definition.Description,
        RequiredPermission = definition.RequiredPermission,
        RequiresConfirmation = definition.RequiresConfirmation,

        Status = state?.Status ?? AdminJobStatus.Idle,
        Progress = state?.Progress ?? new AdminJobProgress(),
        StartedAt = state?.StartedAt,
        FinishedAt = state?.FinishedAt,
        DurationMs = state?.DurationMs,
        ItemsProcessed = state?.ItemsProcessed ?? 0,
        TriggeredBy = state?.TriggeredBy,
        RunCount = state?.RunCount ?? 0,
        Error = state?.Error,
        HasCheckpoint = state?.Checkpoint != null,
    };
}
