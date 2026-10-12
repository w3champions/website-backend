# Observation fields

One observation = one probe (method) for one path of one target in one measurement. The CSV header
and the JSON/ndjson property names are the same list, in exactly this order.

| Field | Meaning |
|---|---|
| `measurementId`, `runId`, `battleTag`, `submittedAt` | identity; `submittedAt` is server time (UTC, ISO-8601) |
| `clientIp` | the launcher's reported public IPv4, else the connection's IPv4 address; null for an IPv6-only peer |
| `country`, `city`, `lat`, `lon`, `asn`, `asnOrg` | GeoLite2 of `clientIp` at submit time (null when unknown) |
| `os` | OS family as reported by the launcher (e.g. `windows`, `macos`) |
| `primaryAdapterType` | kind of the adapter holding the default route (`ethernet`, `wifi`, ...) |
| `vpnSuspected` | launcher heuristics (e.g. a tunnel adapter holds the default route) |
| `targetKey`, `targetLabel`, `targetIp` | what was measured; `targetIp` = the ICMP endpoint for Icmp*, the TCP host for Tcp*, the UDP host for FloUdpPing |
| `path` | exactly `direct` or `proxied` |
| `routeLabel` | flo route label for proxied paths (e.g. `US East 4 -> US East 3`) |
| `method` | `IcmpTraceroute`, `IcmpMtr`, `TcpTraceroute`, `TcpMtr`, `TcpPing`, `FloUdpPing` |
| `outcome`, `reason` | `outcome` is `ok`, `unsupported`, `unreachable`, `timeout` or `error`; `reason` is an open-ended string explaining a non-ok outcome (e.g. `os_requires_admin`); do not assume a fixed set |
| `sent`, `received`, `lossPct` | probe counts; `lossPct` null when `sent` = 0 |
| `minMs`, `avgMs`, `maxMs`, `stddevMs`, `p50Ms`, `p90Ms`, `p95Ms`, `p99Ms`, `jitterMs` | RTT stats in ms, null when nothing succeeded. `jitterMs` = mean absolute difference of consecutive successful samples |
| `hopCount` | number of hops in the trace (0 for ping methods) |
| `lastHopIp`, `lastHopAsn` | the **final hop** (below); null when it never answered |

The exact column list is: `measurementId, runId, battleTag, submittedAt, clientIp, country, city, lat, lon, asn, asnOrg, os,
primaryAdapterType, vpnSuspected, targetKey, targetLabel, targetIp, path, routeLabel, method, outcome, reason, sent, received,
lossPct, minMs, avgMs, maxMs, stddevMs, p50Ms, p90Ms, p95Ms, p99Ms, jitterMs, hopCount, lastHopIp, lastHopAsn`.

CSV details: RFC 4180 quoting, CRLF line ends, ISO-8601 UTC timestamps, empty cell = null. A text cell
starting with `=`, `+`, `-`, `@`, tab or CR is prefixed with `'` (spreadsheet formula protection), so a
`reason` or city may appear with a leading quote; numeric cells are never altered.

**Where the stats come from:**
- `TcpPing`, `FloUdpPing`: the probe's own series.
- `IcmpMtr`, `TcpMtr`, `IcmpTraceroute`, `TcpTraceroute`: the **final hop**. That is the target (the lowest-TTL hop whose IP equals `targetIp` and that answered); else the last hop that answered; else the deepest hop (all lost, so `lossPct` 100). Traceroute rounds are few samples, so their percentiles are coarse. In `GET measurements/{id}` the traceroute-method probe itself has `stats: null`; the per-hop stats are under `hops`.
- `outcome` other than `ok`: usually `sent` = 0 and every stat null. Count them via `shareUnreachable` (only the `unreachable` outcome) or by filtering ndjson locally; there is no `outcome` query filter.

Paths and what they measure:
- `direct`: from the player's machine straight to the target.
- `proxied` (`routeLabel` set; flo nodes only): ICMP and TCP methods stop at the first relay. Only
  `FloUdpPing` on a proxied path is end to end through the relay chain.
