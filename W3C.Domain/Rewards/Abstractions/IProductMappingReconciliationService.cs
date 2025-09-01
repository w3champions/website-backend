using System.Threading.Tasks;
using W3C.Domain.Rewards.Entities;
using W3C.Domain.Rewards.ValueObjects;

namespace W3C.Domain.Rewards.Abstractions;

/// <summary>
/// Service interface for reconciling product mappings and ensuring consistency between associations and reward assignments
/// </summary>
public interface IProductMappingReconciliationService
{
    /// <summary>
    /// Reconciles a specific product mapping, ensuring all users with associations get the correct reward assignments
    /// </summary>
    /// <param name="mappingId">The product mapping ID to reconcile</param>
    /// <param name="oldMapping">The previous state of the mapping (for comparison)</param>
    /// <param name="newMapping">The new state of the mapping</param>
    /// <param name="dryRun">If true, only analyzes changes without applying them</param>
    /// <returns>Result of the reconciliation process</returns>
    Task<ProductMappingReconciliationResult> ReconcileProductMapping(
        string mappingId,
        ProductMapping oldMapping,
        ProductMapping newMapping,
        bool dryRun = false);

    /// <summary>
    /// Previews what changes would be made during reconciliation for a specific mapping
    /// </summary>
    /// <param name="mappingId">The product mapping ID to preview</param>
    /// <returns>Preview of reconciliation changes</returns>
    Task<ProductMappingReconciliationResult> PreviewReconciliation(string mappingId);

    /// <summary>
    /// Reconciles rewards for a specific user based on their current associations
    /// </summary>
    /// <param name="userId">The user ID to reconcile</param>
    /// <param name="eventIdPrefix">Prefix for generating unique EventIds for reward assignments</param>
    /// <param name="dryRun">If true, only analyzes changes without applying them</param>
    /// <returns>Result of the user reconciliation process</returns>
    Task<ProductMappingReconciliationResult> ReconcileUserAssociations(string userId, string eventIdPrefix, bool dryRun = false);

    /// <summary>
    /// Reconciles all product mappings in the system
    /// </summary>
    /// <param name="dryRun">If true, only analyzes changes without applying them</param>
    /// <returns>Result of the full reconciliation process</returns>
    Task<ProductMappingReconciliationResult> ReconcileAllMappings(bool dryRun = false);
}
