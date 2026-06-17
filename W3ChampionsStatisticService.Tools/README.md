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
