# Cancelled Matches Admin Page Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give moderators an admin page listing cancelled (non-ranked) matches per game mode, with per-row replay download and chat log access, and enough identity information to attribute misbehaviour in anonymised FFA modes.

**Architecture:** The statistic service does not store cancelled matches and the `MatchCanceledEvent` stream is incomplete (it misses every game-creation failure). Matchmaking's own `Match` collection records all of them, so `website-backend` proxies a new matchmaking admin endpoint rather than building a read model. The frontend adds a dedicated admin grid that reuses the existing match-history leaf components.

**Tech Stack:** TypeScript + Express + MongoDB + mocha/chai/sinon/supertest (matchmaking-service); .NET 8 + ASP.NET Core + Newtonsoft.Json + NUnit/Moq (website-backend); Vue 3 + TypeScript + Vuetify + Pinia + vitest (website).

**Design spec:** `docs/superpowers/specs/2026-08-05-cancelled-matches-admin-page-design.md`

## Global Constraints

- **Three repositories, in dependency order.** Phase 1 `/home/francis/repos/matchmaking-service`, Phase 2 `/home/francis/repos/w3c-backend`, Phase 3 `/home/francis/repos/w3c-website`. Each phase is independently deployable and must be merged and deployed before the next phase returns data.
- **Create a branch in each repo before editing.** Never commit to `master`/`main`. The backend work already has a branch, `worktree-cancelled-matches-admin-spec`, containing the spec and the `GM_FOOTMEN_FRENZY` fix.
- **`dotnet` was not available in the environment where this plan was written.** Every C# task's verification step must actually be run by the implementer; do not assume the code compiles.
- **Permission gate is `EPermission.Moderation`** on every new backend endpoint. `BearerHasPermissionFilter` is `AttributeTargets.Method`, so it must be repeated on every action — it cannot be applied at controller level.
- **Never call the replay service from a list endpoint or during list rendering.** Replay-service calls cost money. Chat logs and replays are fetched only on an explicit per-match user action. Replay availability is inferred from `floGameId != null`, never by probing.
- **Disclosure:** battletags are public information and are not PII — they may be logged and traced freely, and no tracing exclusions are warranted on their account. What this page does that is sensitive is *linking* flo's anonymised `Player N` back to an account in modes that deliberately hide names in-game. That linkage is what the Moderation gate protects, and the gate must be enforced server-side on every action; a client-side flag is not sufficient.
- **Never enable read model handlers locally** (`CLAUDE.md`) and never point local runs at the production database.
- **`GameMode.Undefined` (0) means "all game modes"** everywhere in this feature.
- **`slotIndex` is 0-based** (matches the `CreateGameSlot[]` index). Flo renders the mask as `Player {index + 1}`, so all user-facing display must add 1.
- **Spelling is deliberately inconsistent between layers.** Server-side identifiers use the single-l `Canceled` spelling to match the existing `EMatchState.CANCELED` and `MatchCanceledEvent` — this covers the matchmaking route (`/admin/canceled-matches`), the backend route (`api/admin/matches/canceled`) and all C# type names. Frontend identifiers and all user-visible copy use `Cancelled`. Do not "fix" either side to match the other; follow whichever convention the file you are editing already uses.

---

## File Structure

**Phase 1 — matchmaking-service**

| File | Responsibility |
| --- | --- |
| `src/app/flows/game-creation/helpers/player-slots.helper.ts` | Modify: persist the assigned slot index onto the match player |
| `src/app/data/repos/matches.repo.ts` | Modify: paged cancelled-match query + supporting index |
| `src/app/apis/admin.api.ts` | Modify: new `GET /admin/canceled-matches` route |
| `src/tests/app/player-slots.helper.test.ts` | Create: slot index assignment test |
| `src/tests/app/admin-canceled-matches.api.test.ts` | Create: route test |

**Phase 2 — website-backend**

| File | Responsibility |
| --- | --- |
| `W3C.Domain/MatchmakingService/MatchEventDtos.cs` | Modify: JSON mapping for `_id`, entity timestamps, `slotIndex` |
| `W3C.Domain/MatchmakingService/MatchmakingServiceClient.cs` | Modify: proxy method + request/response DTOs |
| `W3ChampionsStatisticService/Admin/AdminMatchesController.cs` | Create: Moderation-gated `api/admin/matches/canceled` |
| `W3C.Domain/ReplayService/ReplayServiceClient.cs` | Modify: map unavailable replay/chat to a 404-able outcome |
| `W3ChampionsStatisticService/Replays/ReplaysController.cs` | Modify: return 404 when the replay service has nothing |
| `W3ChampionsStatisticService/WebApi/ActionFilters/ReplayRateLimitAttribute.cs` | Modify: moderator hourly limit |
| `WC3ChampionsStatisticService.UnitTests/GameModes/GameModesHelperTests.cs` | Create: FFA mode membership |
| `WC3ChampionsStatisticService.UnitTests/Admin/AdminMatchesControllerTests.cs` | Create: proxy + permission tests |

**Phase 3 — website**

| File | Responsibility |
| --- | --- |
| `src/types/admin/CancelledMatch.ts` | Create: DTOs |
| `src/services/admin/CancelledMatchService.ts` | Create: list + chat-log-by-flo-id calls |
| `src/services/admin/CancelledMatchService.test.ts` | Create: service tests |
| `src/components/matches/PlayerMatchInfo.vue` | Modify: `noWinner` prop |
| `src/components/matches/TeamMatchInfo.vue` | Modify: pass `noWinner` through |
| `src/components/matches/DownloadReplayIcon.vue` | Modify: optional `floGameId` prop |
| `src/store/admin/replayManagement/store.ts` | Modify: `loadChatLogByFloId` |
| `src/components/admin/replays/AdminReplayChatLogMessages.vue` | Modify: optional `floGameId` prop |
| `src/components/admin/AdminCancelledMatches.vue` | Create: the page |
| `src/router/types.ts`, `src/router/index.ts`, `src/components/admin/AdminNavigation.vue` | Modify: route + nav entry |

---

# Phase 1 — matchmaking-service

Work in `/home/francis/repos/matchmaking-service`. Run `git checkout -b feat/cancelled-matches-admin` before Task 1.

Commands: `npm run build` (tsc), `npm run lint`, `npm test` (mocha).

---

### Task 1: Persist the FLO slot index on match players

`IMatchPlayer.slotIndex` already exists and is documented as the canonical 0-based FLO slot index, but only custom games populate it. `setPlayersToSlots` computes the assignment for matchmaking games and discards it. Persisting it lets the admin page show `Player N` next to a battletag with no replay-service call.

**Files:**
- Modify: `src/app/flows/game-creation/helpers/player-slots.helper.ts:14-29`
- Test: `src/tests/app/player-slots.helper.test.ts`

**Interfaces:**
- Consumes: nothing.
- Produces: `IMatchPlayer.slotIndex` populated (0-based) on every non-computer player of a match that reached flo game creation. Phase 2 reads this as `slotIndex` on the player JSON.

- [ ] **Step 1: Write the failing test**

Create `src/tests/app/player-slots.helper.test.ts`:

```ts
import { expect } from 'chai';
import { generateEmptySlots, setPlayersToSlots } from 'app/flows/game-creation/helpers/player-slots.helper';
import { GameCreationPlayer } from 'app/flows/game-creation/game-creation.models';
import { MapEntity } from 'app/data/models/mapEntity';

function playerStub(team: number, floPlayerId: number): GameCreationPlayer {
    return {
        matchPlayer: { team, battleTag: `player-${floPlayerId}` },
        playerInstance: { floPlayer: { id: floPlayerId } },
    } as unknown as GameCreationPlayer;
}

function mapStub(): MapEntity {
    return { gameMap: { twelve_p: false }, mappedForces: undefined } as unknown as MapEntity;
}

describe('setPlayersToSlots', function() {
    it('records the assigned slot index on each match player', function() {
        const slots = generateEmptySlots(false);
        const players = [playerStub(0, 101), playerStub(1, 102)];

        setPlayersToSlots(players, slots, mapStub());

        for (const player of players) {
            const slotIndex = player.matchPlayer.slotIndex;
            expect(slotIndex).to.be.a('number');
            expect(slots[slotIndex!].player_id).to.equal(player.playerInstance.floPlayer.id);
        }
    });

    it('assigns a distinct slot index per player', function() {
        const slots = generateEmptySlots(false);
        const players = [playerStub(0, 101), playerStub(1, 102)];

        setPlayersToSlots(players, slots, mapStub());

        const indexes = players.map(p => p.matchPlayer.slotIndex);
        expect(new Set(indexes).size).to.equal(players.length);
    });
});
```

If `generateEmptySlots` or `getNextAvailableSlot` requires more of `MapEntity` than the stub provides (for example map forces), extend `mapStub()` with the minimum fields those functions actually read — read `player-slots.helper.ts` and match it. Do not change production code to accommodate the stub.

- [ ] **Step 2: Run the test to verify it fails**

Run: `npx mocha src/tests/app/player-slots.helper.test.ts --require src/tests/load-environment-variables.ts`
Expected: FAIL — `expected undefined to be a number`.

- [ ] **Step 3: Write the implementation**

In `src/app/flows/game-creation/helpers/player-slots.helper.ts`, inside `setPlayersToSlots`, after the slot is resolved:

```ts
export const setPlayersToSlots = (allPlayers: GameCreationPlayer[], slots: CreateGameSlot[], map: MapEntity) => {
    allPlayers.forEach(x => {
        const slot = getNextAvailableSlot(slots, x.matchPlayer.team, map);
        const isObs = x.matchPlayer.team == OBS_TEAM;

        slot.player_id = x.playerInstance.floPlayer.id;
        // Persist the lobby slot so downstream consumers can map flo's masked
        // "Player N" names (N = slotIndex + 1) back to a battleTag without
        // re-reading the replay. Custom games already set this earlier.
        x.matchPlayer.slotIndex = slots.indexOf(slot);
        slot.settings = {
            color: null,
            team: x.matchPlayer.team,
            computer: null,
            handicap: 100,
            status: SlotStatus.Occupied,
            race: isObs ? 0 : convertToFloRace(x.matchPlayer.race),
        };
    });
};
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `npx mocha src/tests/app/player-slots.helper.test.ts --require src/tests/load-environment-variables.ts`
Expected: PASS (2 passing)

- [ ] **Step 5: Verify nothing else regressed**

Run: `npm run build && npm run lint && npm test`
Expected: build succeeds, lint clean, full suite passes.

- [ ] **Step 6: Commit**

```bash
git add src/app/flows/game-creation/helpers/player-slots.helper.ts src/tests/app/player-slots.helper.test.ts
git commit -m "Record the assigned FLO slot index on matchmaking match players"
```

---

### Task 2: Paged cancelled-match query on MatchesRepo

**Files:**
- Modify: `src/app/data/repos/matches.repo.ts` (imports, `initialize`, new method)
- Test: `src/tests/app/matches.repo.canceled.test.ts`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `export interface CanceledMatchesQuery { gameMode?: EGameMode; battleTag?: string; page: number; itemsPerPage: number; }`
  - `export interface CanceledMatchesPage { total: number; matches: Match[]; }`
  - `MatchesRepo.getCanceledMatches(query: CanceledMatchesQuery): Promise<CanceledMatchesPage>`

  Task 3 calls `dbContext.matchesRepo.getCanceledMatches(...)`.

- [ ] **Step 1: Write the failing test**

Create `src/tests/app/matches.repo.canceled.test.ts`:

```ts
import { expect } from 'chai';
import sinon from 'sinon';
import { MatchesRepo } from 'app/data/repos/matches.repo';
import { EMatchState } from 'app/data/models/match';
import { EGameMode } from 'shared/data/enums';

function repoWith(findResult: any[], countResult: number) {
    const repo = Object.create(MatchesRepo.prototype) as MatchesRepo;
    const find = sinon.stub().resolves(findResult);
    const count = sinon.stub().resolves(countResult);
    (repo as any).find = find;
    (repo as any).count = count;
    return { repo, find, count };
}

describe('MatchesRepo.getCanceledMatches', function() {
    it('filters to cancelled matches and returns the total', async function() {
        const { repo, find, count } = repoWith([{ _id: 'abc' }], 7);

        const result = await repo.getCanceledMatches({ page: 1, itemsPerPage: 25 });

        expect(result.total).to.equal(7);
        expect(result.matches).to.have.length(1);
        expect(find.firstCall.args[0]).to.deep.equal({ state: EMatchState.CANCELED });
        expect(count.firstCall.args[0]).to.deep.equal({ state: EMatchState.CANCELED });
    });

    it('applies the game mode and battle tag filters', async function() {
        const { repo, find } = repoWith([], 0);

        await repo.getCanceledMatches({
            page: 1,
            itemsPerPage: 25,
            gameMode: EGameMode.GM_1ON1,
            battleTag: 'Player#1234',
        });

        expect(find.firstCall.args[0]).to.deep.equal({
            state: EMatchState.CANCELED,
            gameMode: EGameMode.GM_1ON1,
            'players.battleTag': 'Player#1234',
        });
    });

    it('sorts newest first and computes the offset from the page', async function() {
        const { repo, find } = repoWith([], 0);

        await repo.getCanceledMatches({ page: 3, itemsPerPage: 10 });

        expect(find.firstCall.args[1]).to.equal(10);
        expect(find.firstCall.args[2]).to.deep.equal({ '_updated_at': -1 });
        expect(find.firstCall.args[3]).to.equal(20);
    });

    it('falls back to safe paging values and caps the page size', async function() {
        const { repo, find } = repoWith([], 0);

        await repo.getCanceledMatches({ page: 0, itemsPerPage: 5000 });

        expect(find.firstCall.args[1]).to.equal(100);
        expect(find.firstCall.args[3]).to.equal(0);
    });
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `npx mocha src/tests/app/matches.repo.canceled.test.ts --require src/tests/load-environment-variables.ts`
Expected: FAIL — `repo.getCanceledMatches is not a function`.

- [ ] **Step 3: Write the implementation**

In `src/app/data/repos/matches.repo.ts`, add the mongodb types to the imports:

```ts
import { Filter, Sort } from 'mongodb';
```

Add the exported types above the `MatchesRepo` class (mirroring `PlayerWarningsQuery`/`PlayerWarningsPage` in `player-warnings.repo.ts:9-19`):

```ts
export const MaxCanceledMatchesPageSize = 100;

export interface CanceledMatchesQuery {
    gameMode?: EGameMode;
    battleTag?: string;
    page: number;
    itemsPerPage: number;
}

export interface CanceledMatchesPage {
    total: number;
    matches: Match[];
}
```

Add the method to `MatchesRepo`:

```ts
/**
 * Cancelled matches are never in the in-memory `matches` cache - `initialize`
 * only loads non-cancelled matches and `cancelMatch` removes them - so this
 * always queries Mongo.
 *
 * Sorted by `_updated_at`, which `EntityRepo.save` stamps immediately before the
 * write. For a document still in the CANCELED state that is the moment it was
 * cancelled: nothing rewrites it afterwards except a heal, which moves it to
 * FINISHED and out of this result set.
 */
public async getCanceledMatches(query: CanceledMatchesQuery): Promise<CanceledMatchesPage> {
    const filter: Filter<Match> = { state: EMatchState.CANCELED };

    if (query.gameMode !== undefined) {
        filter.gameMode = query.gameMode;
    }

    if (query.battleTag) {
        filter['players.battleTag'] = query.battleTag;
    }

    const page = Number.isFinite(query.page) && query.page > 0 ? query.page : 1;
    const requestedPageSize = Number.isFinite(query.itemsPerPage) && query.itemsPerPage > 0 ? query.itemsPerPage : 25;
    const itemsPerPage = Math.min(requestedPageSize, MaxCanceledMatchesPageSize);
    const sort: Sort = { '_updated_at': -1 };
    const offset = (page - 1) * itemsPerPage;

    const [matches, total] = await Promise.all([
        this.find(filter, itemsPerPage, sort, offset),
        this.count(filter),
    ]);

    return { total, matches };
}
```

Add the supporting index in `initialize()`, alongside the existing two:

```ts
public async initialize(): Promise<void> {
    await this.createIndex({ '_created_at': -1 });
    await this.createIndex({ 'players.battleTag': 1 });
    await this.createIndex({ 'state': 1, 'gameMode': 1, '_updated_at': -1 });
    // ... unchanged below
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `npx mocha src/tests/app/matches.repo.canceled.test.ts --require src/tests/load-environment-variables.ts`
Expected: PASS (4 passing)

- [ ] **Step 5: Verify the whole suite**

Run: `npm run build && npm run lint && npm test`
Expected: all green.

- [ ] **Step 6: Commit**

```bash
git add src/app/data/repos/matches.repo.ts src/tests/app/matches.repo.canceled.test.ts
git commit -m "Add a paged cancelled-match query and its index"
```

---

### Task 3: `GET /admin/canceled-matches` route

**Files:**
- Modify: `src/app/apis/admin.api.ts` (new route beside `/warnings` at `:55-73`)
- Test: `src/tests/app/admin-canceled-matches.api.test.ts`

**Interfaces:**
- Consumes: `dbContext.matchesRepo.getCanceledMatches(query)` from Task 2.
- Produces: `GET /admin/canceled-matches?page=&itemsPerPage=&gameMode=&battleTag=` returning `{ total: number, matches: Match[] }`, guarded by `requireAdmin` (header `x-admin-secret`). Phase 2 Task 5 calls this.

- [ ] **Step 1: Write the failing test**

Create `src/tests/app/admin-canceled-matches.api.test.ts`:

```ts
import { expect } from 'chai';
import express from 'express';
import request from 'supertest';
import sinon from 'sinon';
import { AdminAPI } from 'app/apis/admin.api';
import { dbContext } from 'app/data/dbContext';
import { EGameMode } from 'shared/data/enums';

describe('AdminAPI cancelled matches', function() {
    const previousAdminSecret = process.env.ADMIN_API_SECRET;
    let app: express.Express;
    let sandbox: sinon.SinonSandbox;
    let originalMatchesRepo: any;

    before(function() {
        process.env.ADMIN_API_SECRET = 'test-secret';
    });

    after(function() {
        process.env.ADMIN_API_SECRET = previousAdminSecret;
    });

    beforeEach(function() {
        sandbox = sinon.createSandbox();
        originalMatchesRepo = (dbContext as any).matchesRepo;
        app = express();
        app.use(express.json());
        app.use('/admin', new AdminAPI(app).getHandler());
    });

    afterEach(function() {
        (dbContext as any).matchesRepo = originalMatchesRepo;
        sandbox.restore();
    });

    it('rejects requests without the admin secret', async function() {
        const res = await request(app).get('/admin/canceled-matches');

        expect(res.status).to.equal(403);
    });

    it('lists cancelled matches through the repo', async function() {
        const getCanceledMatches = sandbox.stub().resolves({ total: 1, matches: [{ _id: 'match-1' }] });
        (dbContext as any).matchesRepo = { getCanceledMatches };

        const res = await request(app)
            .get('/admin/canceled-matches?page=2&itemsPerPage=10&gameMode=1&battleTag=Player%231234')
            .set('x-admin-secret', 'test-secret');

        expect(res.status).to.equal(200);
        expect(res.body.total).to.equal(1);
        expect(res.body.matches[0]._id).to.equal('match-1');
        expect(getCanceledMatches.calledOnceWith({
            page: 2,
            itemsPerPage: 10,
            gameMode: EGameMode.GM_1ON1,
            battleTag: 'Player#1234',
        })).to.equal(true);
    });

    it('treats game mode 0 as all game modes', async function() {
        const getCanceledMatches = sandbox.stub().resolves({ total: 0, matches: [] });
        (dbContext as any).matchesRepo = { getCanceledMatches };

        await request(app)
            .get('/admin/canceled-matches?gameMode=0')
            .set('x-admin-secret', 'test-secret');

        expect(getCanceledMatches.firstCall.args[0].gameMode).to.equal(undefined);
    });

    it('rejects a non-numeric game mode', async function() {
        (dbContext as any).matchesRepo = { getCanceledMatches: sandbox.stub() };

        const res = await request(app)
            .get('/admin/canceled-matches?gameMode=nope')
            .set('x-admin-secret', 'test-secret');

        expect(res.status).to.equal(400);
    });
});
```

`EGameMode.GM_1ON1` must equal `1`. Open `src/shared/data/enums.ts` and confirm; if the numeric value differs, use the correct member and matching query string rather than changing the assertion to a bare number.

- [ ] **Step 2: Run the test to verify it fails**

Run: `npx mocha src/tests/app/admin-canceled-matches.api.test.ts --require src/tests/load-environment-variables.ts`
Expected: FAIL — the route 404s, so the first passing assertion is wrong (403 vs 404).

- [ ] **Step 3: Write the implementation**

In `src/app/apis/admin.api.ts`, add this route immediately after the `/warnings` route block. `EGameMode` and `dbContext` are already imported in this file; confirm and add the import if not.

```ts
this.getRouter().get('/canceled-matches', requireAdmin, async (req: Request, res: Response, _next: NextFunction) => {
    const page = Number(req.query.page || 1);
    const itemsPerPage = Number(req.query.itemsPerPage || 25);
    const battleTag = req.query.battleTag as string | undefined;

    // Game mode 0 (Undefined) means "all modes", so it is simply left off the filter.
    let gameMode: EGameMode | undefined;
    const rawGameMode = req.query.gameMode;
    if (rawGameMode !== undefined && rawGameMode !== '') {
        const parsedGameMode = Number(rawGameMode);
        if (!Number.isFinite(parsedGameMode)) {
            return res.status(400).json({ errors: [{ msg: `Invalid game mode: ${rawGameMode}` }] });
        }
        if (parsedGameMode !== 0) {
            gameMode = parsedGameMode as EGameMode;
        }
    }

    const result = await dbContext.matchesRepo.getCanceledMatches({
        page,
        itemsPerPage,
        gameMode,
        battleTag,
    });

    return res.json(result);
});
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `npx mocha src/tests/app/admin-canceled-matches.api.test.ts --require src/tests/load-environment-variables.ts`
Expected: PASS (4 passing)

- [ ] **Step 5: Verify the whole suite**

Run: `npm run build && npm run lint && npm test`
Expected: all green.

- [ ] **Step 6: Commit and open the Phase 1 PR**

```bash
git add src/app/apis/admin.api.ts src/tests/app/admin-canceled-matches.api.test.ts
git commit -m "Expose cancelled matches on the admin API"
git push -u origin feat/cancelled-matches-admin
```

Open a PR. In the description, note that `matches.manager.ts:168` still omits the `pushMatchCanceled`/`pushMatchStart` calls that `:319` makes, so game-creation failures remain invisible to the event stream — this endpoint sidesteps that, but the inconsistency is worth fixing separately.

---

# Phase 2 — website-backend

Work in `/home/francis/repos/w3c-backend`, on branch `worktree-cancelled-matches-admin-spec` (already contains the spec and the `GM_FOOTMEN_FRENZY` fix).

Commands: `dotnet build`, `dotnet test`, `dotnet format --verify-no-changes`.

---

### Task 4: DTO mapping for the proxied payload

The existing `Match` / `PlayerMMrChange` / `UnfinishedMatchPlayer` DTOs already model a matchmaking match document, so they are reused rather than duplicated. But they are annotated for **BSON only**, and `MatchmakingServiceClient` deserialises with **Newtonsoft**, which ignores `[BsonElement]`. Without a `[JsonProperty("_id")]` the match id silently deserialises as `null`.

Verified safe: the event `Match` DTO is not returned by any controller (`MatchesController`'s `Ok(match)` calls return `MatchupDetail`, which holds a `Matchup`), so adding JSON attributes changes no existing response.

**Files:**
- Modify: `W3C.Domain/MatchmakingService/MatchEventDtos.cs` (`Match` at `:100-151`, `UnfinishedMatchPlayer` at `:44`)
- Test: `WC3ChampionsStatisticService.UnitTests/GameModes/GameModesHelperTests.cs` (create)

**Interfaces:**
- Consumes: nothing.
- Produces: `Match.id` populated from JSON `_id`; `Match.createdAt` (`DateTimeOffset?`) from `_created_at`; `Match.canceledAt` (`DateTimeOffset?`) from `_updated_at`; `UnfinishedMatchPlayer.slotIndex` (`int?`). Task 5 and Task 6 rely on these names.

- [ ] **Step 1: Write the failing tests**

Create `WC3ChampionsStatisticService.UnitTests/GameModes/GameModesHelperTests.cs`:

```csharp
using Newtonsoft.Json;
using NUnit.Framework;
using W3C.Contracts.Matchmaking;
using W3C.Domain.GameModes;
using W3C.Domain.MatchmakingService;

namespace WC3ChampionsStatisticService.Tests.GameModes;

[TestFixture]
public class GameModesHelperTests
{
    [Test]
    public void FootmenFrenzyIsAnFfaGameMode()
    {
        Assert.That(GameModesHelper.IsFfaGameMode(GameMode.GM_FOOTMEN_FRENZY), Is.True);
    }

    [Test]
    public void TheEstablishedFfaGameModesAreStillFfa()
    {
        Assert.That(GameModesHelper.IsFfaGameMode(GameMode.FFA), Is.True);
        Assert.That(GameModesHelper.IsFfaGameMode(GameMode.GM_SC_FFA_4), Is.True);
        Assert.That(GameModesHelper.IsFfaGameMode(GameMode.GM_SC_OZ), Is.True);
    }

    [Test]
    public void LtwFfaIsNotAnonymisedAndStaysOutOfTheFfaList()
    {
        Assert.That(GameModesHelper.IsFfaGameMode(GameMode.GM_LTW_FFA), Is.False);
        Assert.That(GameModesHelper.IsFfaGameMode(GameMode.GM_1v1), Is.False);
    }
}

[TestFixture]
public class MatchJsonMappingTests
{
    private const string CanceledMatchJson = """
    {
      "_id": "abc1234567",
      "_created_at": "2026-08-01T10:00:00.000Z",
      "_updated_at": "2026-08-01T16:05:00.000Z",
      "state": 3,
      "gameMode": 5,
      "gamename": "w3c-test",
      "startTime": 1754042400000,
      "floGameId": 4242,
      "players": [
        { "battleTag": "Tester#1234", "team": 0, "race": 1, "slotIndex": 2 }
      ]
    }
    """;

    [Test]
    public void MatchIdIsReadFromTheUnderscoreIdJsonProperty()
    {
        var match = JsonConvert.DeserializeObject<Match>(CanceledMatchJson);

        Assert.That(match.id, Is.EqualTo("abc1234567"));
    }

    [Test]
    public void EntityTimestampsAreMapped()
    {
        var match = JsonConvert.DeserializeObject<Match>(CanceledMatchJson);

        Assert.That(match.createdAt, Is.Not.Null);
        Assert.That(match.canceledAt, Is.Not.Null);
        Assert.That(match.canceledAt!.Value, Is.GreaterThan(match.createdAt!.Value));
    }

    [Test]
    public void SlotIndexIsMappedOnPlayers()
    {
        var match = JsonConvert.DeserializeObject<Match>(CanceledMatchJson);

        Assert.That(match.players[0].slotIndex, Is.EqualTo(2));
    }

    [Test]
    public void AMissingEndTimeStaysZeroRatherThanThrowing()
    {
        var match = JsonConvert.DeserializeObject<Match>(CanceledMatchJson);

        Assert.That(match.endTime, Is.EqualTo(0));
    }

    [Test]
    public void AMissingFloGameIdDeserialisesToNull()
    {
        var match = JsonConvert.DeserializeObject<Match>("""{"_id":"x","state":3}""");

        Assert.That(match.floGameId, Is.Null);
        Assert.That(match.HasFloGameId(), Is.False);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~GameModes"`
Expected: `MatchJsonMappingTests` fail (`match.id` is null; `createdAt`/`canceledAt`/`slotIndex` do not compile). `GameModesHelperTests` should already pass — the `GM_FOOTMEN_FRENZY` fix is committed on this branch. If they fail, that fix is missing; restore it before continuing.

- [ ] **Step 3: Write the implementation**

In `W3C.Domain/MatchmakingService/MatchEventDtos.cs`, add `using Newtonsoft.Json;` if absent.

On `Match`, annotate `id` and add the two entity timestamps:

```csharp
    // BsonElement covers reads from the raw event collections; JsonProperty covers
    // the matchmaking admin API responses, which MatchmakingServiceClient
    // deserialises with Newtonsoft. Newtonsoft ignores BsonElement, so without
    // this the id silently comes back null.
    [BsonElement("_id")]
    [JsonProperty("_id")]
    public string id { get; set; }
    public int? floGameId { get; set; }

    [BsonElement("_created_at")]
    [JsonProperty("_created_at")]
    public DateTimeOffset? createdAt { get; set; }

    // EntityRepo.save stamps _updated_at immediately before the write, and
    // cancelMatch goes through save. For a match still in the CANCELED state this
    // is when it was cancelled; a heal would move it to FINISHED.
    [BsonElement("_updated_at")]
    [JsonProperty("_updated_at")]
    public DateTimeOffset? canceledAt { get; set; }
```

Add `using System;` to the file if `DateTimeOffset` is not already resolvable.

On `UnfinishedMatchPlayer`, add the slot index:

```csharp
    public FloPing[] floPings { get; set; }

    // 0-based FLO lobby slot. Flo masks anonymised names as "Player {slotIndex + 1}",
    // so display must add one. Null for matches that never reached game creation.
    public int? slotIndex { get; set; }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~GameModes"`
Expected: PASS (8 tests)

- [ ] **Step 5: Verify no regressions in the existing event handling**

Run: `dotnet build && dotnet test`
Expected: full suite passes. The read-model handler tests exercise BSON deserialisation of these same DTOs, so a green suite confirms the added attributes did not disturb it.

- [ ] **Step 6: Commit**

```bash
git add W3C.Domain/MatchmakingService/MatchEventDtos.cs WC3ChampionsStatisticService.UnitTests/GameModes/GameModesHelperTests.cs
git commit -m "Map matchmaking JSON onto the shared match DTOs"
```

---

### Task 5: `MatchmakingServiceClient.GetCanceledMatches`

**Files:**
- Modify: `W3C.Domain/MatchmakingService/MatchmakingServiceClient.cs` (method beside `GetPlayerWarnings` at `:90-113`; DTOs beside `PlayerWarningsGetRequest` at `:758-770`)
- Test: covered by Task 6's controller test (the client has no standalone test fixture today)

**Interfaces:**
- Consumes: Phase 1 Task 3's route; `Match` from Task 4.
- Produces:
  - `CanceledMatchesGetRequest { int Page = 1; int ItemsPerPage = 25; GameMode GameMode; string BattleTag; }`
  - `CanceledMatchesResponse { int total; List<Match> matches; }`
  - `Task<CanceledMatchesResponse> GetCanceledMatches(CanceledMatchesGetRequest req)`

  Task 6 calls `GetCanceledMatches`.

- [ ] **Step 1: Add the request/response DTOs**

In `W3C.Domain/MatchmakingService/MatchmakingServiceClient.cs`, beside the other admin DTOs:

```csharp
public class CanceledMatchesGetRequest
{
    public int Page { get; set; } = 1;
    public int ItemsPerPage { get; set; } = 25;

    // GameMode.Undefined (0) means "all game modes" and is not sent upstream.
    public GameMode GameMode { get; set; }
    public string BattleTag { get; set; }
}

public class CanceledMatchesResponse
{
    public int total { get; set; }
    public List<Match> matches { get; set; }
}
```

Ensure `using W3C.Contracts.Matchmaking;` is present for `GameMode`.

- [ ] **Step 2: Add the proxy method**

Mirror `GetPlayerWarnings` exactly, including the `x-admin-secret` header:

```csharp
public async Task<CanceledMatchesResponse> GetCanceledMatches(CanceledMatchesGetRequest req)
{
    var url = $"{MatchmakingApiUrl}/admin/canceled-matches?page={req.Page}&itemsPerPage={req.ItemsPerPage}";

    if (req.GameMode != GameMode.Undefined)
    {
        url += $"&gameMode={(int)req.GameMode}";
    }

    if (!string.IsNullOrEmpty(req.BattleTag))
    {
        url += $"&battleTag={HttpUtility.UrlEncode(req.BattleTag)}";
    }

    var request = new HttpRequestMessage(HttpMethod.Get, url);
    request.Headers.Add("x-admin-secret", AdminSecret);
    var response = await _httpClient.SendAsync(request);

    if (response.IsSuccessStatusCode)
    {
        return await GetResult<CanceledMatchesResponse>(response);
    }

    return null;
}
```

- [ ] **Step 3: Verify it compiles**

Run: `dotnet build`
Expected: build succeeds.

- [ ] **Step 4: Commit**

```bash
git add W3C.Domain/MatchmakingService/MatchmakingServiceClient.cs
git commit -m "Proxy the matchmaking cancelled-matches endpoint"
```

---

### Task 6: `AdminMatchesController`

**Files:**
- Create: `W3ChampionsStatisticService/Admin/AdminMatchesController.cs`
- Test: `WC3ChampionsStatisticService.UnitTests/Admin/AdminMatchesControllerTests.cs`

**Interfaces:**
- Consumes: `MatchmakingServiceClient.GetCanceledMatches` from Task 5.
- Produces: `GET api/admin/matches/canceled?gameMode=&playerBattleTag=&page=&itemsPerPage=` returning `{ total, matches }`. Phase 3 calls this.

> **The search parameter MUST NOT be named `battleTag`.** `BearerHasPermissionFilter.cs:33`
> does `context.ActionArguments["battleTag"] = res.BattleTag;` unconditionally, so on any
> action it guards, a parameter with that exact name is silently overwritten with the acting
> moderator's own battletag. A `battleTag` search filter would quietly return only the
> moderator's own matches. Unit tests calling the controller method directly cannot see this,
> because they bypass filters.

- [ ] **Step 1: Write the failing test**

Create `WC3ChampionsStatisticService.UnitTests/Admin/AdminMatchesControllerTests.cs`:

```csharp
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using NUnit.Framework;
using W3C.Contracts.Admin.Permission;
using W3C.Contracts.Matchmaking;
using W3C.Domain.MatchmakingService;
using W3ChampionsStatisticService.Admin;
using W3ChampionsStatisticService.WebApi.ActionFilters;

namespace WC3ChampionsStatisticService.Tests.Admin;

[TestFixture]
public class AdminMatchesControllerTests
{
    private const string CanceledMatchesJson = """
    {"total":3,"matches":[{"_id":"abc1234567","state":3,"gameMode":5,"floGameId":4242,
    "players":[{"battleTag":"Tester#1234","team":0,"slotIndex":2}]}]}
    """;

    [Test]
    public void CanceledMatchesRequiresModerationPermission()
    {
        var method = typeof(AdminMatchesController).GetMethod(nameof(AdminMatchesController.GetCanceledMatches))!;
        var attribute = method.GetCustomAttribute<BearerHasPermissionFilter>();

        Assert.That(attribute, Is.Not.Null);
        Assert.That(attribute!.Permission, Is.EqualTo(EPermission.Moderation));
    }

    [Test]
    public async Task CanceledMatchesForwardsFiltersAndAdminSecret()
    {
        var handler = new CapturingHandler(CanceledMatchesJson);
        var controller = CreateController(handler);

        var result = await controller.GetCanceledMatches(GameMode.FFA, "Tester#1234", 2, 10);

        Assert.That(result, Is.InstanceOf<OkObjectResult>());
        Assert.That(handler.Requests, Has.Count.EqualTo(1));
        var uri = handler.Requests[0].RequestUri!;
        Assert.That(uri.AbsolutePath, Does.EndWith("/admin/canceled-matches"));
        Assert.That(uri.Query, Does.Contain("page=2"));
        Assert.That(uri.Query, Does.Contain("itemsPerPage=10"));
        Assert.That(uri.Query, Does.Contain("gameMode=5"));
        Assert.That(uri.Query, Does.Contain("battleTag=Tester"));
        Assert.That(handler.Requests[0].Headers.Contains("x-admin-secret"), Is.True);
    }

    [Test]
    public async Task AllGameModesOmitsTheGameModeFilter()
    {
        var handler = new CapturingHandler(CanceledMatchesJson);
        var controller = CreateController(handler);

        await controller.GetCanceledMatches(GameMode.Undefined, null, 1, 25);

        Assert.That(handler.Requests[0].RequestUri!.Query, Does.Not.Contain("gameMode="));
    }

    [Test]
    public async Task PageSizeIsClampedBeforeItLeavesTheService()
    {
        var handler = new CapturingHandler(CanceledMatchesJson);
        var controller = CreateController(handler);

        await controller.GetCanceledMatches(GameMode.Undefined, null, 1, 5000);

        Assert.That(handler.Requests[0].RequestUri!.Query, Does.Contain("itemsPerPage=100"));
    }

    [Test]
    public async Task TheProxiedPayloadKeepsIdsSlotIndexesAndTotal()
    {
        var handler = new CapturingHandler(CanceledMatchesJson);
        var controller = CreateController(handler);

        var result = (OkObjectResult)await controller.GetCanceledMatches(GameMode.Undefined, null, 1, 25);
        var payload = (CanceledMatchesResponse)result.Value!;

        Assert.That(payload.total, Is.EqualTo(3));
        Assert.That(payload.matches[0].id, Is.EqualTo("abc1234567"));
        Assert.That(payload.matches[0].floGameId, Is.EqualTo(4242));
        Assert.That(payload.matches[0].players[0].slotIndex, Is.EqualTo(2));
    }

    private static AdminMatchesController CreateController(CapturingHandler handler)
    {
        var client = new MatchmakingServiceClient(new TestHttpClientFactory(new HttpClient(handler)));
        return new AdminMatchesController(client);
    }

    private class TestHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private class CapturingHandler(string responseBody) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json"),
            });
        }
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~AdminMatchesControllerTests"`
Expected: FAIL — `AdminMatchesController` does not exist.

- [ ] **Step 3: Write the implementation**

Create `W3ChampionsStatisticService/Admin/AdminMatchesController.cs`:

```csharp
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using W3C.Contracts.Admin.Permission;
using W3C.Contracts.Matchmaking;
using W3C.Domain.MatchmakingService;
using W3C.Domain.Tracing;
using W3ChampionsStatisticService.WebApi.ActionFilters;

namespace W3ChampionsStatisticService.Admin;

/// <summary>
/// Moderation-facing views over matches that never produced a ranked result.
///
/// Cancelled matches are read straight from matchmaking, which is the only
/// complete record: the MatchCanceledEvent stream omits every game-creation
/// failure, so a read model built from it would be missing exactly the matches
/// moderators care about.
/// </summary>
[ApiController]
[Route("api/admin/matches")]
[Trace]
public class AdminMatchesController(MatchmakingServiceClient matchmakingServiceClient) : ControllerBase
{
    private const int MaxPageSize = 100;

    private readonly MatchmakingServiceClient _matchmakingServiceClient = matchmakingServiceClient;

    /// <param name="gameMode">GameMode.Undefined (0) lists every mode.</param>
    [HttpGet("canceled")]
    [BearerHasPermissionFilter(Permission = EPermission.Moderation)]
    public async Task<IActionResult> GetCanceledMatches(
        [FromQuery] GameMode gameMode = GameMode.Undefined,
        // NOT named battleTag: BearerHasPermissionFilter overwrites an argument
        // with that exact name with the acting moderator's own tag.
        [FromQuery] string playerBattleTag = null,
        [FromQuery] int page = 1,
        [FromQuery] int itemsPerPage = 25)
    {
        if (page < 1) page = 1;
        if (itemsPerPage < 1) itemsPerPage = 25;
        if (itemsPerPage > MaxPageSize) itemsPerPage = MaxPageSize;

        var result = await _matchmakingServiceClient.GetCanceledMatches(new CanceledMatchesGetRequest
        {
            Page = page,
            ItemsPerPage = itemsPerPage,
            GameMode = gameMode,
            BattleTag = playerBattleTag,
        });

        if (result == null)
        {
            return StatusCode(502, "The matchmaking service did not return cancelled matches.");
        }

        return Ok(result);
    }
}
```

Confirm `MatchmakingServiceClient` is registered in `Program.cs` (it is used by `AdminController` already, so no new registration should be needed). If controllers are not auto-discovered, register as the neighbouring admin controllers are.

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~AdminMatchesControllerTests"`
Expected: PASS (5 tests)

- [ ] **Step 5: Verify the whole suite and formatting**

Run: `dotnet build && dotnet test && dotnet format --verify-no-changes`
Expected: all green.

- [ ] **Step 6: Commit**

```bash
git add W3ChampionsStatisticService/Admin/AdminMatchesController.cs WC3ChampionsStatisticService.UnitTests/Admin/AdminMatchesControllerTests.cs
git commit -m "Add the Moderation-gated cancelled matches endpoint"
```

---

### Task 7: Make unavailable replays and chat logs return 404

A replay may never have been recorded, or may have aged into DeepArchive. Today both paths behave wrongly in opposite directions: `GetChatLogs` returns HTTP 200 with a null body, and `GenerateReplay` throws and becomes a 500.

**Files:**
- Modify: `W3C.Domain/ReplayService/ReplayServiceClient.cs:18-33`
- Modify: `W3ChampionsStatisticService/Replays/ReplaysController.cs:21-79`
- Test: `WC3ChampionsStatisticService.UnitTests/Replays/ReplayServiceClientTests.cs` (create)

**Interfaces:**
- Consumes: nothing.
- Produces: `ReplayServiceClient.GenerateReplay` returns `null` when the replay service has nothing; `GetChatLogs` returns `null` on any non-success status. `ReplaysController` turns both nulls into `NotFound()`.

- [ ] **Step 1: Write the failing test**

Create `WC3ChampionsStatisticService.UnitTests/Replays/ReplayServiceClientTests.cs`:

```csharp
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using W3C.Domain.ReplayService;

namespace WC3ChampionsStatisticService.Tests.Replays;

[TestFixture]
public class ReplayServiceClientTests
{
    [Test]
    public async Task AMissingChatLogComesBackAsNullRatherThanAnEmptyObject()
    {
        var client = ClientReturning(HttpStatusCode.NotFound, "not found");

        var chats = await client.GetChatLogs(4242);

        Assert.That(chats, Is.Null);
    }

    [Test]
    public async Task AMissingReplayComesBackAsNullRatherThanThrowing()
    {
        var client = ClientReturning(HttpStatusCode.NotFound, "not found");

        var replay = await client.GenerateReplay(4242);

        Assert.That(replay, Is.Null);
    }

    [Test]
    public async Task AnArchivedReplayComesBackAsNull()
    {
        var client = ClientReturning(HttpStatusCode.Gone, "archived");

        var replay = await client.GenerateReplay(4242);

        Assert.That(replay, Is.Null);
    }

    private static ReplayServiceClient ClientReturning(HttpStatusCode status, string body)
    {
        var handler = new StubHandler(status, body);
        return new ReplayServiceClient(new TestHttpClientFactory(new HttpClient(handler)));
    }

    private class TestHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
    }
}
```

Open `ReplayServiceClient.cs` first and match the constructor signature and the `GenerateReplay` return type in the test; adjust the assertions to that type (it streams, so the "nothing available" value is whatever null-ish result the implementation below returns).

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~ReplayServiceClientTests"`
Expected: FAIL — `GetChatLogs` returns a non-null deserialised object, and `GenerateReplay` throws `HttpRequestException`.

- [ ] **Step 3: Write the implementation**

In `W3C.Domain/ReplayService/ReplayServiceClient.cs`, replace `GetStreamAsync` with a status-checked `GetAsync`, and add the missing status check to `GetChatLogs`:

```csharp
    // A replay legitimately may not exist: it was never recorded, or it has aged
    // into DeepArchive. Callers turn null into a 404 rather than a 500.
    public async Task<Stream> GenerateReplay(int gameId)
    {
        var url = $"{ReplayServiceUrl}/generate/{gameId}?secret={AdminSecret}";
        var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadAsStreamAsync();
    }

    public async Task<ReplayChatsData> GetChatLogs(int gameId)
    {
        var url = $"{ReplayServiceUrl}/chats/{gameId}?secret={AdminSecret}";
        var response = await _httpClient.GetAsync(url);

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var content = await response.Content.ReadAsStringAsync();
        return JsonConvert.DeserializeObject<ReplayChatsData>(content);
    }
```

Add `using System.IO;` if needed, and keep the existing return type of `GenerateReplay` if it already returns a stream by another name.

In `W3ChampionsStatisticService/Replays/ReplaysController.cs`, guard all four actions. For each replay download action:

```csharp
        var replay = await _replayServiceClient.GenerateReplay(floMatchId);
        if (replay == null) return NotFound();
        return File(replay, "application/octet-stream", $"{floMatchId}.w3g");
```

and for each chats action:

```csharp
        var chats = await _replayServiceClient.GetChatLogs(floMatchId);
        if (chats == null) return NotFound();
        return Ok(chats);
```

Preserve each action's existing attributes, file name and content type — only the null guard is new.

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~ReplayServiceClientTests"`
Expected: PASS (3 tests)

- [ ] **Step 5: Verify the whole suite**

Run: `dotnet build && dotnet test && dotnet format --verify-no-changes`
Expected: all green.

- [ ] **Step 6: Commit**

```bash
git add W3C.Domain/ReplayService/ReplayServiceClient.cs W3ChampionsStatisticService/Replays/ReplaysController.cs WC3ChampionsStatisticService.UnitTests/Replays/ReplayServiceClientTests.cs
git commit -m "Return 404 when a replay or chat log is unavailable"
```

---

### Task 8: Raise the hourly replay limit for moderators

Cancelled matches always fall into the strict bucket (10/hour), because `CheckMatchAge` resolves age through `LoadFinishedMatchDetailsBy*`, which returns null for a cancelled match. Ten downloads an hour is too few for a moderator working a list.

**Files:**
- Modify: `W3ChampionsStatisticService/WebApi/ActionFilters/ReplayRateLimitAttribute.cs:46-103`
- Test: `WC3ChampionsStatisticService.UnitTests/Replays/ReplayRateLimitTests.cs` (create)

**Interfaces:**
- Consumes: `IW3CAuthenticationService` (registered at `Program.cs:178`).
- Produces: `ReplayRateLimitAttribute.ModeratorHourlyLimit` (default 50). No signature changes.

- [ ] **Step 1: Write the failing test**

Create `WC3ChampionsStatisticService.UnitTests/Replays/ReplayRateLimitTests.cs`. Read `ReplayRateLimitAttribute` and `RateLimitService` first, then write tests asserting:

```csharp
// 1. A request whose bearer token carries EPermission.Moderation gets
//    HourlyLimit == 50, DailyLimit unchanged at the strict value of 50,
//    PolicyName "replay-moderator", and PartitionKey "moderator:{battleTag}:replay".
// 2. A request with a valid token WITHOUT Moderation keeps HourlyLimit 10
//    and PartitionKey "ip:{ip}:replay".
// 3. A request with no Authorization header is unchanged.
// 4. A request carrying a valid X-API-Token is unchanged - HasValidApiToken wins.
```

Build the `ActionExecutingContext` with a `DefaultHttpContext` whose `RequestServices` resolves stubbed `IMatchRepository`, `ILogger<ReplayRateLimitAttribute>` and `IW3CAuthenticationService`. `DetermineRateLimitContext` is `protected`, so either call it through a small test-only subclass in the fixture or exercise the filter through `OnActionExecutionAsync`; pick whichever the existing `RateLimit` tests already do, if any exist.

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~ReplayRateLimitTests"`
Expected: FAIL — `ModeratorHourlyLimit` does not exist.

- [ ] **Step 3: Write the implementation**

In `ReplayRateLimitAttribute`, add the property beside the other limits:

```csharp
    /// <summary>
    /// Hourly limit for authenticated moderators. The daily limit deliberately stays
    /// at the strict/relaxed value, so a moderator can spend a day's allowance in one
    /// burst but not exceed it.
    /// </summary>
    public int ModeratorHourlyLimit { get; set; } = 50;
```

At the end of `DetermineRateLimitContext`, after the existing call:

```csharp
        var rateLimitContext = await rateLimitService.DetermineRateLimitContext(
            context.HttpContext,
            Scope,
            policyName,
            hourlyLimit,
            dailyLimit);

        // An API token already carries its own negotiated limits; never override them.
        if (rateLimitContext.HasValidApiToken)
        {
            return rateLimitContext;
        }

        var moderatorBattleTag = await TryGetModeratorBattleTag(context, logger);
        if (moderatorBattleTag != null)
        {
            rateLimitContext.HourlyLimit = ModeratorHourlyLimit;
            rateLimitContext.PolicyName = "replay-moderator";
            // Partition per moderator rather than per IP so colleagues behind one
            // address do not consume each other's budget.
            rateLimitContext.PartitionKey = $"moderator:{moderatorBattleTag}:{Scope}";
        }

        return rateLimitContext;
```

Add the helper, resolving the auth service from DI rather than `new`-ing it as `BearerHasPermissionFilter` does:

```csharp
    private static async Task<string> TryGetModeratorBattleTag(ActionExecutingContext context, ILogger logger)
    {
        try
        {
            string authHeader = context.HttpContext.Request.Headers["Authorization"];
            if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer "))
            {
                return null;
            }

            var token = authHeader["Bearer ".Length..].Trim();
            var authService = context.HttpContext.RequestServices.GetRequiredService<IW3CAuthenticationService>();
            var user = authService.GetUserByToken(token, false);

            if (user == null || string.IsNullOrEmpty(user.BattleTag)) return null;
            if (!user.IsAdmin) return null;
            if (user.Permissions == null || !user.Permissions.Contains(EPermission.Moderation)) return null;

            return user.BattleTag;
        }
        catch (Exception ex)
        {
            // A bad token must not break the request; fall through to the IP-based limit.
            logger.LogDebug(ex, "Could not resolve a moderator identity for replay rate limiting");
            return null;
        }
    }
```

Match `GetUserByToken`'s real signature and the shape of `user.Permissions` by reading `BearerHasPermissionFilter.cs:16-59` — copy its checks exactly rather than guessing. Remove the `async`/`await` if the auth call is synchronous. Add `using Microsoft.Extensions.DependencyInjection;`, `using W3C.Contracts.Admin.Permission;` and `using W3ChampionsStatisticService.Services;` as required.

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~ReplayRateLimitTests"`
Expected: PASS

- [ ] **Step 5: Verify the whole suite**

Run: `dotnet build && dotnet test && dotnet format --verify-no-changes`
Expected: all green.

- [ ] **Step 6: Commit and open the Phase 2 PR**

```bash
git add W3ChampionsStatisticService/WebApi/ActionFilters/ReplayRateLimitAttribute.cs WC3ChampionsStatisticService.UnitTests/Replays/ReplayRateLimitTests.cs
git commit -m "Give authenticated moderators a higher hourly replay limit"
git push
```

Open a PR covering Phase 2. Note in the description that it requires the Phase 1 matchmaking deployment to return data, and that `GM_FOOTMEN_FRENZY` is now treated as an FFA mode (previously ongoing Footmen Frenzy matches exposed real battletags through `/api/matches/ongoing`).

---

# Phase 3 — website

Work in `/home/francis/repos/w3c-website`. Run `git checkout -b feat/cancelled-matches-admin` before Task 9.

Commands: `npm run lint`, `npm run dprint`, `npm run type-check`, `npm test`.

Note: vitest runs in `environment: "node"` with no vue plugin, so `.vue` files cannot be unit tested. Only the service layer gets tests.

---

### Task 9: Cancelled match types and service

**Files:**
- Create: `src/types/admin/CancelledMatch.ts`
- Create: `src/services/admin/CancelledMatchService.ts`
- Create: `src/services/admin/CancelledMatchService.test.ts`

**Interfaces:**
- Consumes: Phase 2's `GET api/admin/matches/canceled`, and the existing `GET api/replays/by-flo-id/{floMatchId}/chats`.
- Produces:
  - `interface CancelledMatchPlayer { battleTag: string; name?: string; race: number; team: number; slotIndex?: number; mmr?: { rating: number }; ranking?: { leagueOrder?: number }; country?: string; }`
  - `interface CancelledMatch { id: string; gamename?: string; gameMode: number; mapName?: string; map?: string; gateway?: number; season?: number; startTime: number; createdAt?: string; canceledAt?: string; floGameId?: number | null; players: CancelledMatchPlayer[]; }`
  - `interface CancelledMatchesPage { total: number; matches: CancelledMatch[]; }`
  - `class CancelledMatchService { getCancelledMatches(token, opts): Promise<CancelledMatchesPage>; getChatLogByFloId(token, floGameId): Promise<ReplayChatLog>; }`

  Tasks 11 and 12 consume these.

- [ ] **Step 1: Write the failing test**

Create `src/services/admin/CancelledMatchService.test.ts`:

```ts
import { test } from "vitest";
import { strict as assert } from "node:assert";
import { CancelledMatchService } from "./CancelledMatchService";

function urlOf(input: RequestInfo | URL): string {
  if (typeof input === "string") return input;
  if (input instanceof URL) return input.href;
  return input.url;
}

function serviceWith(response: { status: number; body?: unknown }) {
  const calls: { url: string; method: string }[] = [];
  const impl: typeof globalThis.fetch = (input, init) => {
    calls.push({ url: urlOf(input), method: init?.method ?? "GET" });
    const body = response.body === undefined ? null : JSON.stringify(response.body);
    return Promise.resolve(
      new Response(body, {
        status: response.status,
        headers: body === null ? undefined : { "Content-Type": "application/json" },
      }),
    );
  };
  return {
    calls,
    service: new CancelledMatchService({ endpoint: "https://api.example.com/", fetch: impl }),
  };
}

test("getCancelledMatches requests the admin endpoint with paging", async () => {
  const { service, calls } = serviceWith({ status: 200, body: { total: 2, matches: [] } });

  const page = await service.getCancelledMatches("tok", { gameMode: 0, page: 2, itemsPerPage: 25 });

  assert.equal(page.total, 2);
  assert.ok(calls[0].url.startsWith("https://api.example.com/api/admin/matches/canceled?"));
  assert.ok(calls[0].url.includes("page=2"));
  assert.ok(calls[0].url.includes("itemsPerPage=25"));
});

test("getCancelledMatches omits an all-modes filter and an empty battle tag", async () => {
  const { service, calls } = serviceWith({ status: 200, body: { total: 0, matches: [] } });

  await service.getCancelledMatches("tok", { gameMode: 0, page: 1, itemsPerPage: 25, battleTag: "" });

  assert.ok(!calls[0].url.includes("gameMode="));
  assert.ok(!calls[0].url.includes("playerBattleTag="));
});

test("getCancelledMatches sends the selected mode and encodes the battle tag", async () => {
  const { service, calls } = serviceWith({ status: 200, body: { total: 0, matches: [] } });

  await service.getCancelledMatches("tok", { gameMode: 5, page: 1, itemsPerPage: 25, battleTag: "Tester#1234" });

  assert.ok(calls[0].url.includes("gameMode=5"));
  assert.ok(calls[0].url.includes("playerBattleTag=Tester%231234"));
});

test("getCancelledMatches surfaces a failure rather than an empty page", async () => {
  const { service } = serviceWith({ status: 403 });

  await assert.rejects(() => service.getCancelledMatches("tok", { gameMode: 0, page: 1, itemsPerPage: 25 }));
});

test("getChatLogByFloId reads the by-flo-id chat route", async () => {
  const { service, calls } = serviceWith({ status: 200, body: { players: [], messages: [], events: [] } });

  await service.getChatLogByFloId("tok", 4242);

  assert.equal(calls[0].url, "https://api.example.com/api/replays/by-flo-id/4242/chats");
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `npx vitest run src/services/admin/CancelledMatchService.test.ts`
Expected: FAIL — cannot resolve `./CancelledMatchService`.

- [ ] **Step 3: Write the types**

Create `src/types/admin/CancelledMatch.ts`:

```ts
/** A player in a cancelled match, as matchmaking recorded them. */
export interface CancelledMatchPlayer {
  battleTag: string;
  name?: string;
  inviteName?: string;
  race: number;
  team: number;
  country?: string;
  /**
   * 0-based FLO lobby slot. Flo shows anonymised players as `Player {slotIndex + 1}`,
   * so always add one before displaying. Absent for matches cancelled before the
   * FLO game was created.
   */
  slotIndex?: number;
  mmr?: { rating: number };
  ranking?: { leagueOrder?: number };
}

/**
 * A match that ended without a result. There is no winner, no score and no MMR
 * change - those fields do not exist for these matches, so nothing here should be
 * rendered as a win or a loss.
 */
export interface CancelledMatch {
  id: string;
  gamename?: string;
  gameMode: number;
  map?: string;
  mapName?: string;
  gateway?: number;
  season?: number;
  startTime: number;
  createdAt?: string;
  /** When the match was cancelled. */
  canceledAt?: string;
  /** Null when the match was cancelled before the FLO game existed: no replay, no chat log. */
  floGameId?: number | null;
  players: CancelledMatchPlayer[];
}

export interface CancelledMatchesPage {
  total: number;
  matches: CancelledMatch[];
}

/** A replay or chat log can only exist once FLO created the game. */
export function hasReplayArtifacts(match: CancelledMatch): boolean {
  return match.floGameId != null && match.floGameId > 0;
}

/** The number a player saw in game for an anonymised match. */
export function displaySlot(player: CancelledMatchPlayer): number | undefined {
  return player.slotIndex == null ? undefined : player.slotIndex + 1;
}
```

- [ ] **Step 4: Write the service**

Create `src/services/admin/CancelledMatchService.ts`:

```ts
import { AuthorizedClient, type AuthorizedClientDeps } from "@/services/http/AuthorizedClient";
import type { CancelledMatchesPage } from "@/types/admin/CancelledMatch";
import type { ReplayChatLog } from "@/store/admin/replayManagement/types";

export interface CancelledMatchQuery {
  /** 0 (Undefined) means every game mode. */
  gameMode: number;
  page: number;
  itemsPerPage: number;
  battleTag?: string;
}

/**
 * Reads cancelled matches and, on demand, the chat log for one of them.
 *
 * Takes its endpoint (and optionally a fetch) rather than importing API_URL, so
 * it can be constructed in tests - `@/config/env` reads `window` at module load
 * and cannot be imported outside a browser.
 *
 * The chat log call hits the replay service, which costs money per call. Only
 * ever call it for a single match in response to an explicit user action - never
 * to enrich a list.
 */
export class CancelledMatchService {
  private readonly client: AuthorizedClient;

  constructor(deps: AuthorizedClientDeps) {
    this.client = new AuthorizedClient(deps);
  }

  async getCancelledMatches(token: string, query: CancelledMatchQuery): Promise<CancelledMatchesPage> {
    const params = new URLSearchParams();
    params.set("page", String(query.page));
    params.set("itemsPerPage", String(query.itemsPerPage));

    if (query.gameMode) {
      params.set("gameMode", String(query.gameMode));
    }

    // The backend parameter is deliberately NOT called "battleTag":
    // BearerHasPermissionFilter overwrites an action argument of that exact name
    // with the acting moderator's own tag, which would silently turn this search
    // into "matches I played in".
    if (query.battleTag) {
      params.set("playerBattleTag", query.battleTag);
    }

    return await this.client.getJson<CancelledMatchesPage>(
      `api/admin/matches/canceled?${params}`,
      token,
    );
  }

  /**
   * Cancelled matches have no Matchup row, so the id-based chat route cannot find
   * them. The FLO game id is the only key the replay service understands.
   */
  async getChatLogByFloId(token: string, floGameId: number): Promise<ReplayChatLog> {
    return await this.client.getJson<ReplayChatLog>(
      `api/replays/by-flo-id/${encodeURIComponent(floGameId)}/chats`,
      token,
    );
  }
}
```

Open `src/store/admin/replayManagement/types.ts` and import the chat log type by its real exported name; if it lives elsewhere, follow `AdminReplayChatLogMessages.vue`'s import.

- [ ] **Step 5: Run the test to verify it passes**

Run: `npx vitest run src/services/admin/CancelledMatchService.test.ts`
Expected: PASS (5 tests)

- [ ] **Step 6: Verify and commit**

Run: `npm run lint && npm run dprint && npm run type-check && npm test`

```bash
git add src/types/admin/CancelledMatch.ts src/services/admin/CancelledMatchService.ts src/services/admin/CancelledMatchService.test.ts
git commit -m "Add the cancelled match admin service and types"
```

---

### Task 10: Neutral result rendering and by-flo-id replay download

`won` is the only field the match-history leaf components hard-depend on. The proxied payload **will** contain `won: false`, because `PlayerMMrChange.won` is a non-nullable `bool` in C#, so without a flag every player renders as a loser.

**Files:**
- Modify: `src/components/matches/PlayerMatchInfo.vue` (`won` computed at `:135-143`, `playerColorClass` at `:167-173`, template at `:21`, `:27-28`, `:33-35`)
- Modify: `src/components/matches/TeamMatchInfo.vue`
- Modify: `src/components/matches/DownloadReplayIcon.vue`

**Interfaces:**
- Consumes: nothing.
- Produces: `PlayerMatchInfo` and `TeamMatchInfo` accept `noWinner?: boolean`; `DownloadReplayIcon` accepts `floGameId?: number | null`. Task 12 sets all three.

- [ ] **Step 1: Add `noWinner` to `PlayerMatchInfo.vue`**

Add to the component's props:

```ts
    noWinner: {
      type: Boolean,
      default: false,
    },
```

and make the `won` computed return no colour when set. Keep the existing `unfinishedMatch` branch — do not merge the two, because `unfinishedMatch` also suppresses spoiler logic, which is not wanted here:

```ts
    const won = computed<string>(() => {
      if (props.unfinishedMatch) return "";
      // Cancelled matches have no result. The payload still carries won: false
      // because the backend field is a non-nullable bool, so it must be ignored
      // rather than trusted.
      if (props.noWinner) return "";
      if (Object.prototype.hasOwnProperty.call(props.player, "won")) {
        return props.player.won ? "w3-won" : "w3-lost";
      }
      return "";
    });
```

Match the existing code's exact shape when editing — read `:135-143` first. `playerColorClass` already feeds the name link and both MMR-delta spans, so no template change is needed.

- [ ] **Step 2: Pass `noWinner` through `TeamMatchInfo.vue`**

Add the same prop declaration, and bind it on the `PlayerMatchInfo` usage:

```vue
      :no-winner="noWinner"
```

- [ ] **Step 3: Add `floGameId` to `DownloadReplayIcon.vue`**

Change the props and the URL construction:

```ts
const { gameId, floGameId } = defineProps({
  gameId: {
    type: String,
    required: false,
    default: "",
  },
  /**
   * Download by FLO game id instead of match id. Cancelled matches have no
   * Matchup row, so the id-based route cannot resolve them.
   */
  floGameId: {
    type: Number,
    required: false,
    default: null,
  },
});
```

and inside `downloadReplay`:

```ts
    const url = floGameId != null
      ? `${API_URL}api/replays/by-flo-id/${floGameId}`
      : `${API_URL}api/replays/${gameId}`;
```

and the download filename:

```ts
    a.download = `${floGameId ?? gameId}.w3g`;
```

The existing 404 and 429 handling already covers "replay unavailable", which Phase 2 Task 7 now returns correctly.

- [ ] **Step 4: Verify existing usages still type-check**

Run: `npm run type-check && npm run lint`
Expected: clean. `gameId` becoming optional must not break `MatchesGrid.vue:118` or `MatchDetail.vue:87`, which still pass it.

- [ ] **Step 5: Commit**

```bash
git add src/components/matches/PlayerMatchInfo.vue src/components/matches/TeamMatchInfo.vue src/components/matches/DownloadReplayIcon.vue
git commit -m "Support result-less matches and by-flo-id replays in match components"
```

---

### Task 11: Load a chat log by FLO game id

**Files:**
- Modify: `src/store/admin/replayManagement/store.ts` (beside `loadChatLog` at `:11`)
- Modify: `src/components/admin/replays/AdminReplayChatLogMessages.vue` (props at `:93-95`, load at `:206`)

**Interfaces:**
- Consumes: `CancelledMatchService.getChatLogByFloId` from Task 9.
- Produces: `replayManagementStore.loadChatLogByFloId(floGameId: number)`; `AdminReplayChatLogMessages` accepts `floGameId?: number | null` and loads by it when present. Task 12 renders this component with `floGameId`.

- [ ] **Step 1: Add the store action**

In `src/store/admin/replayManagement/store.ts`, beside `loadChatLog`, using the same lazy-singleton pattern the other admin stores use:

```ts
    /**
     * Cancelled matches have no Matchup row, so the id-based chat route 404s for
     * them. This is the only way to reach their chat log.
     */
    async loadChatLogByFloId(floGameId: number) {
      const token = useOauthStore().token;
      this.chatLog = await getCancelledMatchService().getChatLogByFloId(token, floGameId);
    },
```

Match the surrounding code exactly: read `loadChatLog` first and mirror how it obtains the token, where it assigns the result, and how it reports errors. Add the lazy service accessor at module level:

```ts
let _cancelledMatchService: CancelledMatchService | null = null;
function getCancelledMatchService(): CancelledMatchService {
  // API_URL reads window at module load, so the service cannot be constructed
  // at module top level.
  return (_cancelledMatchService ??= new CancelledMatchService({ endpoint: API_URL }));
}
```

- [ ] **Step 2: Accept `floGameId` in the messages component**

In `src/components/admin/replays/AdminReplayChatLogMessages.vue`, add the prop beside `matchId`:

```ts
    floGameId: {
      type: Number,
      required: false,
      default: null,
    },
```

and branch at the load site (`:206`):

```ts
        if (props.floGameId != null) {
          await replayManagementStore.loadChatLogByFloId(props.floGameId);
        } else {
          await replayManagementStore.loadChatLog(props.matchId);
        }
```

Also relax `matchId` to `required: false, default: ""` so the component can be used with only a FLO id.

The existing `getPlayerName` join at `:157` (`log.value.players.find((x) => x.id == playerId)?.name`) needs no change: `players[].id` from the replay service is already the in-game slot number and carries the real, unmasked name.

- [ ] **Step 3: Verify**

Run: `npm run type-check && npm run lint && npm test`
Expected: clean.

- [ ] **Step 4: Commit**

```bash
git add src/store/admin/replayManagement/store.ts src/components/admin/replays/AdminReplayChatLogMessages.vue
git commit -m "Allow loading a chat log by FLO game id"
```

---

### Task 12: The Cancelled Matches admin page

**Files:**
- Create: `src/components/admin/AdminCancelledMatches.vue`
- Modify: `src/router/types.ts` (`EAdminRouteName`)
- Modify: `src/router/index.ts` (import near `:66`, child route in the `/admin` children array at `:266-304`)
- Modify: `src/components/admin/AdminNavigation.vue` (icon import at `:65-73`, child entry in the Moderation group at `:157-205`)

**Interfaces:**
- Consumes: `CancelledMatchService`, `CancelledMatch`, `hasReplayArtifacts`, `displaySlot` (Task 9); `noWinner` and `floGameId` props (Task 10); `AdminReplayChatLogMessages` with `floGameId` (Task 11).
- Produces: route `admin-cancelled-matches`.

- [ ] **Step 1: Add the route name**

In `src/router/types.ts`, in `EAdminRouteName`:

```ts
  CANCELLED_MATCHES = "Admin - Cancelled Matches",
```

- [ ] **Step 2: Register the route**

In `src/router/index.ts`, add the import beside the other admin components:

```ts
import AdminCancelledMatches from "@/components/admin/AdminCancelledMatches.vue";
```

and the child entry in the `/admin` children array:

```ts
        { path: "admin-cancelled-matches", name: EAdminRouteName.CANCELLED_MATCHES, component: AdminCancelledMatches },
```

- [ ] **Step 3: Add the nav entry**

In `src/components/admin/AdminNavigation.vue`, import an icon in the `@mdi/js` block (for example `mdiCancel`), then add a child **inside the existing Moderation group** (`:157-205`):

```ts
          {
            title: "Cancelled Matches",
            icon: mdiCancel,
            permission: EPermission.Moderation,
            component: "admin-cancelled-matches",
            routeName: EAdminRouteName.CANCELLED_MATCHES,
          },
```

`component` **must** equal the route's `path` segment — the `/admin` landing redirect pushes `admin/${firstItem.component}` (`:129-138`). Do not create a new top-level group.

- [ ] **Step 4: Create the page**

Create `src/components/admin/AdminCancelledMatches.vue`, following `AdminJobs.vue` for structure (lazy service singleton, token from `useOauthStore`, local refs, `v-alert` errors, hardcoded English strings — recent admin pages use no `$t`).

It must include:

1. **A permission guard.** There is no router guard anywhere in this app, so any admin can deep-link here:

```ts
const canModerate = computed(() =>
  oauthStore.permissions.includes(EPermission[EPermission.Moderation]));
```

Render a refusal message instead of the table when false.

2. **Filters:** the existing `GameModeSelect` component (`@/components/common/GameModeSelect.vue`, as used at `Matches.vue:23`) defaulting to all modes, a battletag text field, and a `v-pagination` driven by `total`.

3. **A notes panel** (a `v-alert type="info"` above the table) with exactly these points:

```
- Drawn and stalled matches are only marked cancelled by a cleanup sweep that runs
  6 hours after the match started, so a match that just ended will not appear yet.
- Replay and chat log are unavailable for matches cancelled before the game server
  was created.
- No cancellation reason is recorded, so this list cannot show why a match ended.
```

4. **Rows** showing the game mode, map, start time, cancellation time (`canceledAt`), and the players. Render players grouped by team using `TeamMatchInfo` with `:no-winner="true"`, or a simple per-player list — but every player must show `Player {displaySlot(player)} — {battleTag}` when `slotIndex` is present, and the battletag alone when it is not.

5. **Per-row actions:** a `DownloadReplayIcon` with `:flo-game-id="match.floGameId"`, and a button opening a `v-dialog` containing `<admin-replay-chat-log-messages :flo-game-id="match.floGameId" />`. Both disabled with an explanatory tooltip when `hasReplayArtifacts(match)` is false.

**The chat log must only be fetched when the dialog is opened** — render the component with `v-if="openChatMatchId === match.id"` so it mounts on demand. Never fetch chat logs while rendering the list; each call costs money.

- [ ] **Step 5: Verify**

Run: `npm run lint && npm run dprint && npm run type-check && npm test`
Expected: all clean.

- [ ] **Step 6: Manual verification**

Run the app against a backend with the Phase 2 endpoint available. Confirm:
- The page appears in the Moderation section of the admin sidebar for a Moderation account, and not for an admin without it.
- Switching game mode and searching a battletag refetches and resets to page 1.
- No player is coloured as a winner or loser.
- A match with `floGameId: null` shows both actions disabled.
- Opening the chat dialog issues exactly one request, and closing and reopening does not spam the replay service.
- In an anonymised FFA match, slot numbers line up with the names in the chat log.

- [ ] **Step 7: Commit and open the Phase 3 PR**

```bash
git add src/components/admin/AdminCancelledMatches.vue src/router/types.ts src/router/index.ts src/components/admin/AdminNavigation.vue
git commit -m "Add the cancelled matches admin page"
git push -u origin feat/cancelled-matches-admin
```

---

## Self-Review Notes

Checked against the spec:

- **Read model rejected in favour of the proxy** — Tasks 2, 3, 5, 6.
- **`slotIndex` persisted upstream and displayed +1** — Tasks 1, 4, 9, 12.
- **`canceledAt` from `_updated_at`, never from an ObjectId timestamp** — Task 2 (sort), Task 4 (mapping), Task 12 (display).
- **Reuse existing DTOs rather than new ones** — Task 4 reuses `Match`/`PlayerMMrChange`/`UnfinishedMatchPlayer`; the `[JsonProperty("_id")]` requirement is a correction discovered while planning, since Newtonsoft ignores `[BsonElement]` and the id would otherwise be null.
- **`api/admin/matches/canceled` sub-layering** — Task 6.
- **Moderator replay limit: hourly 50, daily untouched** — Task 8.
- **Replay/chat unavailability returns 404** — Task 7.
- **`GM_FOOTMEN_FRENZY` as FFA** — already committed; regression test in Task 4.
- **No replay-service calls in the list path** — enforced in Task 9's service docstring and Task 12 Step 4/6.
- **`noWinner`** — Task 10, with the `won: false` explanation.
- **Moderation gate, no audit logging** — Task 6 (server) and Task 12 (in-page).
- **Notes panel** — Task 12, copy supplied verbatim.

Spec items intentionally **not** given tasks, matching the spec's out-of-scope list: the missing `pushMatchCanceled`/`pushMatchStart` (raised in the Phase 1 PR description), `LeaveDraw` handling, persisting the cancellation reason, and displaying `MatchLogs`.

`playerScores: []` needs no task: the new endpoint returns matchmaking match documents and never populates `playerScores`, and the page does not route through `MatchDetail.vue`. The constraint is recorded in the spec so a future change does not reintroduce it.
