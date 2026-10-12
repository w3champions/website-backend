using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using W3ChampionsStatisticService.NetworkDiagnostics.Submission;

namespace W3ChampionsStatisticService.NetworkDiagnostics.Results;

/// <summary>C6 MeasurementSummaryDto. <c>Restarts</c> is long like the stored value (an unsigned 32-bit counter on the wire, P10).</summary>
public record MeasurementSummaryDto(
    string MeasurementId, string RunId, string BattleTag, DateTime SubmittedAt, string ClientIp, ClientGeo ClientGeo,
    string Os, string PrimaryAdapterType, bool? VpnSuspected, int TargetCount, long Restarts);

/// <summary>One [offsetMs, rttMs | null] pair per sample. Serialized only when set: an absent series has no key (A4).</summary>
public record HopDetailDto(int Ttl, string Ip, StatsDto Stats)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<double?[]> Samples { get; init; }
}

public record ProbeDetailDto(string Method, string Outcome, string Reason, StatsDto Stats, List<HopDetailDto> Hops)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<double?[]> Samples { get; init; }
}

public record PathDetailDto(string Path, string RouteLabel, NetDiagEndpoints Endpoints, List<ProbeDetailDto> Probes);

public record TargetDetailDto(string TargetKey, string Label, List<PathDetailDto> Paths);

/// <summary>C6 MeasurementDetailDto: summary fields + C5 context + ipInfo + targets (+ samples when raw=true).</summary>
public record MeasurementDetailDto(
    string MeasurementId, string RunId, string BattleTag, DateTime SubmittedAt, string ClientIp, ClientGeo ClientGeo,
    string Os, string PrimaryAdapterType, bool? VpnSuspected, int TargetCount, long Restarts,
    NetDiagOs OsInfo, string LauncherVersion, DateTime? AttemptStartedAt, DateTime? WindowStartedAt, DateTime? WindowEndedAt,
    NetDiagPacing Pacing, NetDiagSystem System, List<NetDiagFloSnapshotRow> FloSnapshot, string DefaultGateway,
    Dictionary<string, ClientGeo> IpInfo, List<TargetDetailDto> Targets)
{
    /// <summary>Only with raw=true, and only when the 90-day raw series is gone: true, and no samples key anywhere (A4).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? RawExpired { get; init; }
}

public static class MeasurementDetailMapper
{
    public static MeasurementSummaryDto Summary(NetworkDiagnosticMeasurement m) => new(
        m.Id, m.RunId, m.BattleTag, m.SubmittedAt, m.ClientIp, m.ClientGeo, m.Os, m.PrimaryAdapterType, m.VpnSuspected, m.TargetCount, m.Restarts);

    /// <param name="raw">The measurement's raw series; null when not asked for or expired.</param>
    /// <param name="includeRaw">raw=true was asked: add the samples, or say that they expired.</param>
    public static MeasurementDetailDto Detail(NetworkDiagnosticMeasurement m, NetworkDiagnosticRawSeries raw, IReadOnlyList<NetworkDiagnosticIpInfo> ipInfo, bool includeRaw)
    {
        var series = includeRaw && raw != null ? DecodeSeries(raw) : null;
        List<double?[]> ProbeSeries(int target, int path, int probe) =>
            series != null && series.TryGetValue((target, path, probe, null), out var samples) ? samples : null;
        // While the raw data exists every hop carries the key (A4), also a hop that was submitted without samples.
        List<double?[]> HopSeries(int target, int path, int probe, int hop) =>
            series == null ? null : series.TryGetValue((target, path, probe, hop), out var samples) ? samples : [];

        var targets = (m.Targets ?? []).Select((target, ti) => new TargetDetailDto(target.TargetKey, target.Label,
            (target.Paths ?? []).Select((path, pi) => new PathDetailDto(path.Path, path.RouteLabel, path.Endpoints,
                (path.Probes ?? []).Select((probe, ri) => new ProbeDetailDto(probe.Method, probe.Outcome, probe.Reason, NoStatsWhenNothingSent(probe.Stats),
                    (probe.Hops ?? []).Select((hop, hi) => new HopDetailDto(hop.Ttl, hop.Ip, NoStatsWhenNothingSent(hop.Stats)) { Samples = HopSeries(ti, pi, ri, hi) }).ToList())
                {
                    Samples = ProbeSeries(ti, pi, ri),
                }).ToList())).ToList())).ToList();

        var ipMap = new Dictionary<string, ClientGeo>();
        foreach (var i in ipInfo)
        {
            ipMap.TryAdd(i.Ip, new ClientGeo { Asn = i.Asn, AsnOrg = i.AsnOrg, Country = i.Country, City = i.City, Lat = i.Lat, Lon = i.Lon, GeoDbBuild = i.GeoDbBuild });
        }

        return new MeasurementDetailDto(
            m.Id, m.RunId, m.BattleTag, m.SubmittedAt, m.ClientIp, m.ClientGeo, m.Os, m.PrimaryAdapterType, m.VpnSuspected, m.TargetCount, m.Restarts,
            m.OsInfo, m.LauncherVersion, m.AttemptStartedAt, m.WindowStartedAt, m.WindowEndedAt, m.Pacing, m.System, m.FloSnapshot ?? [], m.DefaultGateway,
            ipMap, targets)
        {
            RawExpired = includeRaw && raw == null ? true : null,
        };
    }

    /// <summary>Nothing sent means no statistics; also for documents stored before ResultBuilder stopped writing zero-filled objects.</summary>
    private static StatsDto NoStatsWhenNothingSent(StatsDto stats) => stats is { Sent: 0 } ? null : stats;

    /// <summary>The IPs whose IpInfo rows the detail view needs: client + every hop.</summary>
    public static List<string> IpsOf(NetworkDiagnosticMeasurement m) =>
        new[] { m.ClientIp }
            .Concat((m.Targets ?? []).SelectMany(t => t.Paths ?? []).SelectMany(p => p.Probes ?? []).SelectMany(p => p.Hops ?? []).Select(h => h.Ip))
            .Where(ip => !string.IsNullOrEmpty(ip)).Distinct().ToList();

    /// <summary>Keyed like ResultBuilder wrote them: (target, path, probe, hop) index, hop null = the probe's own series.</summary>
    private static Dictionary<(int Target, int Path, int Probe, int? Hop), List<double?[]>> DecodeSeries(NetworkDiagnosticRawSeries raw)
    {
        var series = new Dictionary<(int, int, int, int?), List<double?[]>>();
        foreach (var entry in raw.Series ?? [])
        {
            series[(entry.TargetIndex, entry.PathIndex, entry.ProbeIndex, entry.HopIndex)] = ToMs(entry.Data);
        }
        return series;
    }

    private static List<double?[]> ToMs(byte[] data) =>
        RawSeriesCodec.Decode(data).Select(s => new double?[] { s.OffsetMs, s.RttUs is { } us ? us / 1000.0 : null }).ToList();
}
