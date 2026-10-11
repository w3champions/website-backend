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

    public const string Event =
        """{"id":"EV-7K3M","name":"Friday Showmatch","kind":"show-matches","prizePoolUsd":500,"startsAt":"2026-10-09T18:00:00.000Z","endsAt":"2026-10-09T23:00:00.000Z","maxGames":10,"allocationId":"alloc-1","allocationName":"Weekly showmatches","status":"open","phase":"active","consumed":3,"held":1,"invalid":2,"adminNote":"","createdBy":"Organizer#1234","createdVia":"launcher","createdAt":"2026-10-08T10:00:00.000Z","updatedBy":"Organizer#1234","updatedAt":"2026-10-08T10:00:00.000Z"}""";

    public const string Events = "[" + Event + "]";

    public const string EventDetail =
        """{"id":"EV-7K3M","name":"Friday Showmatch","kind":"show-matches","prizePoolUsd":500,"startsAt":"2026-10-09T18:00:00.000Z","endsAt":"2026-10-09T23:00:00.000Z","maxGames":10,"allocationId":"alloc-1","allocationName":"Weekly showmatches","status":"suspended","consumed":3,"held":0,"invalid":2,"suspensionMessage":"Paused while we review the results","adminNote":"Reported on Discord","createdBy":"Organizer#1234","createdVia":"launcher","createdAt":"2026-10-08T10:00:00.000Z","updatedBy":"Admin#1","updatedAt":"2026-10-09T19:00:00.000Z","suspendedBy":"Admin#1","suspendedAt":"2026-10-09T19:00:00.000Z","organizers":["Organizer#1234"],"delegates":[{"battleTag":"Delegate#2345","addedBy":"Organizer#1234","addedAt":"2026-10-08T11:00:00.000Z"}],"hosts":[{"battleTag":"Host#3456","addedBy":"Delegate#2345","addedAt":"2026-10-08T12:00:00.000Z"}]}""";

    public const string People =
        """{"organizers":["Organizer#1234"],"delegates":[{"battleTag":"Delegate#2345","addedBy":"Organizer#1234","addedAt":"2026-10-08T11:00:00.000Z"}],"hosts":[{"battleTag":"Host#3456","addedBy":"Delegate#2345","addedAt":"2026-10-08T12:00:00.000Z"}]}""";

    public const string GamesPage =
        """{"games":[{"matchId":"m-1","lobbyName":"Showmatch 1","startedAt":"2026-10-09T18:05:00.000Z","outcome":"valid","lengthSeconds":312,"namesHidden":false,"host":"Host#3456","teams":[{"playerCount":1,"players":[{"battleTag":"Grubby#1234","won":false}]},{"playerCount":1,"players":[{"battleTag":"Moon#5678","won":true}]}],"observerCount":1,"observers":["Caster#1111"],"computers":0,"viewerCount":42,"watchedSecondsTotal":12600,"watchedSecondsAvg":300}],"nextCursor":"c2"}""";

    public const string ActiveGames =
        """[{"matchId":"m-2","eventId":"EV-7K3M","eventName":"Friday Showmatch","host":"Host#3456","lobbyName":"Showmatch 2","startedAt":"2026-10-09T19:00:00.000Z","players":["Grubby#1234","Moon#5678"],"observers":[],"viewerCount":17}]""";

    public const string Audit =
        """[{"id":"aud-1","at":"2026-10-09T19:00:00.000Z","actor":"Admin#1","actorRole":"admin","action":"event-updated","eventId":"EV-7K3M","details":{"changes":{"endsAt":{"from":"2026-10-09T23:00:00.000Z","to":"2026-10-10T01:00:00.000Z"},"maxGames":{"from":10,"to":12}},"acknowledged":true,"tags":["a","b"]}}]""";

    public const string RoleHints =
        """[{"battleTag":"Grubby#1234","organizerOf":[{"allocationId":"alloc-1","allocationName":"Weekly showmatches"}],"delegateOf":[],"hostOf":[{"eventId":"EV-7K3M","eventName":"Friday Showmatch"}]},{"battleTag":"Moon#5678","organizerOf":[],"delegateOf":[],"hostOf":[]}]""";
}
