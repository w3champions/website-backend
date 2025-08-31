using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using W3C.Domain.Rewards.Entities;

namespace W3ChampionsStatisticService.Rewards.DTOs;

public class CreateProductMappingRequest
{
    [Required(ErrorMessage = "ProductName is required")]
    [StringLength(200, MinimumLength = 1, ErrorMessage = "ProductName must be between 1 and 200 characters")]
    public string ProductName { get; set; } = string.Empty;

    [Required(ErrorMessage = "At least one product provider is required")]
    [MinLength(1, ErrorMessage = "At least one product provider is required")]
    public List<ProductProviderPairRequest> ProductProviders { get; set; } = new();

    [Required(ErrorMessage = "At least one reward ID is required")]
    [MinLength(1, ErrorMessage = "At least one reward ID is required")]
    public List<string> RewardIds { get; set; } = new();

    [Required(ErrorMessage = "Type is required")]
    public ProductMappingType Type { get; set; }

    public Dictionary<string, object> AdditionalParameters { get; set; } = new();
}

public class ProductProviderPairRequest
{
    [Required(ErrorMessage = "ProviderId is required")]
    [StringLength(50, MinimumLength = 1, ErrorMessage = "ProviderId must be between 1 and 50 characters")]
    [RegularExpression(@"^[a-z_]+$", ErrorMessage = "ProviderId can only contain lowercase letters and underscores")]
    public string ProviderId { get; set; } = string.Empty;

    [Required(ErrorMessage = "ProductId is required")]
    [StringLength(100, MinimumLength = 1, ErrorMessage = "ProductId must be between 1 and 100 characters")]
    public string ProductId { get; set; } = string.Empty;
}