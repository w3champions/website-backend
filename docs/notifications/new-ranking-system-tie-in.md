# Tie-in: prj/new-ranking-system

Status of the project branch (checked 2026-08-13): last commit 2026-06-11 on
website-backend, 2026-06-10 on launcher-e; no open PRs target it in either
repo.

What the project builds: progression read-models (`PlayerProgression`,
season-keyed; `ProgressionMilestone` and `ProgressionPrestige`, permanent), an
apex leaderboard, an owner-private `my-milestones` endpoint, and in-tree docs
(`docs/ranking/`). All of it uses machinery that lives on master today: the
match-finished read-model framework (`IMatchFinishedReadModelHandler`,
`HandlerVersions`), `[InjectActingPlayerAuthCode]` owner-private serving, and
`IRequiresIndexes` startup indexing.

## The decision (Scenario A, 2026-08-13)

Notification work builds on the **current** ranking system, in master
conventions, targeting master. The project branch is precedent — proof that
these conventions carry per-player durable state through review — and not a
base branch. Nothing in this area depends on `lastDelta`,
`updatedProgression`, or any progression collection.

## What ports when the project lands

The port is bounded and known:

1. **Detection substrate.** The promotion diff moves from `Rank` documents
   (the RankingChanged sync) to `PlayerProgression` / `updatedProgression`
   (match-finished events) — the same two-phase shape against a different
   collection. If upstream ships explicit `promoted` / `divisionUp` flags,
   diffing reduces to flag reads.
2. **League vocabulary.** `LeagueConstellation` order today; the progression
   league/division fields then.
3. **Launcher touchpoints** (tracked on the launcher branch): celebration
   dedup with the score-screen `PromotionCeremony` (keyed per
   `lastResultMatchId`), near-duplicate i18n keys, `useReducedMotion`
   adoption.

## Blast radius

The `FriendRankPromoted` hub payload is the client boundary. The launcher's
notification center — catalog, delivery modes, toast/Moment rules, settings —
is unchanged by the port; only the server-side detection substrate moves.
Everything client-side stays the same.
