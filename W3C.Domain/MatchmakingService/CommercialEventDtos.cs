using System;
using System.Collections.Generic;
using System.Dynamic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

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

public class CommercialEventDto
{
    public string Id { get; set; }
    public string Name { get; set; }

    /// <summary>"show-matches", "tournament" or "other".</summary>
    public string Kind { get; set; }

    public int PrizePoolUsd { get; set; }
    public DateTime StartsAt { get; set; }
    public DateTime EndsAt { get; set; }
    public int MaxGames { get; set; }
    public string AllocationId { get; set; }
    public string AllocationName { get; set; }

    /// <summary>Effective status: "open", "suspended" or "closed".</summary>
    public string Status { get; set; }

    /// <summary>"upcoming" or "active"; null unless the status is open.</summary>
    public string Phase { get; set; }

    public int Consumed { get; set; }
    public int Held { get; set; }
    public int Invalid { get; set; }
    public string SuspensionMessage { get; set; }
    public string AdminNote { get; set; }
    public string CreatedBy { get; set; }

    /// <summary>"admin" or "launcher".</summary>
    public string CreatedVia { get; set; }

    public DateTime CreatedAt { get; set; }
    public string UpdatedBy { get; set; }
    public DateTime UpdatedAt { get; set; }

    /// <summary>"system" for automatic closes.</summary>
    public string ClosedBy { get; set; }

    public DateTime? ClosedAt { get; set; }
    public string SuspendedBy { get; set; }
    public DateTime? SuspendedAt { get; set; }
}

public class CommercialEventDetailDto : CommercialEventDto
{
    /// <summary>Members of the event's current allocation.</summary>
    public List<string> Organizers { get; set; }

    public List<CommercialEventRoleEntryDto> Delegates { get; set; }
    public List<CommercialEventRoleEntryDto> Hosts { get; set; }
}

public class CommercialEventPeopleDto
{
    /// <summary>Members of the event's current allocation.</summary>
    public List<string> Organizers { get; set; }

    public List<CommercialEventRoleEntryDto> Delegates { get; set; }
    public List<CommercialEventRoleEntryDto> Hosts { get; set; }
}

public class CommercialEventGamePlayerDto
{
    public string BattleTag { get; set; }
    public bool Won { get; set; }
}

public class CommercialEventGameTeamDto
{
    public int PlayerCount { get; set; }
    public List<CommercialEventGamePlayerDto> Players { get; set; }
}

public class CommercialEventGameDto
{
    public string MatchId { get; set; }
    public string LobbyName { get; set; }
    public DateTime StartedAt { get; set; }

    /// <summary>"in-progress", "valid" or "invalid".</summary>
    public string Outcome { get; set; }

    /// <summary>"start-failed", "no-result", "terminated" or "no-winner"; null unless the outcome is invalid.</summary>
    public string InvalidReason { get; set; }

    public int? LengthSeconds { get; set; }

    /// <summary>Always false on admin routes (names are always shown).</summary>
    public bool NamesHidden { get; set; }

    public string Host { get; set; }
    public List<CommercialEventGameTeamDto> Teams { get; set; }
    public int ObserverCount { get; set; }
    public List<string> Observers { get; set; }
    public int Computers { get; set; }
    public int ViewerCount { get; set; }
    public long WatchedSecondsTotal { get; set; }
    public long WatchedSecondsAvg { get; set; }
}

public class CommercialEventGamesPageDto
{
    public List<CommercialEventGameDto> Games { get; set; }

    /// <summary>Opaque; null on the last page.</summary>
    public string NextCursor { get; set; }
}

public class CommercialEventActiveGameDto
{
    public string MatchId { get; set; }
    public string EventId { get; set; }
    public string EventName { get; set; }
    public string Host { get; set; }
    public string LobbyName { get; set; }
    public DateTime StartedAt { get; set; }
    public List<string> Players { get; set; }
    public List<string> Observers { get; set; }
    public int ViewerCount { get; set; }
}

public class CommercialEventAuditEntryDto
{
    public string Id { get; set; }
    public DateTime At { get; set; }

    /// <summary>A battle tag, or "system".</summary>
    public string Actor { get; set; }

    /// <summary>"admin", "organizer", "delegate" or "system".</summary>
    public string ActorRole { get; set; }

    public string Action { get; set; }
    public string EventId { get; set; }
    public string AllocationId { get; set; }

    /// <summary>
    /// Free-form JSON object. ExpandoObject rather than JObject because website responses are written by
    /// System.Text.Json, which serializes a JObject as nested empty arrays.
    /// </summary>
    [JsonConverter(typeof(ExpandoObjectConverter))]
    public ExpandoObject Details { get; set; }
}

public class CommercialEventAllocationRefDto
{
    public string AllocationId { get; set; }
    public string AllocationName { get; set; }
}

public class CommercialEventRefDto
{
    public string EventId { get; set; }
    public string EventName { get; set; }
}

public class CommercialEventRoleHintsDto
{
    public string BattleTag { get; set; }
    public List<CommercialEventAllocationRefDto> OrganizerOf { get; set; }
    public List<CommercialEventRefDto> DelegateOf { get; set; }
    public List<CommercialEventRefDto> HostOf { get; set; }
}

/// <summary>Event update body. Null fields are omitted when forwarded; matchmaking validates them.</summary>
public class CommercialEventUpdateRequest
{
    public string Name { get; set; }
    public string Kind { get; set; }
    public int? PrizePoolUsd { get; set; }
    public DateTime? StartsAt { get; set; }
    public DateTime? EndsAt { get; set; }
    public int? MaxGames { get; set; }
    public string AdminNote { get; set; }
}

/// <summary>Event create body: the update fields plus the allocation.</summary>
public class CommercialEventCreateRequest : CommercialEventUpdateRequest
{
    public string AllocationId { get; set; }
}

public class CommercialEventMoveRequest
{
    public string AllocationId { get; set; }
}

public class CommercialEventSuspendRequest
{
    public string SuspensionMessage { get; set; }
    public string AdminNote { get; set; }
}

public class CommercialEventPersonRequest
{
    /// <summary>"delegate" or "host".</summary>
    public string Role { get; set; }
}

/// <summary>Role-hint lookup body (index P41). A read: it carries no acting admin.</summary>
public class CommercialEventRoleLookupRequest
{
    public List<string> BattleTags { get; set; }
}
