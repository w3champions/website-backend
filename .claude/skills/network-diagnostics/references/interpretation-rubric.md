# Interpretation rubric

Adapted from flo's `lag-investigation` skill (interpretation-rubric Pattern I and false-flags Part 1).
Check every finding against section 1 before you report it.

## 1. False flags

- **Loss at an intermediate hop that does not persist to the destination is ICMP rate-limiting, not loss.**
  Routers deprioritise the TTL-exceeded replies they generate. If every later hop and the destination
  show ~0%, nothing was lost. Real loss propagates downstream. Never build an argument, in either
  direction, on intermediate-hop loss.
- **In TCP traceroute/MTR only the final hop is a real TCP measurement.** Intermediate hop times are the
  routers' ICMP replies. Never say "the TCP delay starts at hop 3".
- **On Windows the hop list is the ICMP path.** The TCP path may differ (ECMP, policy routing). Compare
  final-hop stats across methods, not hop lists.
- **Proxied paths:** ICMP and TCP methods stop at the first relay. Only `FloUdpPing` is end to end.
  A great proxied `IcmpMtr` says nothing about the relay-to-node leg.
- **One measurement is an anecdote.** Fleet conclusions need `count` >= 20 per group. Always state the
  count next to the number. `count` counts observations (several per measurement when a measurement has several paths/probes), so also keep the number of distinct measurements in mind.
- **High `p99Ms` with a clean `p50Ms` and `p90Ms`** is usually a single stall (Wi-Fi scan, CPU spike).
  Check `system.cpu.usagePct`, `wifi` and whether `restarts` > 0 before calling it a path problem.
- **`unsupported` outcomes (e.g. reason `os_requires_admin`) are a client capability gap, not unreachability.**
  `reason` strings are open-ended: read them, do not match a fixed list. There is no `outcome` query
  filter: such rows have no latency stats and drop out of the percentiles, but they still count in
  `count`. Fetch ndjson and filter with `jq 'select(.outcome=="ok")'` when you need the success-only view.
  `shareUnreachable` counts only the `unreachable` outcome (not `timeout`, `error` or `unsupported`).
- **VPN-suspected clients** measure the VPN's path. Use `vpn=false` for ISP-level conclusions.
- **GeoLite2 city/ASN is approximate**, especially for mobile and CGNAT ranges. Use it to group, not to
  identify a person's location.
- **Units:** all `*Ms` are milliseconds; adapter `rxBps`/`txBps` are bits per second; `lossPct` is a
  percentage, but the aggregate `shareLossOver1Pct`/`shareUnreachable` are fractions 0-1.
- **Truncated answers.** If the response had `X-NetDiag-Truncated: true`, only the newest 200 000
  matching observations were used. Narrow the filter before drawing conclusions; do not report the
  numbers as the full picture.

## 2. Fleet sampling caveats

- With `SamplePercent`, a user who reconnects is re-rolled on every reconnect until assigned, so
  players with flapping connections are over-sampled. A fleet sample is not a uniform sample of
  players, and it is biased toward unstable connections: do not read the share of bad results as the
  share of bad players.
- `SampleCount` can slightly exceed the requested count because of late joiners.
- Fleet assignments accept results until 15 minutes after the window end; analyse after that for a complete run.
- Only players with a running launcher at that time can be measured: players who cannot connect at all are invisible.

## 3. Always compare against a control

Before blaming a node, compare the same clients, same method, same time against:
- a **baseline** (`baseline:cloudflare`, `google`, the regional one): is the client's own connection bad?
- a **second node** in the same region: is it specific to this node or provider?
Bad to everything: last mile. Bad only to one node: that node's path or provider. Bad only from one ASN: that ISP's route (peering).

## 4. TCP vs ICMP - the middlebox signature

A stateful TCP-aware appliance (DPI, DDoS scrubbing, SYN-proxy) on the provider uplink holds TCP
segments. Players on TCP see hundreds of ms while ICMP and UDP look perfect.
- Signature: final-hop `TcpMtr`/`TcpPing` p50/p90 far above `IcmpMtr` to the same target (healthy is about 1x;
  flo's russia-west incident measured 6.8x at the destination hop), while baselines show TCP close to ICMP.
- **Judge p50 and p90.** The stalls are intermittent, so a median-only check passes a broken node.
- If ICMP is *also* bad, it is ordinary congestion or routing, not a middlebox.
- Hand-off: this is a provider issue; continue with flo's `lag-investigation` skill (transit
  assessment) and the controller, not node tuning.

## 5. Where is the problem? (decision tree)

1. `gateway` lossy or jittery: **LAN/Wi-Fi** (check `wifi.rssiDbm`, adapter kind).
2. Baselines bad too: **ISP / last mile** of that player or ASN.
3. Only target X bad, a nearby node fine: **path to X** (group by `asn`: one ISP is peering; all ISPs is X's provider).
4. Direct bad, a proxied `FloUdpPing` route good: **route around it**; recommend that relay.
5. TCP much worse than ICMP only on X: **middlebox** (section 4).

## 6. Verdict template

```markdown
## Finding
**Where:** <LAN | ISP <asnOrg> (AS<asn>) | path to <target> | provider middlebox | inconclusive>

Evidence (method, count, numbers):
- <target/method/group>: p50 <x> ms, p90 <y> ms, loss <z>% (n=<count>)
- Control <baseline/node>: p50 ..., loss ... (n=...)
- <system signal if relevant>

**Not the cause:**
- <signal that looked bad> - <why ruled out (rubric section 1 rule)>

**Recommendation:**
- <route via relay R | contact ISP | disable node | collect more data: run ...>

**Data:** run <runId>, <from>-<to>, <n> measurements. Truncated: <yes/no>.
```
