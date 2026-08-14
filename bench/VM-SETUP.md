# FriendRankPromoted live test — win11 VM setup

Bench material: this folder never merges into the eventual PR.

Run end-to-end on 2026-08-14: both trigger runs behaved as documented and the
in-game toast + bell row rendered off the real push.

The rig: this backend + its mongo run in docker ON the VM; the dev launcher's
website-backend URLs point at `127.0.0.1:6123`; promotions are triggered by
inserting matchmaking-shaped events into the mongo. Everything between the
insert and the toast is the real shipped pipeline: RankSyncHandler checkout →
baseline diff → friend fan-out → SignalR push → launcher handler.

Prerequisites: docker running; this repo on `dev/friend-rank-promoted`; the
launcher checkout on `dev/notification-center`, run as a dev build (the
localhost-http allowance in tauri-plugin-auth exists only under
`debug_assertions`).

## Backend up

```bash
docker network create w3c-network-local   # once; compose expects it as external
docker compose -f docker-compose.yml -f bench/compose.handlers.yml up -d --build
docker logs w3champions-statistic-service-dev --tail 20   # Kestrel listening on :80
```

The override file turns `START_HANDLERS` on — required for the two 5-second
pollers (LeagueSyncHandler, RankSyncHandler), safe because the compose mongo is
isolated. `MATCHMAKING_API`/`CHAT_API` in `environments/local-compose.env` point
at sibling containers that don't exist here; this test never calls them.

## Seed (your real battleTag — the launcher must be signed in as this account)

```bash
docker exec -i -e USER_BATTLETAG="You#1234" mongodb-website-backend-local-compose \
  mongosh "mongodb://w3champions:w3champions@localhost:27017" --quiet \
  < bench/seed-friend-rank.mongosh.js
```

## Launcher at the local backend

In the launcher checkout, `src/environment.ts`, `TestEnvironment` (the `usePtr`
one) — an UNCOMMITTED edit, reverted after the test:

```ts
statisticServiceBackendUrl: "http://127.0.0.1:6123/",
statisticServiceBackendWebSocketUrl: "ws://127.0.0.1:6123/",
```

Keep the trailing slashes (both values are string-concatenated). Leave every
other endpoint untouched — that is load-bearing, see Troubleshooting. Run in
PTR mode: sign-in then goes through the TEST
identification-service, whose JWTs this backend's baked-in public key
validates. Relaunch after the edit — the URLs are read once at module load.

Signed in, the friends list is served by the local backend and shows
`TestFriend#1234`.

## Trigger

```bash
docker exec -i mongodb-website-backend-local-compose \
  mongosh "mongodb://w3champions:w3champions@localhost:27017" --quiet \
  < bench/promote-test-friend.mongosh.js
```

- Run 1: TestFriend enters Silver — records the baseline, SILENT by design.
- Wait ~10s (the script reads the baseline the 5s poller writes).
- Run 2: Silver → Gold. Toast within ~5s — "positive" severity, toast-only
  (deliberately never a Moment), plus a bell row whose click opens the friend's
  profile.
- Each further run promotes one league, up to Grandmaster.

## Troubleshooting

- `401` on `/auth/session` or `AuthorizationFailed` on the hub: the session is
  not a test-identity login — confirm PTR mode.
- Baseline never appears in `LeagueBaseline` (db `W3Champions-Statistic-Service`):
  handlers are off — `docker exec w3champions-statistic-service-dev env | grep START_HANDLERS`
  must say `true` (the override file was skipped).
- Baseline advances but no toast: check the LAUNCHER side first. The handler
  resolves the mode name from `backendStore.gameModes`, populated by the
  MATCHMAKING handshake — not by this backend. Without it the launcher logs
  `Skipping friend rank-up notification: no name for game mode 1` and drops
  the toast, which from the backend looks identical to success. This is why
  the environment edit above repoints only the two statistic URLs: PTR keeps
  matchmaking on test, where the modes come from. (Hence also: `useLocal=1`
  is NOT a shortcut around the edit — LocalEnvironment already carries the
  `:6123` URLs, but repoints matchmaking and chat at local stacks this rig
  doesn't run, so the toast never renders.)
- Only then the push side — `docker logs w3champions-statistic-service-dev`
  for "Friend rank promotion" warnings, and the launcher's hub connection
  state. A successful hub connect logs NOTHING, so a silent auth rejection
  and a healthy connection look the same from the backend; confirm receipt
  from the launcher.

## Teardown

```bash
docker compose -f docker-compose.yml -f bench/compose.handlers.yml down -v   # -v wipes the seeded data
```

Revert the `environment.ts` edit.
