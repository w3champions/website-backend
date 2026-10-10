namespace WC3ChampionsStatisticService.Tests.Admin;

/// <summary>Matchmaking commercial-events admin API bodies (contract C-E3), shared by the client and controller tests.</summary>
internal static class CommercialEventsTestJson
{
    public const string Allocation =
        """{"id":"alloc-1","name":"Weekly showmatches","gamesPerPeriod":20,"recurrence":"weekly","startsAt":"2026-10-01T00:00:00.000Z","endsAt":"2026-12-31T00:00:00.000Z","allowEventCreation":true,"adminNote":"","state":"active","members":[{"battleTag":"Organizer#1234","addedBy":"Admin#1","addedAt":"2026-10-01T10:00:00.000Z"}],"currentPeriod":{"periodId":"alloc-1:2026-10-08T00:00:00.000Z","periodStart":"2026-10-08T00:00:00.000Z","periodEnd":"2026-10-15T00:00:00.000Z","size":20,"consumed":5,"held":1,"invalid":2,"used":6,"available":14,"warning":"none"},"resetsAt":"2026-10-15T00:00:00.000Z","createdBy":"Admin#1","createdAt":"2026-10-01T09:00:00.000Z","updatedBy":"Admin#2","updatedAt":"2026-10-02T09:00:00.000Z"}""";

    public const string Allocations = "[" + Allocation + "]";

    public const string Period =
        """{"periodId":"alloc-1:2026-10-01T00:00:00.000Z","periodStart":"2026-10-01T00:00:00.000Z","periodEnd":"2026-10-08T00:00:00.000Z","size":20,"consumed":18,"held":0,"invalid":3,"used":18,"available":2,"warning":"high"}""";

    public const string Periods = "[" + Period + "]";

    public const string AllocationInUseError = """{"error":"ALLOCATION_IN_USE","code":"ALLOCATION_IN_USE"}""";
}
