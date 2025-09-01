#nullable enable

using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using W3C.Domain.Rewards.ValueObjects;

namespace W3ChampionsStatisticService.Rewards.DTOs;

public class UpdateRewardRequest
{
    [StringLength(100, MinimumLength = 1, ErrorMessage = "DisplayId must be between 1 and 100 characters")]
    [RegularExpression(@"^[a-zA-Z0-9_\-]+$", ErrorMessage = "DisplayId can only contain letters, numbers, underscores and hyphens")]
    public string? DisplayId { get; set; }

    public Dictionary<string, object>? Parameters { get; set; }

    public RewardDuration? Duration { get; set; }

    public bool? IsActive { get; set; }
}
