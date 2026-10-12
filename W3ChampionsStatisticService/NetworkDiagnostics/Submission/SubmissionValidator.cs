using System.Collections.Generic;
using System.Linq;

namespace W3ChampionsStatisticService.NetworkDiagnostics.Submission;

/// <summary>
/// Pure C5 cap and shape checks, in the style of LagReportController.ValidateSubmission. A 400 is terminal for
/// the launcher, so only the C5 caps and what the result builder needs to run safely are rejected (A12).
/// </summary>
public static class SubmissionValidator
{
    public const int MaxBodyBytes = 8 * 1024 * 1024;
    private const int MaxTargets = 200;
    private const int MaxPathsPerTarget = 2;
    private const int MaxProbesPerPath = 6;
    private const int MaxHopsPerProbe = 30;
    private const int MaxSamplesPerSeries = 600;
    private const int MaxAdapters = 64;
    private const int MaxSnapshotRows = 300;
    private const int MaxStringLength = 500;
    // The launcher's hop ttl is a u8 (P11).
    private const int MinTtl = 0;
    private const int MaxTtl = 255;

    public static string ValidateSubmission(NetDiagSubmission dto, string routeMeasurementId)
    {
        if (dto == null) return "Missing body";
        if (dto.MeasurementId != routeMeasurementId) return "measurement_id does not match the URL";
        var targets = dto.Targets ?? [];
        if (targets.Count > MaxTargets) return "Too many targets";
        if ((dto.FloSnapshot?.Count ?? 0) > MaxSnapshotRows) return "Too many flo_snapshot rows";
        if ((dto.System?.Adapters?.Count ?? 0) > MaxAdapters) return "Too many adapters";

        foreach (var target in targets)
        {
            var error = ValidateTarget(target);
            if (error != null) return error;
        }

        // String lists (dns, evidence, third_party) are deliberately not capped by entry count: the result
        // builder truncates them to 64 (P11). Each entry is still covered by the 500-char check.
        if (Strings(dto).Any(s => s?.Length > MaxStringLength)) return "String too long";
        return null;
    }

    public static List<ProbeSample> ToSamples(List<long?[]> pairs) =>
        (pairs ?? []).Select(p => new ProbeSample((uint)p[0]!.Value, p[1] is { } rtt ? (uint)rtt : null)).ToList();

    private static string ValidateTarget(NetDiagTargetDto target)
    {
        if (target == null) return "Null target";
        var paths = target.Paths ?? [];
        if (paths.Count > MaxPathsPerTarget) return "Too many paths";
        foreach (var path in paths)
        {
            if (path == null) return "Null path";
            var probes = path.Probes ?? [];
            if (probes.Count > MaxProbesPerPath) return "Too many probes";
            foreach (var probe in probes)
            {
                var error = ValidateProbe(probe);
                if (error != null) return error;
            }
        }
        return null;
    }

    private static string ValidateProbe(NetDiagProbeDto probe)
    {
        if (probe == null) return "Null probe";
        if ((probe.Hops?.Count ?? 0) > MaxHopsPerProbe) return "Too many hops";
        var error = ValidateSamples(probe.Samples);
        if (error != null) return error;
        foreach (var hop in probe.Hops ?? [])
        {
            if (hop == null) return "Null hop";
            if (hop.Ttl is < MinTtl or > MaxTtl) return "hop ttl out of range";
            error = ValidateSamples(hop.Samples);
            if (error != null) return error;
        }
        return null;
    }

    private static string ValidateSamples(List<long?[]> samples)
    {
        if (samples == null) return null;
        if (samples.Count > MaxSamplesPerSeries) return "Too many samples";
        foreach (var pair in samples)
        {
            if (pair == null || pair.Length != 2 || pair[0] is not { } offset) return "Malformed sample";
            if (offset < 0 || offset > uint.MaxValue) return "Sample offset out of range";
            if (pair[1] is { } rtt && (rtt < 0 || rtt >= RawSeriesCodec.LostMarker)) return "Sample rtt out of range";
        }
        return null;
    }

    private static IEnumerable<string> Strings(NetDiagSubmission d)
    {
        yield return d.MeasurementId;
        yield return d.LauncherVersion;
        yield return d.PublicIpv4;
        yield return d.DefaultGateway;
        yield return d.Os?.Family;
        yield return d.Os?.Name;
        yield return d.Os?.Version;
        foreach (var s in SystemStrings(d.System)) yield return s;
        foreach (var row in d.FloSnapshot ?? [])
        {
            if (row == null) continue;
            yield return row.NodeName;
            yield return row.RouteLabel;
            yield return row.Transport;
            yield return row.Health;
            yield return row.ControllerStatus;
        }
        foreach (var s in TargetStrings(d.Targets)) yield return s;
    }

    private static IEnumerable<string> SystemStrings(NetDiagSystem system)
    {
        if (system == null) yield break;
        yield return system.Cpu?.Model;
        foreach (var a in system.Adapters ?? [])
        {
            if (a == null) continue;
            yield return a.Name;
            yield return a.Kind;
            yield return a.Gateway;
            yield return a.UpSince;
            yield return a.UpSinceReason;
            foreach (var dns in a.Dns ?? []) yield return dns;
        }
        yield return system.Wifi?.Band;
        yield return system.Wifi?.Reason;
        foreach (var e in system.Vpn?.Evidence ?? []) yield return e;
        foreach (var av in system.Antivirus?.ThirdParty ?? []) yield return av;
        yield return system.Antivirus?.Reason;
    }

    // Only called after the structural walk proved there are no null targets, paths, probes or hops.
    private static IEnumerable<string> TargetStrings(List<NetDiagTargetDto> targets)
    {
        foreach (var t in targets ?? [])
        {
            yield return t.TargetKey;
            yield return t.Label;
            foreach (var p in t.Paths ?? [])
            {
                yield return p.Path;
                yield return p.RouteLabel;
                yield return p.Endpoints?.Icmp;
                yield return p.Endpoints?.Tcp;
                yield return p.Endpoints?.Udp;
                foreach (var probe in p.Probes ?? [])
                {
                    yield return probe.Method;
                    yield return probe.Outcome;
                    yield return probe.Reason;
                    foreach (var hop in probe.Hops ?? []) yield return hop.Ip;
                }
            }
        }
    }
}
