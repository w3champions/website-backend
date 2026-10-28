using System;
using System.Threading.Tasks;
using W3ChampionsStatisticService.Tools.MapMetadataBackfill;

namespace W3ChampionsStatisticService.Tools.MapKeyBackfill;

public static class MapKeyBackfillCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        MapKeyBackfillOptions options;
        try
        {
            options = MapKeyBackfillOptions.Parse(args);
        }
        catch (HelpRequestedException)
        {
            Console.WriteLine(MapKeyBackfillOptions.Usage);
            return 0;
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            Console.Error.WriteLine();
            Console.Error.WriteLine(MapKeyBackfillOptions.Usage);
            return 2;
        }

        MapKeyBackfillTarget target;
        try
        {
            target = MapKeyBackfillSafetyGuard.DescribeTarget(options.ConnectionString);
        }
        catch (MongoDB.Driver.MongoConfigurationException ex)
        {
            Console.Error.WriteLine($"Invalid MongoDB connection string: {ex.Message}");
            return 2;
        }

        Console.WriteLine(options.Apply ? "Running map key backfill in APPLY mode." : "Running map key backfill in dry-run mode.");
        Console.WriteLine($"Host(s): {string.Join(", ", target.Servers)}{(target.IsLocal ? " (local)" : " (REMOTE)")}");
        Console.WriteLine($"Database: {options.DatabaseName}, collections: {string.Join(", ", options.Collections)}");

        try
        {
            MapKeyBackfillSafetyGuard.EnsureApplyAllowed(options, target);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 2;
        }

        try
        {
            await RunBackfillAsync(options);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static async Task RunBackfillAsync(MapKeyBackfillOptions options)
    {
        await Task.CompletedTask;
        throw new NotImplementedException(
            $"backfill-map-keys is not implemented beyond option parsing and the safety guard yet. Nothing was read from or written to {options.DatabaseName}.");
    }
}
