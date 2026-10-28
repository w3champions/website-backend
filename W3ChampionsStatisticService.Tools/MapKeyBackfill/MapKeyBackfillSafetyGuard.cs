using System;
using System.Collections.Generic;
using System.Linq;
using MongoDB.Driver;

namespace W3ChampionsStatisticService.Tools.MapKeyBackfill;

public static class MapKeyBackfillSafetyGuard
{
    private static readonly string[] LocalHosts = { "localhost", "127.0.0.1" };

    public static MapKeyBackfillTarget DescribeTarget(string connectionString)
    {
        var url = new MongoUrl(connectionString.Replace("'", string.Empty));
        var hosts = url.Servers.Select(s => s.Host).ToList();

        // An SRV record resolves to hosts we can't see here, so treat it as remote.
        var isLocal = url.Scheme != MongoDB.Driver.Core.Configuration.ConnectionStringScheme.MongoDBPlusSrv
            && hosts.Count > 0
            && hosts.All(h => LocalHosts.Contains(h, StringComparer.OrdinalIgnoreCase));

        return new MapKeyBackfillTarget(url.Servers.Select(s => s.ToString()).ToList(), isLocal);
    }

    public static void EnsureApplyAllowed(MapKeyBackfillOptions options, MapKeyBackfillTarget target)
    {
        if (!options.Apply || target.IsLocal)
        {
            return;
        }

        if (!options.AllowRemote)
        {
            throw new ArgumentException(
                $"Refusing --apply on remote host(s) {string.Join(", ", target.Servers)}. " +
                "Pass --allow-remote and --confirm-database <name> (production runbook only).");
        }

        if (!string.Equals(options.ConfirmDatabase, options.DatabaseName, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Refusing --apply: --confirm-database '{options.ConfirmDatabase}' does not match the target database '{options.DatabaseName}'.");
        }
    }
}

public sealed record MapKeyBackfillTarget(IReadOnlyList<string> Servers, bool IsLocal);
