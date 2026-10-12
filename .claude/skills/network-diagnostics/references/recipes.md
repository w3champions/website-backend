# Recipes

All use `nd` from `api.md`. Replace placeholders; never guess BattleTags. Values with `#` or `:`
go through `-G --data-urlencode`. Check `X-NetDiag-Truncated` on every aggregate (`-D headers.txt`).

## Debug a user ("Name#1234 lags on US East 4")

1. Existing data: `nd measurements -G --data-urlencode 'battleTag=Name#1234' --data-urlencode pageSize=10 | jq '.items[] | {measurementId, submittedAt, clientGeo, primaryAdapterType, vpnSuspected}'`.
2. If nothing is recent (say, older than a week; a rule of thumb), create a `Users` run for that tag (only if the user named the player). Targets: the node(s) the user plays on (`floNodeIds`), two nearby nodes for comparison, `baselines: ["cloudflare","google"]` plus the regional baseline (`alidns`/`dnspod` for CN, `yandex` for RU), and `includeDefaultGateway: true`. Methods: `IcmpMtr`, `TcpPing`, `FloUdpPing` (add `TcpMtr` only for macOS players; it is `unsupported` on Windows; `FloUdpPing` applies to flo-node targets only). A 400 `unknown_battletags` means the spelling is wrong or the player is unknown.
3. Poll `nd runs/$RUN | jq '.run.counters, [.assignments[] | {status, submittedAt}]'` until `submitted` is 1 (or the assignment is `Expired`). `measurements/{id}` is readable at once; `observations` and CSV exports of the new rows follow about 30 s later (settle margin).
4. `nd "measurements/$MID" | jq` and read in this order:
   - `system`: `wifi.rssiDbm` (rule of thumb: below about -70 is weak), `vpn.suspected`, `antivirus.thirdParty`, `cpu.usagePct`, adapter `linkSpeedMbps`.
   - the `gateway` target: loss or jitter here is the player's LAN or Wi-Fi. Stop and report that.
   - baselines vs flo node, same method: bad on everything means the player's ISP or last mile; bad only to the node means the path to that node.
   - `direct` vs `proxied` `FloUdpPing` per node: if a proxied route beats direct, recommend that route.
   - `floSnapshot`: what the launcher's own flo ping table showed at the time (cross-check).
5. Report with the verdict template (rubric).

## Debug a node ("players say node 12 is laggy")

1. Existing fleet data first: `nd aggregate -G --data-urlencode targetKey=flo:12 --data-urlencode method=IcmpMtr --data-urlencode groupBy=country --data-urlencode "from=$(date -u -v-7d +%FT%TZ)" -D headers.txt` (GNU date: `date -u -d '-7 days' +%FT%TZ`).
2. If thin, create a small `SampleCount` or `SamplePercent` fleet run and tell the user what you created. Only `All` needs explicit confirmation (spec section 9). Targets: `floNodeIds: [12]` plus 1-2 nodes in the same region, `baselines` for the region. Methods: `IcmpMtr`, `TcpPing`, `FloUdpPing` (`TcpMtr` only works for macOS players).
3. Wait for the 30-minute window (`run.windowEnd`) plus up to 15 minutes for late submits.
4. Aggregate, narrowing step by step (always `runId` or `targetKey` + `method`):
   - `groupBy=asn` (`targetKey=flo:12&method=IcmpMtr`): one or two ISPs?
   - `groupBy=country`: regional?
   - `groupBy=path,method` with a `targetKey`: does a proxied route or UDP avoid it? Does TCP differ from ICMP?
   - Repeat for the comparison node and a baseline. Present the three side by side.
5. For the worst groups, pull rows: `observations?targetKey=flo:12&asn=<asn>&format=ndjson` (rows from the last 30 s are not included yet), look at `lastHopIp`/`lastHopAsn`, and open two or three `measurements/{id}` for hop-level detail.

## Evaluate a new IP ("would 203.0.113.10 serve South America?")

1. Fleet run, `SampleCount` 100-200, `customTargets: [{ip: "203.0.113.10", tcpPort: 443}]`. Compare against the nearest existing flo nodes and the global baselines. Methods: `IcmpMtr`, `TcpPing` (`TcpMtr` only works for macOS players).
2. `aggregate?runId=$RUN&targetKey=custom:203.0.113.10:443&method=TcpPing&groupBy=country`, then the same for the existing nodes. Compare `p50MsMedian`, `p50MsP90` and `shareLossOver1Pct` per country.
3. Check TCP vs ICMP for the custom IP (middlebox signature, rubric section 4).
4. Recommend only where the candidate beats the existing nodes for a meaningful share of the region's players. Say how many observations (`count`) each row rests on.

## Compare direct vs proxied for a set of users

`aggregate?runId=$RUN&method=FloUdpPing&groupBy=targetKey,path`. Proxied `FloUdpPing` is end to end, so this is the one fair direct-vs-proxied comparison. Only flo nodes have a proxied path.
