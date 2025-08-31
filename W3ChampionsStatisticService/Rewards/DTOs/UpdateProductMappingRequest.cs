using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using W3C.Domain.Rewards.Entities;

namespace W3ChampionsStatisticService.Rewards.DTOs;

public class UpdateProductMappingRequest
{
    [StringLength(200, MinimumLength = 1, ErrorMessage = "ProductName must be between 1 and 200 characters")]
    public string? ProductName { get; set; }

    [MinLength(1, ErrorMessage = "At least one product provider is required when updating providers")]
    public List<ProductProviderPairRequest>? ProductProviders { get; set; }

    [MinLength(1, ErrorMessage = "At least one reward ID is required when updating rewards")]
    public List<string>? RewardIds { get; set; }

    public ProductMappingType? Type { get; set; }

    public Dictionary<string, object>? AdditionalParameters { get; set; }

    public bool? IsActive { get; set; }
}
