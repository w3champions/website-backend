using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;

namespace W3ChampionsStatisticService.Services;

/// <summary>
/// One immutable view of the admin roster. An operation that makes more than one admin decision
/// (refuse a block, project the effective friendlist, decide CanBlock, build the response) takes ONE
/// snapshot and uses it for all of them, so a roster refresh mid-operation can't split its decisions
/// across two views.
/// </summary>
public sealed class AdminSnapshot
{
    /// <summary>No admins: the fail-open view used before the roster has ever loaded.</summary>
    public static readonly AdminSnapshot Empty = new([]);

    public AdminSnapshot(IEnumerable<string> battleTags)
    {
        ArgumentNullException.ThrowIfNull(battleTags);
        Tags = battleTags
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .ToFrozenSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Admin BattleTags, case-insensitive. Never null, never changes.</summary>
    public IReadOnlySet<string> Tags { get; }

    /// <summary>False for null/blank tags.</summary>
    public bool IsAdmin(string battleTag) => !string.IsNullOrWhiteSpace(battleTag) && Tags.Contains(battleTag);
}
