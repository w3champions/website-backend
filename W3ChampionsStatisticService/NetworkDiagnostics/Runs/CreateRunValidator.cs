using System.Linq;
using System.Text.RegularExpressions;

namespace W3ChampionsStatisticService.NetworkDiagnostics.Runs;

/// <summary>Pure C6 POST runs validation. Normalize first, then Validate the normalized request.</summary>
public static class CreateRunValidator
{
    public static readonly Regex BattleTagPattern = new(@"^[^#\s]{2,12}#\d{3,6}$", RegexOptions.Compiled);
    private const int MaxReportedLength = 64;

    public static CreateRunRequest Normalize(CreateRunRequest r)
    {
        if (r == null) return null;
        var audience = r.Audience == null ? null : r.Audience with { BattleTags = r.Audience.BattleTags?.Distinct().ToList() };
        var targets = r.Targets == null ? null : r.Targets with
        {
            FloNodeIds = (r.Targets.FloNodeIds ?? []).Distinct().ToList(),
            CoreServers = (r.Targets.CoreServers ?? []).Distinct().ToList(),
            Baselines = (r.Targets.Baselines ?? []).Distinct().ToList(),
            CustomTargets = (r.Targets.CustomTargets ?? [])
                .Select(c => c with { TcpPort = c.TcpPort ?? NetDiagConstants.DefaultCustomTcpPort })
                .Distinct()
                .ToList(),
        };
        return r with
        {
            Audience = audience,
            Targets = targets,
            Methods = r.Methods?.Distinct().ToList(),
            WindowSeconds = r.WindowSeconds ?? NetDiagConstants.DefaultWindowSeconds,
        };
    }

    public static string Validate(CreateRunRequest r)
    {
        if (r == null) return "body is required";
        if (r.Audience == null) return "audience is required";
        if (r.Targets == null) return "targets is required";
        if (r.Methods == null || r.Methods.Count == 0) return "methods must not be empty";
        foreach (var m in r.Methods)
        {
            if (!NetDiagConstants.AllMethods.Contains(m ?? "")) return $"unknown method: {Clip(m)}";
        }
        if (r.WindowSeconds is not { } window || window < NetDiagConstants.MinWindowSeconds || window > NetDiagConstants.MaxWindowSeconds)
            return "windowSeconds must be in 30-300";
        return ValidateAudience(r.Audience) ?? ValidateTargets(r.Targets);
    }

    private static string ValidateAudience(RunAudience a)
    {
        switch (a.Kind)
        {
            case AudienceKind.Users:
                var tags = a.BattleTags ?? [];
                if (tags.Count is 0 or > NetDiagConstants.MaxBattleTagsPerRun) return "battleTags must hold 1-500 tags";
                foreach (var t in tags)
                {
                    if (t == null || !BattleTagPattern.IsMatch(t)) return $"invalid battleTag: {Clip(t)}";
                }
                return null;
            case AudienceKind.SampleCount:
                return a.Count is >= 1 ? null : "count must be >= 1";
            case AudienceKind.SamplePercent:
                return a.Percent is > 0 and <= 100 ? null : "percent must be in (0, 100]";
            case AudienceKind.All:
                return null;
            default:
                return "unknown audience kind";
        }
    }

    private static string ValidateTargets(RunTargets t)
    {
        var any = t.FloNodeIds.Count > 0 || t.AllFloNodes || t.CoreServers.Count > 0 || t.Baselines.Count > 0
            || t.CustomTargets.Count > 0 || t.IncludeDefaultGateway;
        if (!any) return "at least one target is required";
        if (t.CustomTargets.Count > NetDiagConstants.MaxCustomTargets) return "at most 50 custom targets";
        foreach (var c in t.CustomTargets)
        {
            if (!NetDiagIp.IsStrictIpv4(c.Ip)) return $"invalid custom target ip: {Clip(c.Ip)}";
            if (c.TcpPort is < 1 or > 65535) return "custom target port must be in 1-65535";
        }
        return null;
    }

    private static string Clip(string value) => value == null ? "null" : value.Length <= MaxReportedLength ? value : value[..MaxReportedLength];
}
