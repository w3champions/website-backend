using System;
using System.Collections.Generic;

namespace W3C.Domain.MatchmakingService;

// Typed mirrors of the matchmaking commercial-events admin API (contract C-E3). Enum-like values are strings, so a new
// value from matchmaking cannot break deserialization. Request bodies have no acting battle tag property: the client
// adds actingBattleTag from the bearer token, so a website body can never supply it.

public class CommercialEventRoleEntryDto
{
    public string BattleTag { get; set; }
    public string AddedBy { get; set; }
    public DateTime AddedAt { get; set; }
}

public class CommercialEventPeriodUsageDto
{
    public string PeriodId { get; set; }
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }
    public int Size { get; set; }
    public int Consumed { get; set; }
    public int Held { get; set; }
    public int Invalid { get; set; }
    public int Used { get; set; }
    public int Available { get; set; }

    /// <summary>"none", "high" or "empty".</summary>
    public string Warning { get; set; }
}

public class CommercialEventAllocationDto
{
    public string Id { get; set; }
    public string Name { get; set; }
    public int GamesPerPeriod { get; set; }

    /// <summary>"once", "weekly" or "monthly".</summary>
    public string Recurrence { get; set; }

    public DateTime StartsAt { get; set; }
    public DateTime EndsAt { get; set; }
    public bool AllowEventCreation { get; set; }
    public string AdminNote { get; set; }

    /// <summary>"upcoming", "active" or "expired".</summary>
    public string State { get; set; }

    public List<CommercialEventRoleEntryDto> Members { get; set; }

    /// <summary>Upcoming: the first period; active: the current one; expired: null.</summary>
    public CommercialEventPeriodUsageDto CurrentPeriod { get; set; }

    public DateTime? ResetsAt { get; set; }
    public string CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public string UpdatedBy { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>Allocation create and update body. Null fields are omitted when forwarded; matchmaking validates them.</summary>
public class CommercialEventAllocationRequest
{
    public string Name { get; set; }
    public int? GamesPerPeriod { get; set; }
    public string Recurrence { get; set; }
    public DateTime? StartsAt { get; set; }
    public DateTime? EndsAt { get; set; }
    public bool? AllowEventCreation { get; set; }
    public string AdminNote { get; set; }
}
