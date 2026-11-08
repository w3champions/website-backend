# W3ChampionsStatisticService.Tools

Small operational tools for the statistic service.

## Map metadata backfill

Backfills missing `MapName` and `MapId` fields on historical `Matchup` documents. The command is dry-run by default and writes a CSV report before any production run should be applied.

Dry run:

```bash
MONGO_CONNECTION_STRING="mongodb://..." dotnet run \
  --project W3ChampionsStatisticService.Tools \
  -- backfill-map-metadata \
  --target-min-season 1 \
  --target-max-season 10 \
  --source-min-season 11 \
  --preview-limit 50 \
  --report map-metadata-backfill-report.csv
```

To review or apply one season at a time, use `--season`:

```bash
MONGO_CONNECTION_STRING="mongodb://..." dotnet run \
  --project W3ChampionsStatisticService.Tools \
  -- backfill-map-metadata \
  --season 1 \
  --source-min-season 11 \
  --preview-limit 50 \
  --report map-metadata-backfill-season-1.csv
```

Apply only the safe, resolved rows:

```bash
MONGO_CONNECTION_STRING="mongodb://..." dotnet run \
  --project W3ChampionsStatisticService.Tools \
  -- backfill-map-metadata \
  --target-min-season 1 \
  --target-max-season 10 \
  --source-min-season 11 \
  --report map-metadata-backfill-report.csv \
  --apply
```

The resolver uses newer `Matchup` rows as the source catalog, plus a small built-in mapping for old-only map keys. Ambiguous or missing matches are reported and skipped.
When one map id has had multiple display names, the resolver prefers the name from season 24 by default. Use `--preferred-name-season <season>` to choose a different source season for display names.

Dry runs print a short human-readable preview and write the full CSV report. Use `--preview-limit 0` to hide the console preview or increase the limit when reviewing a small dataset.

Optional manual mappings can be supplied when the dry-run report shows gaps:

```json
{
  "LegacyMapKey": { "mapName": "Display Name", "mapId": 123 }
}
```

Then run with:

```bash
--manual-map manual-map-metadata.json
```

## Map key backfill

Rewrites the legacy mangled map keys stored in the read models into the map file name, plus `MapId` and `MapName`. Collections: `Matchup`, `MapsPerSeason`, `PlayerHeroStats`, `PlayerRaceOnMapVersusRaceRatio`, `OverallRaceAndWinStat` (`OnGoingMatchup` is transient and skipped). The command is dry-run by default and always writes a CSV report.

Dry run:

```bash
dotnet run --project W3ChampionsStatisticService.Tools -- backfill-map-keys \
  --connection-string mongodb://localhost:27017 \
  --update-service-map-data MapData.json \
  --mm-maps Maps.json \
  --manual-map manual-map.json \
  --write-event-catalog event-catalog.jsonl \
  --report map-key-backfill-report.csv
```

Apply, reusing the event catalog of the reviewed dry run:

```bash
dotnet run --project W3ChampionsStatisticService.Tools -- backfill-map-keys \
  --connection-string mongodb://localhost:27017 \
  --update-service-map-data MapData.json --mm-maps Maps.json --manual-map manual-map.json \
  --event-catalog event-catalog.jsonl \
  --report map-key-backfill-apply.csv \
  --apply
```

`--apply` on any host other than `localhost`/`127.0.0.1` also needs `--allow-remote --confirm-database <database>`.

Inputs:
- **Event catalog:** one aggregation of `MatchFinishedEvent` on `{match.map, match.mapId, match.mapName, match.season, match.gameMode}`, one month at a time with `--aggregation-pause-ms` (default 2000) between months. `--write-event-catalog` saves it as JSON lines with a header (database, hosts, creation time); `--event-catalog` reads it back instead of aggregating.
- `--update-service-map-data`: `mongoexport` of UpdateService `MapData` (file name → `MapId`).
- `--mm-maps`: `mongoexport` of matchmaking-service `Maps` (`MapId` → name).
- `--manual-map`: entries for keys that no event explains (see `--help` for the shape).

How a key is resolved:
1. A key that is already a known map file name (events or `MapData`) is in the new shape and is skipped.
2. Events of the same season (and mode, where the collection records one) give the file. Two candidates are the same map when they share a `MapId` or a `MapName`. The most played file wins.
3. A manual entry that applies to the key, season and mode gives the id and name; otherwise the latest event's, filled from `MapData`/`Maps` where missing.
4. Without a same-season event, the entry keeps the legacy key as `Map`, with the manual id and name if an entry applies. In a season that has events, the older `MapMetadataResolver` is tried last.

`OverallRaceAndWinStat` has no season and is 1v1 only, so it resolves against 1v1 events of every season. `PlayerHeroStats` and `PlayerRaceOnMapVersusRaceRatio` record no mode.

Writes:
- `Matchup`: `Map` from the document's own `MatchFinishedEvent` (by `_id`, then `MatchId` ↔ `match._id`). `MapId`/`MapName` are only filled where missing.
- Stats documents are replaced whole. Entries that end up with the same `Map` are merged (counts and wins/losses summed); `"Overall"` stays first.
- `MapsPerSeason`: each season's all-modes bucket, and the all-seasons document (`-1`), are rebuilt as the sum of the migrated mode buckets and season documents. A bucket whose stored total differs from that sum is left as it is (or migrated in place) and reported.
- Re-running is safe: entries already in the new shape don't change.

CSV columns: `collection, legacy_key, season, game_mode, status, chosen_file, map_id, map_name, candidate_files, source, docs_touched, entries, entries_merged, notes`. Statuses: `resolved`, `resolved-by-catalog`, `resolved-manual`, `ambiguous-merged` (several maps share the key; the most played was chosen), `legacy-key`, `unresolved` (left unchanged), `rebuilt` / `not-rebuilt`.

Other options: `--collections`, `--database`, `--batch-size` (default 1000), `--batch-pause-ms`, `--start-after <_id>` (one paged collection only). Run `--help` for the full list.
