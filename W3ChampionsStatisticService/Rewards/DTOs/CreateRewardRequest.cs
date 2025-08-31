using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using W3C.Domain.Rewards.ValueObjects;

namespace W3ChampionsStatisticService.Rewards.DTOs;

public class CreateRewardRequest
{
    [Required(ErrorMessage = "DisplayId is required")]
    [StringLength(100, MinimumLength = 1, ErrorMessage = "DisplayId must be between 1 and 100 characters")]
    [RegularExpression(@"^[a-zA-Z0-9_\-]+$", ErrorMessage = "DisplayId can only contain letters, numbers, underscores and hyphens")]
    public string DisplayId { get; set; } = string.Empty;

    [Required(ErrorMessage = "ModuleId is required")]
    [RegularExpression(@"^[a-z_]+$", ErrorMessage = "ModuleId can only contain lowercase letters and underscores")]
    [StringLength(50, MinimumLength = 1, ErrorMessage = "ModuleId must be between 1 and 50 characters")]
    public string ModuleId { get; set; } = string.Empty;

    public Dictionary<string, object> Parameters { get; set; } = new();

    public RewardDuration Duration { get; set; } = RewardDuration.Permanent();
}