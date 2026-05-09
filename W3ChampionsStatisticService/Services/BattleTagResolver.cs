using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using W3C.Domain.Tracing;

namespace W3ChampionsStatisticService.Services;

[Trace]
public class BattleTagResolver(IdentityServiceClient identityClient, IMemoryCache cache) : IBattleTagResolver
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);
    private const string CacheKeyPrefix = "battletag-canonical:";

    private readonly IdentityServiceClient _identityClient = identityClient;
    private readonly IMemoryCache _cache = cache;

    public async Task<string> ResolveCanonical(string input)
    {
        if (input == null) return null;
        var cacheKey = CacheKeyPrefix + input.ToLowerInvariant();
        if (_cache.TryGetValue<string>(cacheKey, out var cached))
            return cached;

        var canonical = await _identityClient.ResolveCanonicalBattleTag(input);
        _cache.Set(cacheKey, canonical, CacheTtl);
        return canonical;
    }

    public async Task<IDictionary<string, string>> ResolveCanonicalBatch(IEnumerable<string> inputs)
    {
        var distinctInputs = inputs?.Where(i => i != null).Distinct().ToList() ?? new List<string>();
        var tasks = distinctInputs.Select(async input => new { input, canonical = await ResolveCanonical(input) });
        var resolved = await Task.WhenAll(tasks);
        return resolved.ToDictionary(r => r.input, r => r.canonical);
    }
}
