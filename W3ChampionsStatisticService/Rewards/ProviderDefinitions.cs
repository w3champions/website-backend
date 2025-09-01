using System;
using System.Collections.Generic;
using System.Linq;

namespace W3ChampionsStatisticService.Rewards;

public class ProviderInfo
{
    public string Id { get; init; }
    public string Name { get; init; }
    public bool IsEnabled { get; init; }
    public string Description { get; init; }
}

public static class ProviderDefinitions
{
    private static readonly Dictionary<string, ProviderInfo> _providers = new()
    {
        ["patreon"] = new ProviderInfo
        {
            Id = "patreon",
            Name = "Patreon",
            Description = "Patreon subscription rewards",
            IsEnabled = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PATREON_WEBHOOK_SECRET"))
        },
        ["kofi"] = new ProviderInfo
        {
            Id = "kofi",
            Name = "Ko-Fi",
            Description = "Ko-Fi donation rewards",
            IsEnabled = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("KOFI_VERIFICATION_TOKEN"))
        }
    };

    public static IReadOnlyDictionary<string, ProviderInfo> Providers => _providers;

    public static ProviderInfo GetProvider(string providerId)
    {
        return _providers.TryGetValue(providerId.ToLowerInvariant(), out var provider) ? provider : null;
    }

    public static IEnumerable<ProviderInfo> GetEnabledProviders()
    {
        return _providers.Values.Where(p => p.IsEnabled);
    }

    public static IEnumerable<ProviderInfo> GetAllProviders()
    {
        return _providers.Values;
    }

    public static bool IsProviderSupported(string providerId)
    {
        return _providers.ContainsKey(providerId.ToLowerInvariant());
    }

    public static bool IsProviderEnabled(string providerId)
    {
        var provider = GetProvider(providerId);
        return provider?.IsEnabled ?? false;
    }
}
