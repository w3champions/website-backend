using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MongoDB.Bson;
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

    // A localhost URI can still reach another machine through an SSH tunnel or a port forward. The host name the server
    // reports (hostInfo) tells; null when it can't be read.
    public static async Task<string> ServerHostNameAsync(string connectionString)
    {
        try
        {
            var settings = MongoClientSettings.FromConnectionString(connectionString.Replace("'", string.Empty));
            settings.ServerSelectionTimeout = TimeSpan.FromSeconds(10);
            settings.ConnectTimeout = TimeSpan.FromSeconds(10);
            var result = await new MongoClient(settings).GetDatabase("admin").RunCommandAsync<BsonDocument>(new BsonDocument("hostInfo", 1));
            return result.GetValue("system", BsonNull.Value) is BsonDocument system && system.GetValue("hostname", BsonNull.Value) is BsonString hostname
                ? hostname.Value
                : null;
        }
        catch (Exception ex) when (ex is MongoException or TimeoutException)
        {
            return null;
        }
    }

    // Compares host names without port and domain, ignoring case.
    public static bool IsThisMachine(string serverHostName, string machineName)
    {
        if (string.IsNullOrWhiteSpace(serverHostName) || string.IsNullOrWhiteSpace(machineName))
        {
            return false;
        }

        static string Short(string name) => name.Trim().Split(':')[0].Split('.')[0];
        return string.Equals(Short(serverHostName), Short(machineName), StringComparison.OrdinalIgnoreCase);
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
