using System;
using W3ChampionsStatisticService.Tools.MapKeyBackfill;
using W3ChampionsStatisticService.Tools.MapMetadataBackfill;

return args.Length > 0 && args[0].Equals("backfill-map-keys", StringComparison.OrdinalIgnoreCase)
    ? await MapKeyBackfillCommand.RunAsync(args)
    : await MapMetadataBackfillCommand.RunAsync(args);
