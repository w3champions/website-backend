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
    [JsonProperty(Required = Required.Always)]
    public string BattleTag { get; set; }
    [JsonProperty(Required = Required.Always)]
    public string AddedBy { get; set; }
    [JsonProperty(Required = Required.Always)]
    public DateTime AddedAt { get; set; }
}

public class CommercialEventPeriodUsageDto
{
    [JsonProperty(Required = Required.Always)]
    public string PeriodId { get; set; }
    [JsonProperty(Required = Required.Always)]
    public DateTime PeriodStart { get; set; }
    [JsonProperty(Required = Required.Always)]
    public DateTime PeriodEnd { get; set; }
    [JsonProperty(Required = Required.Always)]
    public int Size { get; set; }
    [JsonProperty(Required = Required.Always)]
    public int Consumed { get; set; }
    [JsonProperty(Required = Required.Always)]
    public int Held { get; set; }
    [JsonProperty(Required = Required.Always)]
    public int Invalid { get; set; }
    [JsonProperty(Required = Required.Always)]
    public int Used { get; set; }
    [JsonProperty(Required = Required.Always)]
    public int Available { get; set; }

    /// <summary>"none", "high" or "empty".</summary>
    [JsonProperty(Required = Required.Always)]
    public string Warning { get; set; }
}

public class CommercialEventAllocationDto
{
    [JsonProperty(Required = Required.Always)]
    public string Id { get; set; }
    [JsonProperty(Required = Required.Always)]
    public string Name { get; set; }
    [JsonProperty(Required = Required.Always)]
    public int GamesPerPeriod { get; set; }

    /// <summary>"once", "weekly" or "monthly".</summary>
    [JsonProperty(Required = Required.Always)]
    public string Recurrence { get; set; }

    [JsonProperty(Required = Required.Always)]
    public DateTime StartsAt { get; set; }
    [JsonProperty(Required = Required.Always)]
    public DateTime EndsAt { get; set; }
    [JsonProperty(Required = Required.Always)]
    public bool AllowEventCreation { get; set; }
    public string AdminNote { get; set; }

    /// <summary>"upcoming", "active" or "expired".</summary>
    [JsonProperty(Required = Required.Always)]
    public string State { get; set; }

    [JsonProperty(Required = Required.Always)]
    public List<CommercialEventRoleEntryDto> Members { get; set; }

    /// <summary>Upcoming: the first period; active: the current one; expired: null.</summary>
    [JsonProperty(Required = Required.AllowNull)]
    public CommercialEventPeriodUsageDto CurrentPeriod { get; set; }

    [JsonProperty(Required = Required.AllowNull)]
    public DateTime? ResetsAt { get; set; }
    [JsonProperty(Required = Required.Always)]
    public string CreatedBy { get; set; }
    [JsonProperty(Required = Required.Always)]
    public DateTime CreatedAt { get; set; }
    [JsonProperty(Required = Required.Always)]
    public string UpdatedBy { get; set; }
    [JsonProperty(Required = Required.Always)]
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
    [JsonProperty(Required = Required.Always)]
    public string Id { get; set; }
    [JsonProperty(Required = Required.Always)]
    public string Name { get; set; }

    /// <summary>"show-matches", "tournament" or "other".</summary>
    [JsonProperty(Required = Required.Always)]
    public string Kind { get; set; }

    [JsonProperty(Required = Required.Always)]
    public int PrizePoolUsd { get; set; }
    [JsonProperty(Required = Required.Always)]
    public DateTime StartsAt { get; set; }
    [JsonProperty(Required = Required.Always)]
    public DateTime EndsAt { get; set; }
    [JsonProperty(Required = Required.Always)]
    public int MaxGames { get; set; }
    [JsonProperty(Required = Required.Always)]
    public string AllocationId { get; set; }
    [JsonProperty(Required = Required.Always)]
    public string AllocationName { get; set; }

    /// <summary>Effective status: "open", "suspended" or "closed".</summary>
    [JsonProperty(Required = Required.Always)]
    public string Status { get; set; }

    /// <summary>"upcoming" or "active"; null unless the status is open.</summary>
    public string Phase { get; set; }

    [JsonProperty(Required = Required.Always)]
    public int Consumed { get; set; }
    [JsonProperty(Required = Required.Always)]
    public int Held { get; set; }
    [JsonProperty(Required = Required.Always)]
    public int Invalid { get; set; }
    public string SuspensionMessage { get; set; }
    public string AdminNote { get; set; }
    [JsonProperty(Required = Required.Always)]
    public string CreatedBy { get; set; }

    /// <summary>"admin" or "launcher".</summary>
    [JsonProperty(Required = Required.Always)]
    public string CreatedVia { get; set; }

    [JsonProperty(Required = Required.Always)]
    public DateTime CreatedAt { get; set; }
    [JsonProperty(Required = Required.Always)]
    public string UpdatedBy { get; set; }
    [JsonProperty(Required = Required.Always)]
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
    [JsonProperty(Required = Required.Always)]
    public List<string> Organizers { get; set; }

    [JsonProperty(Required = Required.Always)]
    public List<CommercialEventRoleEntryDto> Delegates { get; set; }
    [JsonProperty(Required = Required.Always)]
    public List<CommercialEventRoleEntryDto> Hosts { get; set; }
}

public class CommercialEventPeopleDto
{
    /// <summary>Members of the event's current allocation.</summary>
    [JsonProperty(Required = Required.Always)]
    public List<string> Organizers { get; set; }

    [JsonProperty(Required = Required.Always)]
    public List<CommercialEventRoleEntryDto> Delegates { get; set; }
    [JsonProperty(Required = Required.Always)]
    public List<CommercialEventRoleEntryDto> Hosts { get; set; }
}

public class CommercialEventGamePlayerDto
{
    [JsonProperty(Required = Required.Always)]
    public string BattleTag { get; set; }
    [JsonProperty(Required = Required.Always)]
    public bool Won { get; set; }
}

public class CommercialEventGameTeamDto
{
    [JsonProperty(Required = Required.Always)]
    public int PlayerCount { get; set; }
    [JsonProperty(Required = Required.Always)]
    public List<CommercialEventGamePlayerDto> Players { get; set; }
}

public class CommercialEventGameDto
{
    [JsonProperty(Required = Required.Always)]
    public string MatchId { get; set; }
    [JsonProperty(Required = Required.Always)]
    public string LobbyName { get; set; }
    [JsonProperty(Required = Required.Always)]
    public DateTime StartedAt { get; set; }

    /// <summary>"in-progress", "valid" or "invalid".</summary>
    [JsonProperty(Required = Required.Always)]
    public string Outcome { get; set; }

    /// <summary>"start-failed", "no-result", "terminated" or "no-winner"; null unless the outcome is invalid.</summary>
    public string InvalidReason { get; set; }

    public int? LengthSeconds { get; set; }

    /// <summary>Always false on admin routes (names are always shown).</summary>
    [JsonProperty(Required = Required.Always)]
    public bool NamesHidden { get; set; }

    public string Host { get; set; }
    [JsonProperty(Required = Required.Always)]
    public List<CommercialEventGameTeamDto> Teams { get; set; }
    [JsonProperty(Required = Required.Always)]
    public int ObserverCount { get; set; }
    [JsonProperty(Required = Required.Always)]
    public List<string> Observers { get; set; }
    [JsonProperty(Required = Required.Always)]
    public int Computers { get; set; }
    [JsonProperty(Required = Required.Always)]
    public int ViewerCount { get; set; }
    [JsonProperty(Required = Required.Always)]
    public long WatchedSecondsTotal { get; set; }
    [JsonProperty(Required = Required.Always)]
    public long WatchedSecondsAvg { get; set; }
}

public class CommercialEventGamesPageDto
{
    [JsonProperty(Required = Required.Always)]
    public List<CommercialEventGameDto> Games { get; set; }

    /// <summary>Opaque; null on the last page.</summary>
    public string NextCursor { get; set; }
}

public class CommercialEventActiveGameDto
{
    [JsonProperty(Required = Required.Always)]
    public string MatchId { get; set; }
    [JsonProperty(Required = Required.Always)]
    public string EventId { get; set; }
    [JsonProperty(Required = Required.Always)]
    public string EventName { get; set; }
    [JsonProperty(Required = Required.Always)]
    public string Host { get; set; }
    [JsonProperty(Required = Required.Always)]
    public string LobbyName { get; set; }
    [JsonProperty(Required = Required.Always)]
    public DateTime StartedAt { get; set; }
    [JsonProperty(Required = Required.Always)]
    public List<string> Players { get; set; }
    [JsonProperty(Required = Required.Always)]
    public List<string> Observers { get; set; }
    [JsonProperty(Required = Required.Always)]
    public int ViewerCount { get; set; }
}

public class CommercialEventAuditEntryDto
{
    [JsonProperty(Required = Required.Always)]
    public string Id { get; set; }
    [JsonProperty(Required = Required.Always)]
    public DateTime At { get; set; }

    /// <summary>A battle tag, or "system".</summary>
    [JsonProperty(Required = Required.Always)]
    public string Actor { get; set; }

    /// <summary>"admin", "organizer", "delegate" or "system".</summary>
    [JsonProperty(Required = Required.Always)]
    public string ActorRole { get; set; }

    [JsonProperty(Required = Required.Always)]
    public string Action { get; set; }
    public string EventId { get; set; }
    public string AllocationId { get; set; }

    /// <summary>
    /// Free-form JSON object. ExpandoObject rather than JObject because website responses are written by
    /// System.Text.Json, which serializes a JObject as nested empty arrays.
    /// </summary>
    [JsonProperty(Required = Required.Always)]
    [JsonConverter(typeof(ExpandoObjectConverter))]
    public ExpandoObject Details { get; set; }
}

public class CommercialEventAllocationRefDto
{
    [JsonProperty(Required = Required.Always)]
    public string AllocationId { get; set; }
    [JsonProperty(Required = Required.Always)]
    public string AllocationName { get; set; }
}

public class CommercialEventRefDto
{
    [JsonProperty(Required = Required.Always)]
    public string EventId { get; set; }
    [JsonProperty(Required = Required.Always)]
    public string EventName { get; set; }
}

public class CommercialEventRoleHintsDto
{
    [JsonProperty(Required = Required.Always)]
    public string BattleTag { get; set; }
    [JsonProperty(Required = Required.Always)]
    public List<CommercialEventAllocationRefDto> OrganizerOf { get; set; }
    [JsonProperty(Required = Required.Always)]
    public List<CommercialEventRefDto> DelegateOf { get; set; }
    [JsonProperty(Required = Required.Always)]
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
