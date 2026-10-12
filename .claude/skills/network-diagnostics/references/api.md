# Network diagnostics admin API

Base: `$W3C_API/api/network-diagnostics`. Every endpoint needs `Authorization: Bearer $W3C_ADMIN_TOKEN`
(admin session JWT with the `Proxies` permission; otherwise 401). JSON is camelCase. Errors are
`{"error": "..."}`. Tokens (measurement secrets) never appear in any admin response.

```bash
nd() { curl -sS -H "Authorization: Bearer $W3C_ADMIN_TOKEN" "$W3C_API/api/network-diagnostics/$1" "${@:2}"; }
```

Pass query values with `-G --data-urlencode` so `#` in BattleTags and `:` in target keys are encoded:

```bash
nd measurements -G --data-urlencode 'battleTag=Name#1234' --data-urlencode 'pageSize=10' | jq .
```

## Conventions

- `page` is **zero-based**. `pageSize` is 1-100 (default 25). Violations: 400 `page must be >= 0`,
  `pageSize must be in 1-100`, `page is too large`.
- `from` is inclusive, `to` is exclusive, ISO-8601 UTC (e.g. `2026-10-01T00:00:00Z`).
- An unparsable query value answers 400 `{"error":"invalid query parameter: <key>"}`.
- **Parameter names outside the filter table are silently ignored, with no error.** A misspelled or invented filter (`outcome=ok`, `node=12`, `isp=...`) returns 200 with the filter dropped, so the response covers more data than intended. Copy parameter names exactly from the table.
- Every read is limited to 15 s server-side: a slow query answers 503 `{"error":"query_timeout"}`.
  Narrow the filter (run id, target key, time range) and retry; do not loop.
- Aggregate and target-filtered measurement lookups consider at most the newest 200 000 matching
  observations. When that limit was hit the response has the header `X-NetDiag-Truncated: true`.
  Headers are not shown by default: add `-D -` (dump headers to stdout) or `-i`.
- Unknown id: 404 `{"error":"not_found"}`.

## Targets

`GET targets` returns `{ floNodes: [{id, name, location, countryId, isDisabled}], coreServers: [{key, label, ip, tcpPort}], baselines: [{key, label, region, ip, tcpPort}] }`.
502 `matchmaking_unavailable` when the flo node list cannot be fetched.

```bash
nd targets | jq '{nodes: [.floNodes[] | {id, name, isDisabled}], baselines: [.baselines[].key], core: [.coreServers[].key]}'
```

Target keys used everywhere (case-sensitive): `flo:<nodeId>`, `core:<key>`, `baseline:<key>`,
`custom:<ip>:<port>`, `gateway` (the player's default gateway).

## Runs

`POST runs` (JSON body, at most 256 KB; content type is not checked):

```bash
nd runs -X POST -H 'Content-Type: application/json' -d '{
  "audience": { "kind": "Users", "battleTags": ["Name#1234"] },
  "targets": { "floNodeIds": [12], "allFloNodes": false, "coreServers": ["core-key"], "baselines": ["cloudflare"],
               "customTargets": [{ "ip": "203.0.113.10", "tcpPort": 443 }], "includeDefaultGateway": true },
  "methods": ["IcmpMtr", "TcpPing", "FloUdpPing"],
  "allowDuringGame": false,
  "windowSeconds": 60
}' | jq .
```

- `audience.kind` is exactly one of (names case-sensitive):
  - `Users` with `battleTags` (1-500, exact case, form `Name#1234`; every tag must be known to the backend);
  - `SampleCount` with `count` >= 1;
  - `SamplePercent` with `percent` in (0, 100];
  - `All` (**only with the user's explicit confirmation**).
- `methods`: non-empty subset of `IcmpTraceroute`, `IcmpMtr`, `TcpTraceroute`, `TcpMtr`, `TcpPing`, `FloUdpPing`.
  Support matrix (design spec sections 2 and 6.1; the backend accepts any combination, the launcher answers `unsupported` where it cannot):
  - `TcpTraceroute` / `TcpMtr` are impossible on Windows without admin (outcome `unsupported`, reason `os_requires_admin`); they work on macOS. **Most players are on Windows: use `TcpPing` for the TCP-versus-ICMP comparison, and treat a missing `TcpMtr` row as a client limitation, not a finding.**
  - `FloUdpPing` works for flo nodes only (`unsupported` for core, baseline, custom and gateway targets).
  - The `gateway` target supports ICMP only (`IcmpMtr`, `IcmpTraceroute`).
  - `proxied` paths exist only for flo nodes with an active route override.
- `targets`: at least one of `floNodeIds`, `allFloNodes`, `coreServers`, `baselines`, `customTargets`, `includeDefaultGateway`. `core`/`baseline` values are the `key`s from `GET targets`. Custom targets need a dotted-quad IPv4 and an optional `tcpPort` (1-65535, default 443). At most 50 custom targets and 200 targets in total.
- `windowSeconds`: 30-300 (default 60). A measurement takes about `windowSeconds + 15` s (`expectedDurationSeconds`).
- `allowDuringGame` (default false) lets the launcher measure while a game is running.
- `Users` runs: assignments are deliverable for 14 days; online users get the request at once, others when they connect. Fleet runs (`SampleCount`/`SamplePercent`/`All`) draw from players online at creation, spread slots over a 30-minute window, add players who log in during it, and accept results until 15 minutes after the window end.
- 201 returns a `RunDto`: `{id, createdAt, createdBy, audience, targets, methods, allowDuringGame, windowSeconds, windowStart, windowEnd, cancelledAt, counters: {assigned, delivered, submitted, expired}, expectedDurationSeconds}`.
- 502 `matchmaking_unavailable` when `floNodeIds` or `allFloNodes` is used and the flo node list cannot be fetched (retry later).
- 400 errors (all `{"error": ...}`):
  - `invalid_json`, `payload_too_large`, `body is required`;
  - `audience is required`, `targets is required`, `methods must not be empty`, `unknown method: <m>`;
  - `windowSeconds must be in 30-300`;
  - `battleTags must hold 1-500 tags`, `invalid battleTag: <tag>`, `count must be >= 1`, `percent must be in (0, 100]`, `unknown audience kind`;
  - `at least one target is required`, `invalid core server key`, `invalid baseline key`, `at most 50 custom targets`, `invalid custom target`, `invalid custom target ip: <ip>`, `custom target port must be in 1-65535`;
  - `too many targets (max 200)`;
  - `unknown_targets` with `targetKeys` (e.g. `["core:x"]`);
  - `unknown_flo_nodes` with `floNodeIds`;
  - `unknown_battletags` with `battleTags` (the ones the backend does not know; fix the spelling or ask the user).

`GET runs?createdBy=&from=&to=&page=0&pageSize=25` returns `{items: RunDto[], total}` (newest first). `createdBy` is an admin BattleTag; `from`/`to` filter on run creation time.

`GET runs/{id}` returns `{run, assignments: [{measurementId, battleTag, status, deliverAfter, deliverBy, deliveredAt, submittedAt, clientGeo}]}`; `status` is `Pending`, `Submitted` or `Expired`; `clientGeo` is set for submitted ones.

`POST runs/{id}/cancel` returns the `RunDto` (409 `already_cancelled`). **Only when the user asks.**

## Shared filters (measurements, observations, aggregate)

| Param | Meaning |
|---|---|
| `battleTag` | exact player (case-sensitive) |
| `runId`, `measurementId` | repeatable (`runId=a&runId=b`) |
| `targetKey`, `nodeId`, `targetIp` | target-level: what was measured (`nodeId=12` equals `targetKey=flo:12`) |
| `path` | exactly `direct` or `proxied` |
| `method` | exactly one of the six method names above |
| `country`, `city`, `asn`, `os`, `adapterType`, `vpn` | client-level: who measured (`asn` numeric, `vpn=true/false`) |
| `from`, `to` | on `submittedAt`; `from` inclusive, `to` exclusive |

Only the names in this table are filters; any other name is ignored without an error. `battleTag`, `country`,
`city`, `os`, `adapterType`, `targetKey`, `targetIp` match **exactly and case-sensitively**: copy the value from
`clientGeo`, `ipInfo` or measurement detail output (e.g. the `country` string a measurement reports) instead of guessing it.

There is **no `outcome` filter**. Anything other than `direct`/`proxied` for `path`, or an unknown
method name, answers 400 `invalid query parameter: path` / `method`. To look at failed probes, fetch
observations (`format=ndjson`) and filter locally with `jq 'select(.outcome != "ok")'`.

## Measurements

`GET measurements?<filters>&page=0&pageSize=25` returns `{items: MeasurementSummaryDto[], total}`, newest first, each item
`{measurementId, runId, battleTag, submittedAt, clientIp, clientGeo, os, primaryAdapterType, vpnSuspected, targetCount, restarts}`.
With any target-level filter (`targetKey`, `nodeId`, `targetIp`, `path`, `method`) the match is
computed from observations (newest 200 000; check `X-NetDiag-Truncated`). A filtered list reports
`total` capped at 100 000; an unfiltered list reports an estimate.

`GET measurements/{id}?raw=false|true` returns the summary fields plus:
- `osInfo {family, name, version}`, `launcherVersion`, `attemptStartedAt`, `windowStartedAt`, `windowEndedAt`;
- `pacing`, `system` (`cpu`, `memory`, `uptimeS`, `clockSkewMs`, `launcherProxyConfigured`, `adapters`, `wifi`, `vpn`, `antivirus`), `floSnapshot` (the launcher's flo ping table at measurement time), `defaultGateway`;
- `ipInfo` (IP -> `{asn, asnOrg, country, city, lat, lon, geoDbBuild}` for the client and every hop) and `targets[].paths[].probes[]`.

`clientGeo` is `{country, city, lat, lon, asn, asnOrg, geoDbBuild}`. Each target is `{targetKey, label, paths[]}`; each path `{path, routeLabel, endpoints, probes[]}`; each probe `{method, outcome, reason, stats, hops[{ttl, ip, stats}]}`.
`system.adapters[]` has `rxBps`/`txBps`: **bits** per second.

With `raw=true`, probes and hops carry `samples: [[offsetMs, rttMs|null]]` (null = lost). Raw series are kept
**90 days**: afterwards the `samples` keys are omitted and `rawExpired: true` is set. Without `raw=true`
there is never a `samples` key.

`StatsDto`: `{sent, received, lossPct, lastMs, minMs, avgMs, maxMs, stddevMs, p50Ms, p90Ms, p95Ms, p99Ms, jitterMs}`.
Percentiles are nearest-rank over successful samples. `lossPct` is null when nothing was sent. All
`*Ms` are null when nothing succeeded. Traceroute-method probes have `stats: null` (see hops).

## Observations (one row per target x path x probe)

`GET observations?<filters>&cursor=&limit=1000&format=json|ndjson|csv`
- `limit` 1-5000 (default 1000), else 400 `limit must be in 1-5000`. `format` default `json`; anything else 400 `invalid query parameter: format`.
- `json`: `{items, nextCursor}`; `nextCursor` is null when the limit did not end the page.
- `ndjson` / `csv`: rows streamed in the body (csv has a header row); the next cursor is in the `X-Next-Cursor` response header, absent when the limit did not end the page.
- `cursor` is the opaque value from the previous page; a malformed one is 400 `invalid query parameter: cursor`.
- Rows come in `_id` (insertion) order, not sorted by time.
- **Settle margin:** only rows older than 30 s are returned, so the rows of a just-submitted measurement appear about 30 s later. `measurements`, `measurements/{id}` and `aggregate` are not delayed.
- **Cursors:** a next cursor is announced only when `limit` ended the page. No cursor (`nextCursor` null / no `X-Next-Cursor` header) means the export is complete as of now minus 30 s. Do **not** poll in a loop for more: to pick up newer rows later, call again with the last cursor you received (it stays valid), or start again without one.

```bash
nd observations -G --data-urlencode "runId=$RUN" --data-urlencode format=csv --data-urlencode limit=5000 -D headers.txt -o page1.csv
grep -i '^x-next-cursor' headers.txt        # absent = complete as of now minus 30 s
nd observations -G --data-urlencode "runId=$RUN" --data-urlencode format=csv --data-urlencode limit=5000 \
   --data-urlencode "cursor=$(grep -i '^x-next-cursor' headers.txt | cut -d' ' -f2 | tr -d '\r')" -o page2.csv
```

Columns: see `observation-schema.md`.

## Aggregate

`GET aggregate?groupBy=asn,path&<filters>`. `groupBy` is 1-3 distinct keys from
`country, city, asn, os, adapterType, vpn, targetKey, path, method, runId`; anything else 400
`invalid query parameter: groupBy`.

```bash
nd aggregate -G --data-urlencode groupBy=asn --data-urlencode targetKey=flo:12 --data-urlencode method=IcmpMtr \
   --data-urlencode "from=2026-10-01T00:00:00Z" -D headers.txt -o agg.json
grep -i '^x-netdiag-truncated' headers.txt   # present = only the newest 200 000 rows were used; narrow the filter
jq '.rows[] | {key, count, p50MsMedian, shareLossOver1Pct}' agg.json
```

It returns `{groupBy, rows: [{key, count, p50MsMedian, p50MsP90, lossPctMedian, shareLossOver1Pct, shareUnreachable, asnOrg, lat, lon}]}`:
- `key` is an object with one entry per `groupBy` key, e.g. `{"asn": 3320, "path": "direct"}`;
- `count` is the number of observations;
- `p50MsMedian` / `p50MsP90`: median and 90th percentile of the observations' `p50Ms`; `lossPctMedian`: median of `lossPct`;
- `shareLossOver1Pct` and `shareUnreachable` are **fractions 0-1** (share of observations with `lossPct` > 1, share with outcome `unreachable`), not percentages;
- `lat`/`lon`: centroid (average across the group's measurements); `asnOrg` is set only when grouping by `asn`;
- rows are capped at 1000.

**Always filter aggregate by `targetKey` (or `nodeId`) and `method`.** Mixing targets or methods in
one group averages unrelated paths.
