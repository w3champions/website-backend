# Notifications (server side)

Server-side detection and delivery of player-facing notification events,
consumed by launcher-e's notification center. Current tenant:
`FriendRankPromoted` — a friend's league promotion, pushed live to online
friends over the website-backend hub.

## Design

Detection rides the rank sync. `RankSyncHandler` checks out
`RankingChangedEvent` batches (matchmaking-published whole-league rosters) and
upserts `Rank` documents. `FriendRankPromotionNotifier` runs two-phase around
that upsert:

1. `CaptureOldRanks` — snapshots the standings the batch is about to replace
   (`LoadRanksByIds`), before the upsert overwrites them.
2. `NotifyPromotions` — after the upsert, diffs old vs new league per
   document. Direction comes from `LeagueConstellation` order (0 is the top;
   a promotion is the order decreasing). A same-order division shuffle is
   silent.

Scope rules:

- **Single-member standings only** (`Player2Id == null`). A team standing is a
  fact about the team, not about one member. Team promotions, if ever wanted,
  are a second detection branch with team framing — not a filter relaxation.
- **New seasons are silent by construction** — season is part of the `Rank`
  document id, so a new season has no old document and produces no false
  transition.
- Backlog batches diff against the last occurrence per document id, matching
  the bulk upsert's last-write-wins.

Fan-out is receiver-side truth: `LoadFriendlistsContaining(battleTag)` finds
every friend list holding the promoted player; the intersection with
`ConnectionMapping` gives the online receivers; each connection gets the
additive hub message `FriendRankPromoted` (battleTag, season, gateway,
gameMode, race, new + old league name/order/division). Clients that do not
know the message ignore it.

The notifier swallows its own failures: rank sync never depends on the push.

Tests: `FriendRankPromotionTests` (6) — event batch in, `SendCoreAsync`
capture out.

## Planned: A-shape refactor

Two changes bring the area onto the service's standard read-model
conventions (the ones `PlayOverviewHandler`, the lag-report handlers and the
telemetry repository already use):

1. **Self-owned detection state.** The pre-upsert snapshot couples detection
   to `RankSyncHandler`'s write timing. The refactor gives the notifier its
   own collection — last-known league per player+season+mode+race, with a
   deterministic id mirroring `Rank`'s — diffed against each incoming batch
   and then updated. Replay-safe, decoupled from the rank sync's internals,
   and the store doubles as the baseline any future durable notification row
   would diff against.
2. **Index for the fan-out query.** `FriendRepository` implements
   `IRequiresIndexes` with a multikey index on `Friends`, so
   `LoadFriendlistsContaining` is one indexed read per batch.

## Blast radius

Server-only. The launcher's notification center treats the hub payload as
data: its catalog, delivery rules and UI decide loudness client-side and are
unchanged by anything in this area. The payload contract of
`FriendRankPromoted` is the boundary — while its shape holds, nothing
client-side moves.

## Relation to prj/new-ranking-system

This area builds on the current ranking system, in master conventions,
targeting master. See [new-ranking-system-tie-in.md](new-ranking-system-tie-in.md)
for the decision and the bounded port when that project lands.
