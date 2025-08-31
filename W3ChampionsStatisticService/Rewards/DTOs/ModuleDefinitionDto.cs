using System.Collections.Generic;

namespace W3ChampionsStatisticService.Rewards.DTOs;

public class ModuleDefinitionDto
{
    public string Id { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public string Type { get; set; }
    public bool IsActive { get; set; }
    
    // Additional properties expected by RewardController
    public string ModuleId { get; set; }
    public string ModuleName { get; set; }
    public bool SupportsParameters { get; set; }
    public Dictionary<string, object> ParameterDefinitions { get; set; } = new();
}