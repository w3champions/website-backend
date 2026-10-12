using System;
using System.Collections.Generic;
using System.Linq;
using MongoDB.Bson;
using W3ChampionsStatisticService.NetworkDiagnostics.Results;
using W3ChampionsStatisticService.NetworkDiagnostics.Runs;

namespace W3ChampionsStatisticService.NetworkDiagnostics.Submission;

public record BuiltResult(
    NetworkDiagnosticMeasurement Measurement,
    NetworkDiagnosticRawSeries RawSeries,
    List<NetworkDiagnosticObservation> Observations);

/// <summary>Pure mapping of a validated submission to the documents written at submit (spec §5.5 steps 3-5).</summary>
public static class ResultBuilder
{
    /// <summary>Longest dns / evidence / third_party list kept in the stored result; longer ones are cut, not rejected (P11).</summary>
    private const int MaxStringListEntries = 64;

    private static readonly TimeSpan IpInfoRetention = NetDiagConstants.Retention + NetDiagConstants.ExplicitDeliveryWindow;

    /// <summary>RemoteIpAddress is unreliable behind the proxy chain, so the launcher's reported IPv4 wins.</summary>
    public static string ChooseClientIp(NetDiagSubmission dto, string remoteIp) =>
        NetDiagIp.IsStrictIpv4(dto.PublicIpv4) ? dto.PublicIpv4 : remoteIp;

    public static List<string> LookupIps(NetDiagSubmission dto, string clientIp) =>
        new[] { clientIp }
            .Concat((dto.Targets ?? []).SelectMany(t => t.Paths ?? []).SelectMany(p => p.Probes ?? [])
                .SelectMany(p => p.Hops ?? []).Select(h => h.Ip))
            .Where(NetDiagIp.IsStrictIpv4)
            .Distinct()
            .ToList();

    public static List<NetworkDiagnosticIpInfo> IpInfoRows(string runId, IReadOnlyDictionary<string, ClientGeo> geo, DateTime now) =>
        geo.Where(kv => kv.Value != null && NetDiagIp.IsStrictIpv4(kv.Key))
            .Select(kv => new NetworkDiagnosticIpInfo
            {
                Id = ObjectId.GenerateNewId(),
                RunId = runId,
                Ip = kv.Key,
                Asn = kv.Value.Asn,
                AsnOrg = kv.Value.AsnOrg,
                Country = kv.Value.Country,
                City = kv.Value.City,
                Lat = kv.Value.Lat,
                Lon = kv.Value.Lon,
                GeoDbBuild = kv.Value.GeoDbBuild,
                FirstSeenAt = now,
                ExpiresAt = now + IpInfoRetention,
            })
            .ToList();

    public static BuiltResult Build(
        NetDiagSubmission dto,
        NetworkDiagnosticAssignment assignment,
        DateTime submittedAt,
        string clientIp,
        IReadOnlyDictionary<string, ClientGeo> geo)
    {
        ClientGeo GeoOf(string ip) => ip != null && geo.TryGetValue(ip, out var g) ? g : null;
        var clientGeo = GeoOf(clientIp);
        var expiresAt = submittedAt + NetDiagConstants.Retention;
        var targets = dto.Targets ?? [];
        var system = SanitizeSystem(dto.System);

        var measurement = new NetworkDiagnosticMeasurement
        {
            Id = assignment.Id,
            RunId = assignment.RunId,
            BattleTag = assignment.BattleTag,
            SubmittedAt = submittedAt,
            ExpiresAt = expiresAt,
            ClientIp = clientIp,
            ClientGeo = clientGeo,
            Os = dto.Os?.Family,
            OsInfo = dto.Os,
            PrimaryAdapterType = system?.Adapters?.FirstOrDefault(a => a.HasDefaultRoute == true)?.Kind,
            VpnSuspected = system?.Vpn?.Suspected ?? false,
            LauncherVersion = dto.LauncherVersion,
            AttemptStartedAt = dto.AttemptStartedAt,
            WindowStartedAt = dto.WindowStartedAt,
            WindowEndedAt = dto.WindowEndedAt,
            Restarts = dto.Restarts,
            PublicIpv4 = dto.PublicIpv4,
            DefaultGateway = dto.DefaultGateway,
            Pacing = dto.Pacing,
            System = system,
            FloSnapshot = (dto.FloSnapshot ?? []).Where(r => r != null).ToList(),
            TargetCount = targets.Count,
        };

        var measuredTargets = new List<MeasuredTarget>();
        var series = new List<RawSeriesEntry>();
        var observations = new List<NetworkDiagnosticObservation>();

        for (var ti = 0; ti < targets.Count; ti++)
        {
            var target = targets[ti];
            var paths = target.Paths ?? [];
            var measuredPaths = new List<MeasuredPath>();
            for (var pi = 0; pi < paths.Count; pi++)
            {
                var path = paths[pi];
                var probes = path.Probes ?? [];
                var measuredProbes = new List<MeasuredProbe>();
                for (var ri = 0; ri < probes.Count; ri++)
                {
                    var probe = probes[ri];
                    var analysis = Analyze(probe, path.Endpoints);
                    measuredProbes.Add(analysis.Measured);
                    series.AddRange(RawEntries(probe, ti, pi, ri));
                    observations.Add(Observation(measurement, target, path, probe, analysis, GeoOf(analysis.FinalHopIp)?.Asn));
                }
                measuredPaths.Add(new MeasuredPath { Path = path.Path, RouteLabel = path.RouteLabel, Endpoints = path.Endpoints, Probes = measuredProbes });
            }
            measuredTargets.Add(new MeasuredTarget { TargetKey = target.TargetKey, Label = target.Label, Paths = measuredPaths });
        }

        var raw = new NetworkDiagnosticRawSeries
        {
            Id = assignment.Id,
            SubmittedAt = submittedAt,
            ExpiresAt = submittedAt + NetDiagConstants.RawRetention,
            Series = series,
        };
        return new BuiltResult(measurement with { Targets = measuredTargets }, raw, observations);
    }

    /// <summary>Drops null adapters and cuts the string lists to 64 entries (P11). Returns a copy; the submission is untouched.</summary>
    private static NetDiagSystem SanitizeSystem(NetDiagSystem system)
    {
        if (system == null) return null;
        return system with
        {
            Adapters = system.Adapters?.Where(a => a != null).Select(a => a with { Dns = Truncate(a.Dns) }).ToList(),
            Vpn = system.Vpn == null ? null : system.Vpn with { Evidence = Truncate(system.Vpn.Evidence) },
            Antivirus = system.Antivirus == null ? null : system.Antivirus with { ThirdParty = Truncate(system.Antivirus.ThirdParty) },
        };
    }

    private static List<string> Truncate(List<string> list) => list?.Take(MaxStringListEntries).ToList();

    private sealed record ProbeAnalysis(MeasuredProbe Measured, StatsDto ObservationStats, string TargetIp, string FinalHopIp, int HopCount);

    private static ProbeAnalysis Analyze(NetDiagProbeDto probe, NetDiagEndpoints endpoints)
    {
        var hops = probe.Hops ?? [];
        var targetIp = TargetIpFor(probe.Method, endpoints);
        var hopStats = hops.Select(h => SeriesStats.Compute(SubmissionValidator.ToSamples(h.Samples))).ToList();
        var measuredHops = hops.Select((h, i) => new MeasuredHop { Ttl = h.Ttl, Ip = h.Ip, Stats = hopStats[i] }).ToList();
        var hopBased = hops.Count > 0 || IsHopMethod(probe.Method);

        StatsDto observationStats;
        string finalHopIp = null;
        if (hopBased)
        {
            var final = FinalHopIndex(hops, hopStats, targetIp);
            observationStats = final is { } f ? hopStats[f] : new StatsDto();
            finalHopIp = final is { } fi && hopStats[fi].Received > 0 ? hops[fi].Ip : null;
        }
        else
        {
            observationStats = SeriesStats.Compute(SubmissionValidator.ToSamples(probe.Samples));
        }

        var probeStats = NetDiagConstants.TracerouteMethods.Contains(probe.Method ?? "") ? null : observationStats;
        var measured = new MeasuredProbe { Method = probe.Method, Outcome = probe.Outcome, Reason = probe.Reason, Stats = probeStats, Hops = measuredHops };
        return new ProbeAnalysis(measured, observationStats, targetIp, finalHopIp, hops.Count);
    }

    /// <summary>The target hop (lowest TTL that is the target and answered), else the last answering hop, else the deepest hop.</summary>
    private static int? FinalHopIndex(List<NetDiagHopDto> hops, List<StatsDto> stats, string targetIp)
    {
        if (hops.Count == 0) return null;
        var byTtl = Enumerable.Range(0, hops.Count).OrderBy(i => hops[i].Ttl).ToList();
        var target = byTtl.Where(i => stats[i].Received > 0 && targetIp != null && hops[i].Ip == targetIp).Cast<int?>().FirstOrDefault();
        return target ?? byTtl.Where(i => stats[i].Received > 0).Cast<int?>().LastOrDefault() ?? byTtl[^1];
    }

    private static bool IsHopMethod(string method) =>
        method is NetDiagConstants.IcmpTraceroute or NetDiagConstants.IcmpMtr or NetDiagConstants.TcpTraceroute or NetDiagConstants.TcpMtr;

    private static string TargetIpFor(string method, NetDiagEndpoints endpoints)
    {
        if (endpoints == null) return null;
        if (NetDiagConstants.IcmpMethods.Contains(method ?? "")) return endpoints.Icmp;
        if (NetDiagConstants.TcpMethods.Contains(method ?? "")) return NetDiagIp.HostOf(endpoints.Tcp);
        if (method == NetDiagConstants.FloUdpPing) return NetDiagIp.HostOf(endpoints.Udp);
        return endpoints.Icmp ?? NetDiagIp.HostOf(endpoints.Tcp) ?? NetDiagIp.HostOf(endpoints.Udp);
    }

    private static IEnumerable<RawSeriesEntry> RawEntries(NetDiagProbeDto probe, int ti, int pi, int ri)
    {
        if (probe.Samples is { Count: > 0 })
        {
            yield return new RawSeriesEntry { TargetIndex = ti, PathIndex = pi, ProbeIndex = ri, Data = RawSeriesCodec.Encode(SubmissionValidator.ToSamples(probe.Samples)) };
        }
        var hops = probe.Hops ?? [];
        for (var hi = 0; hi < hops.Count; hi++)
        {
            if (hops[hi].Samples is not { Count: > 0 }) continue;
            yield return new RawSeriesEntry { TargetIndex = ti, PathIndex = pi, ProbeIndex = ri, HopIndex = hi, Data = RawSeriesCodec.Encode(SubmissionValidator.ToSamples(hops[hi].Samples)) };
        }
    }

    private static NetworkDiagnosticObservation Observation(
        NetworkDiagnosticMeasurement m, NetDiagTargetDto target, NetDiagPathDto path, NetDiagProbeDto probe, ProbeAnalysis a, long? lastHopAsn)
    {
        var s = a.ObservationStats;
        return new NetworkDiagnosticObservation
        {
            Id = ObjectId.GenerateNewId(),
            MeasurementId = m.Id,
            RunId = m.RunId,
            BattleTag = m.BattleTag,
            SubmittedAt = m.SubmittedAt,
            ClientIp = m.ClientIp,
            Country = m.ClientGeo?.Country,
            City = m.ClientGeo?.City,
            Lat = m.ClientGeo?.Lat,
            Lon = m.ClientGeo?.Lon,
            Asn = m.ClientGeo?.Asn,
            AsnOrg = m.ClientGeo?.AsnOrg,
            Os = m.Os,
            PrimaryAdapterType = m.PrimaryAdapterType,
            VpnSuspected = m.VpnSuspected,
            TargetKey = target.TargetKey,
            NodeId = target.TargetKey?.StartsWith("flo:", StringComparison.Ordinal) == true && int.TryParse(target.TargetKey[4..], out var node) ? node : null,
            TargetLabel = target.Label,
            TargetIp = a.TargetIp,
            Path = path.Path,
            RouteLabel = path.RouteLabel,
            Method = probe.Method,
            Outcome = probe.Outcome,
            Reason = probe.Reason,
            Sent = s.Sent,
            Received = s.Received,
            LossPct = s.LossPct,
            MinMs = s.MinMs,
            AvgMs = s.AvgMs,
            MaxMs = s.MaxMs,
            StddevMs = s.StddevMs,
            P50Ms = s.P50Ms,
            P90Ms = s.P90Ms,
            P95Ms = s.P95Ms,
            P99Ms = s.P99Ms,
            JitterMs = s.JitterMs,
            HopCount = a.HopCount,
            LastHopIp = a.FinalHopIp,
            LastHopAsn = lastHopAsn,
            ExpiresAt = m.ExpiresAt,
        };
    }
}
