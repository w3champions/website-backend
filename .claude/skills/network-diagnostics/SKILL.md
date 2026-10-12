---
name: network-diagnostics
description: Use when investigating a W3Champions player's connection quality, a flo node that players report as laggy, or whether a new server IP would serve a region well, using the website-backend remote network diagnostics admin API (runs, measurements, observations, aggregate). Also use for "measure player X", "which ISPs have loss to node N", "is direct or proxied better for these users", or "evaluate this IP".
---

# Network diagnostics (remote measurements from players' launchers)

Admins (and you) ask players' launchers to measure the network: traceroute/MTR (ICMP and TCP), TCP
ping and flo UDP ping, against flo nodes, core servers, public baselines and custom IPs, directly and
through each flo proxy route. Results are enriched with GeoLite2 (country, city, ASN) and stored for
365 days (raw samples for 90).

## Rules - read these first

1. **Read-only, except for creating runs.** You may `GET` anything and `POST runs`. Never
   `POST runs/{id}/cancel` unless the user asks, and never call any other API.
2. **Never create an `All` run without the user's explicit confirmation in this conversation** (design
   spec section 9). It measures every online player. Prefer `SampleCount` or `SamplePercent`, keep
   them small, and tell the user what you created.
3. **`Users` runs measure named players.** Only create them for players the user named. Do not
   enumerate players to measure.
4. **A single intermediate hop's loss is not evidence.** Read `references/interpretation-rubric.md`
   before concluding anything about loss or "the problem starts at hop N".
5. **Personal data.** Results contain IPs, coarse location and system info. Quote only what the user
   needs; do not paste whole measurement documents elsewhere. This repository is public: never write
   real BattleTags, IPs or tokens into files you commit.
6. **Do not loop on failures.** A 503 `query_timeout` means narrow the filter (see below), not retry
   the same call.

## Auth and base URL

```bash
# W3C_API: the website-backend base URL (ask the user which environment).
# W3C_ADMIN_TOKEN: an admin session JWT with the Proxies permission, supplied by the user.
: "${W3C_API:?set W3C_API}" "${W3C_ADMIN_TOKEN:?set W3C_ADMIN_TOKEN}"
curl -sS -H "Authorization: Bearer $W3C_ADMIN_TOKEN" "$W3C_API/api/network-diagnostics/targets" | jq .
```

- `401` with `"error":"AUTH_TOKEN_EXPIRED"`: the token expired. Stop and ask the user for a fresh one.
  Never try to obtain one yourself.
- `401` with any other body: the token is invalid or lacks the `Proxies` permission (a missing
  permission is answered 401, not 403). Tell the user.
- `502 {"error":"matchmaking_unavailable"}`: the flo node list could not be fetched. Retry later.
- `503 {"error":"query_timeout"}`: every read is limited to 15 s server-side. Narrow it (run id,
  target key, time range, method) and retry once; do not loop.
- `400 {"error":"invalid query parameter: <key>"}`: `<key>` has an unparsable value, or (`path`, `method`, `groupBy`, `format`, `cursor`) a value outside the allowed set.
- **Unknown parameter names are silently ignored** (`outcome=ok`, `node=12`, `isp=...` return 200 with the filter dropped, so the answer covers more data than you meant). Copy parameter names exactly from the filter table in `references/api.md`.

## Check for truncation

Aggregates and target-filtered measurement lookups consider at most the newest **200 000** matching
observations. When that limit was hit the response carries the header `X-NetDiag-Truncated: true`
and the numbers cover only the newest rows. Always look at response headers for aggregates
(`curl -D -`, see `references/api.md`); if truncated, narrow `from`/`to`, `runId`, `targetKey` or
`method` and ask again.

## Workflow

1. **Frame the question.** One user, one node or region, or a candidate IP? Pick the recipe in
   `references/recipes.md`.
2. **Look before you measure.** Check existing data first (`measurements?battleTag=...`,
   `aggregate?targetKey=...&from=...`). A run costs players' bandwidth; reuse recent data when it
   answers the question.
3. **Create the smallest run that answers it** (`references/api.md`, `POST runs`). Use only the
   methods you need; `windowSeconds` defaults to 60.
4. **Wait.** `Users` runs finish when the player's launcher runs them (minutes when online, up to
   14 days otherwise). Fleet runs have a 30-minute window and accept results until 15 minutes after
   it. Poll `GET runs/{id}` every 1-2 minutes and read `counters` (`assigned / delivered /
   submitted / expired`). No tight loops.
5. **Analyse** with `aggregate` (fleet) or `measurements/{id}` (one user), then `observations` for
   row-level detail (observation rows appear about 30 s after submit, see `references/api.md`). Use `format=csv` or `ndjson` when you want to compute with a script.
6. **Interpret** with `references/interpretation-rubric.md`. Always compare against a baseline or a
   second node before blaming one.
7. **Report** with the verdict template at the end of the rubric.

## References

| File | Content |
|---|---|
| `references/api.md` | Every endpoint, parameters, bodies, errors, with curl examples |
| `references/observation-schema.md` | Every observation field and how it is derived (final hop, stats) |
| `references/recipes.md` | Debug a user, debug a node, evaluate a new IP, compare direct vs proxied |
| `references/interpretation-rubric.md` | How to read MTR/ping data without the classic false flags, fleet sampling caveats |

## Investigate beyond this skill

The recipes are starting points. When the data does not fit, widen the time window, group by a
different key, compare against more baselines, or read the source in this repo
(`W3ChampionsStatisticService/NetworkDiagnostics/`). Flo-node-side diagnosis (host metrics, Grafana)
lives in the flo repo's `lag-investigation` skill; hand off to it when the evidence points at the
node itself rather than the path.
