using System.Collections.Generic;
using System.Threading.Tasks;
using W3C.Domain.Rewards.Entities;

namespace W3C.Domain.Rewards.Abstractions;

public interface IRewardModule
{
    string ModuleId { get; }
    string ModuleName { get; }
    string Description { get; }
    bool SupportsParameters { get; }

    Task<RewardApplicationResult> Apply(RewardContext context);
    Task<RewardRevocationResult> Revoke(RewardContext context);
    Task<ValidationResult> ValidateParameters(Dictionary<string, object> parameters);
    Dictionary<string, ParameterDefinition> GetParameterDefinitions();
}

public class RewardContext
{
    public string UserId { get; set; }
    public string RewardId { get; set; }
    public Dictionary<string, object> Parameters { get; set; }
    public RewardAssignment Assignment { get; set; }
}

public class RewardApplicationResult
{
    public bool Success { get; set; }
    public string Message { get; set; }
    public Dictionary<string, object> ResultData { get; set; }
}

public class RewardRevocationResult
{
    public bool Success { get; set; }
    public string Message { get; set; }
}

public class ValidationResult
{
    public bool IsValid { get; set; }
    public List<string> Errors { get; set; } = new();
}

public class ParameterDefinition
{
    public string Name { get; set; }
    public string Type { get; set; }
    public bool Required { get; set; }
    public string Description { get; set; }
    public object DefaultValue { get; set; }
}
