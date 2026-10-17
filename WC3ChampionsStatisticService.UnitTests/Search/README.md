# Search API tests

Covers the player-search endpoints: the consolidated core (`global-search`), its rank enrichment
(`ranks-for-players`), and the legacy ladder search that both replace.

Run them with:

```bash
dotnet test WC3ChampionsStatisticService.UnitTests --filter "FullyQualifiedName~UnitTests.Search"
```

104 tests, ~550 ms.

All four search modes are covered:

| Mode | Route | Status | Tests |
|---|---|---|---|
| Core directory search | `GET api/players/global-search` | Current + future | `GlobalSearchTests`, `GlobalSearchControllerTests` |
| Rank enrichment | `POST api/ladder/ranks-for-players` | New | `RanksForPlayersTests` |
| Ladder hybrid search | `GET api/ladder/search` | Outdated, still serving | `LegacyLadderSearchTests` |
| Directory dump | `GET api/players/?search=` | Outdated, still serving | `LegacyPlayerSearchTests` |

Both outdated modes remain live: `api/ladder/search` because the in-game UI bundle calls it and because
it is the ladder's rollback path, `api/players/?search=` because it is the picker's rollback path.

## These tests need no database

Every other repository-backed suite in this project inherits `IntegrationTestBase`, which points at the
shared W3C test Mongo (`IntegrationTestBase.cs:19`) and drops the database in `[SetUp]`. That makes those
suites unrunnable without network access to that host, and destructive when it is reachable.

This suite deliberately avoids that. The seam is the cache in front of the directory:

```
PersonalSettingsProvider.GetPersonalSettingsAsync()
    └── ICachedDataProvider.GetCachedOrRequestAsync(fetchFromMongo, key)
```

Mock the cache so it returns a value **without invoking the callback**, and the Mongo fetch is never
reached — the `MongoClient` handed to the provider stays inert, and `MongoClient`'s constructor opens no
connection. `SearchTestFixtures.PlayerServiceWith(directory)` packages this: hand it a list of
`PersonalSetting` and you have a `PlayerService` whose entire searchable directory is that list.

Everything else is an interface (`IRankRepository`, `IPlayerRepository`) and mocks normally.

The practical consequence: these tests run in CI, on a laptop, and offline, and a failure means the
search logic changed rather than that a shared host was unreachable.

## Layout

| File | Covers |
|---|---|
| `SearchTestFixtures.cs` | Shared builders. No tests. |
| `GlobalSearchTests.cs` | `PlayerService.GlobalSearchForPlayer` — matching, relevance, paging, response shape, seasons |
| `GlobalSearchControllerTests.cs` | `PlayersController.GlobalSearchPlayer` — the `pageSize` clamp, which only exists at the controller |
| `RanksForPlayersTests.cs` | `LadderController.GetRanksForPlayers` — validation, context scoping, mapping |
| `LegacyLadderSearchTests.cs` | `LadderController.SearchPlayer` — characterisation of the legacy ladder route |
| `LegacyPlayerSearchTests.cs` | `PlayersController.SearchPlayer` — characterisation of the legacy directory dump |
| `SubscriberContractTests.cs` | Promises made to identified shipped clients — see below |

## Subscriber contracts

`SubscriberContractTests.cs` answers a different question from the rest of the folder. The other files
ask "does the search behave correctly." That one asks "does it still behave the way a client that ships
today already depends on" — a logic test can be legitimately rewritten when the logic changes, and that
must not quietly take a subscriber guarantee with it.

The subscriber registry, the audit date, and the file's limits live in the header of
`SubscriberContractTests.cs` itself, next to the tests a maintainer would edit — one home, kept where
it is enforced.

## What each group pins, and why it matters

### Matching and relevance

Relevance is a numeric tier encoded as a prefix on `RelevanceId`: `1` exact name, `2` name starts with
the term, `9` substring anywhere. Results are ordered by that string, so **one value is both the sort key
and the pagination cursor**. Changing the tier numbers, the separator, or the ordering silently changes
paging for every consumer — hence `Relevance_TierIsEncodedInRelevanceId` asserts the literal format.

`Relevance_MatchIsOnNameOnly_NotTheNumericTag` pins a non-obvious rule: tiering splits on `#` and looks
only at the name, so searching `1234` finds `Grubby#1234` as a tier-9 substring hit, never as exact.

### Paging

Cursor-based, not offset-based: the caller echoes the last `RelevanceId` it saw and the server returns
strictly-greater ones.

`Paging_HonoursPageSizeExactly` guards a live external dependency. The W3Champions launcher
(`launcher-e`, `src/models/player-search.ts:63`) infers "there is another page" from
`newPlayers.length === PAGE_SIZE` with `PAGE_SIZE = 10`. A backend that silently substituted its own
default would end that launcher's infinite scroll at page one — no error, just missing results.

`Seasons_AreLookedUpOnlyForThePageReturned` pins the ordering that keeps the endpoint cheap: seasons are
fetched *after* paging, so a 3,000-row match costs one bounded lookup of `pageSize` keys. Moving that
lookup before the `Take` would not fail any other test.

### Response shape

The assertions in `Shape_EveryItemCarriesTheFieldsConsumersRead` are drawn from what consumers actually
dereference, not from the DTO definition:

| Field | Consumer requirement |
|---|---|
| `battleTag` | Identity every consumer emits |
| `relevanceId` | Pagination cursor — a missing value breaks the next page |
| `profilePicture` | launcher-e dereferences `.race`/`.pictureId`/`.isClassic` with no null guard |
| `seasons` | launcher-e maps over it with no null guard |
| `name` | Declared, never read by any known consumer |

### `ranks-for-players`

Two things are worth knowing about this endpoint's design, and both are pinned.

**Absence means unranked.** The response contains only players who *are* ranked in the requested
context; callers synthesise their own unranked rows from the search result. Returning zeroed rows here is
what produced the old duplicate-result bug in ladder search, so
`Mapping_UnrankedPlayersAreAbsent_RatherThanZeroed` locks the current behaviour in.

**The four-argument overload is the one that must be called.** `IRankRepository` has both
`LoadRanksForPlayers(tags, season)` (pre-existing, season-only) and
`LoadRanksForPlayers(tags, season, gateWay, gameMode)` (added for this endpoint). C# overload resolution
picks silently, and calling the two-argument version would return ranks from the wrong gateway and game
mode while still looking correct. `Context_TheSeasonOnlyOverloadIsNotUsed` asserts the old overload is
never invoked.

`Validation_OverTheBatchLimitIsRejected` guards the batch bound. The route is anonymous, so
`MaxBattleTags` is what keeps the `$in` query small.

### Legacy ladder search

`LegacyLadderSearchTests` is a **characterisation suite**, not a specification. The consolidation does
not change `api/ladder/search`; it stays live as the website's rollback target and because the in-game UI
bundle (`storage.w3champions.com/{env}/integration/w3champions.js`, shipped by the deprecated Electron
launcher) still calls it.

The tests pin the hybrid response: one flat array holding a ranked block scoped to
`{season, gateway, gameMode}` followed by a globally-matched tail of fully-zeroed rows, which consumers
fork on via `player.games > 0`. `Hybrid_TailIsNotScopedToTheRequestedContext` records the reason an
exact-tag search can return the same player twice.

This route and `global-search` both enforce a minimum length server-side (3 characters);
`players?search=` leaves that to the client.

The value of pinning behaviour nobody intends to change: when this route is eventually retired, the
failing tests are the checklist of what retiring it actually breaks.

## Extending

Add to `SearchTestFixtures` rather than building `PersonalSetting` or `Rank` inline — the constructors
carry defaults (`ProfilePicture.Default()` randomises `PictureId`) that make ad-hoc fixtures flaky.

When adding a test for a consumer-visible guarantee, name the consumer in a comment. Several assertions
here look arbitrary until you know which client would break, and that context is the difference between a
future maintainer fixing the code and "fixing" the test.
