# Notifications (server side)

Server-side detection and delivery of player-facing notification events,
consumed by launcher-e's notification center. Current tenant:
`FriendRankPromoted` — a friend's league promotion, pushed live to online
friends over the website-backend hub.

## Design

Detection rides the rank sync but owns its state. `RankSyncHandler` checks
out `RankingChangedEvent` batches (matchmaking-published whole-league
rosters), upserts `Rank` documents, then hands the batch to
`FriendRankPromotionNotifier.ObserveSyncedRanks`, which diffs it against the
notifier's own `LeagueBaseline` collection — the last league it has seen per
single-member standing, keyed by the standing's deterministic `Rank` id.
Direction comes from `LeagueConstellation` order (0 is the top; a promotion
is the order decreasing). A same-order division shuffle is silent.

Baseline mechanics:

- **Baselines advance before any push**, so delivery is at most once: a
  failed push never resurfaces as a stale diff on a later batch.
- **Silent transitions (demotions, shuffles, unresolvable constellations)
  still advance the baseline** — a later promotion diffs against the true
  previous league, and an unannounceable transition never resurfaces.
- **Only new and changed baselines are written**, so write cost tracks
  transitions, not roster size. Reads are one indexed `_id` lookup per batch.
- A standing without a baseline — season start, or the first batch after
  deploy — records silently.

The baseline store is the natural diff source for any future own-promotion
detection (the "promoted while away" ledger row).

Scope rules:

- **Single-member standings only** (`Player2Id == null`). A team standing is a
  fact about the team, not about one member. Team promotions, if ever wanted,
  are a second detection branch with team framing — not a filter relaxation.
- **New seasons are silent by construction** — season is part of the baseline
  id, so a new season has no baseline document and produces no false
  transition.
- Backlog batches diff against the last occurrence per document id, matching
  the bulk upsert's last-write-wins.

Fan-out is receiver-side truth: `LoadFriendlistsContaining(battleTag)` finds
every friend list holding the promoted player (an indexed read —
`FriendRepository` implements `IRequiresIndexes` with a multikey index over
`Friends`); the intersection with `ConnectionMapping` gives the online
receivers; each connection gets the additive hub message `FriendRankPromoted`
(battleTag, season, gateway, gameMode, race, new + old league
name/order/division). Clients that do not know the message ignore it.

The notifier swallows its own failures: rank sync never depends on the push.

Tests: `FriendRankPromotionTests` (7) — event batch in, `SendCoreAsync`
capture out; baseline assertions pin the at-most-once and silent-advance
rules.

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
