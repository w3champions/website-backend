using System;
using System.IO;
using System.Reflection;

namespace WC3ChampionsStatisticService.UnitTests.Rewards.Fixtures;

public static class PatreonPayloadLoader
{
    private static readonly string PayloadsDir = ResolvePayloadsDir();

    private static string ResolvePayloadsDir()
    {
        var assembly = typeof(PatreonPayloadLoader).GetTypeInfo().Assembly;
        var assemblyDir = Path.GetDirectoryName(assembly.Location);
        var payloadsPath = Path.Combine(assemblyDir, "Rewards", "Fixtures", "Payloads");

        if (!Directory.Exists(payloadsPath))
            throw new InvalidOperationException($"Could not locate payloads directory at {payloadsPath}");

        return payloadsPath;
    }

    public static string LoadRaw(string name) => File.ReadAllText(Path.Combine(PayloadsDir, name));

    public static string MembersCreateJson() => LoadRaw("members-create.json");
    public static string MembersUpdateJson() => LoadRaw("members-update.json");
    public static string MembersDeleteJson() => LoadRaw("members-delete.json");
}
