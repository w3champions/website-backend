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
| `vpnSuspected` | set by the launcher when the OS routes 1.1.1.1 or a measured target through a VPN/tunnel interface (the `system.vpn.evidence` list holds the route facts and any VPN client/service found, but those no longer decide it; results from launchers before this change used the older heuristic: any VPN client/service, or a tunnel holding the default route); `true`/`false` as reported, **null = unknown** (the result carried no VPN information; never read null as "no VPN"). Rows stored before this rule may hold `false` for unknown |
| `targetKey`, `targetLabel`, `targetIp` | what was measured; `targetIp` = the ICMP endpoint for Icmp*, the TCP host for Tcp*, the UDP host for FloUdpPing |
| `path` | exactly `direct` or `proxied` |
| `routeLabel` | flo route label for proxied paths (e.g. `US East 4 -> US East 3`) |
| `method` | `IcmpTraceroute`, `IcmpMtr`, `TcpTraceroute`, `TcpMtr`, `TcpPing`, `FloUdpPing` |
| `outcome`, `reason` | `outcome` is `ok`, `unsupported`, `unreachable`, `timeout` or `error`; `reason` is an open-ended string explaining a non-ok outcome (e.g. `os_requires_admin`); do not assume a fixed set |
| `sent`, `received`, `lossPct` | probe counts (0 when nothing was sent); `lossPct` null when `sent` = 0 |
| `minMs`, `avgMs`, `maxMs`, `stddevMs`, `p50Ms`, `p90Ms`, `p95Ms`, `p99Ms`, `jitterMs` | RTT stats in ms, null when nothing succeeded or nothing was sent. `jitterMs` = mean absolute difference of consecutive successful samples |
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
- `IcmpMtr`, `TcpMtr`, `IcmpTraceroute`, `TcpTraceroute`: the **final hop**. That is the target (the lowest-TTL hop whose IP equals `targetIp` and that answered); else a hop whose IP equals `targetIp` that was probed but never answered (`sent` N, `received` 0, `lossPct` 100, `lastHopIp` null); else the last hop that answered; else the deepest hop (all lost, so `lossPct` 100). The target hop is matched by address, so a hop with `ip: null` is never taken for it, even if it has answered samples. Traceroute rounds are few samples, so their percentiles are coarse. In `GET measurements/{id}` the traceroute-method probe itself has `stats: null`; the per-hop stats are under `hops`.
- `outcome` other than `ok`: usually `sent` = 0 and every derived stat (`lossPct`, `*Ms`) null. In the aggregate such rows count in `count` but contribute no loss or RTT value and are left out of the `shareLossOver1Pct` denominator. Count them via `shareUnreachable` (only the `unreachable` outcome) or by filtering ndjson locally; there is no `outcome` query filter.

Paths and what they measure:
- `direct`: from the player's machine straight to the target.
- `proxied` (`routeLabel` set; flo nodes only): ICMP and TCP methods stop at the first relay. Only
  `FloUdpPing` on a proxied path is end to end through the relay chain.
