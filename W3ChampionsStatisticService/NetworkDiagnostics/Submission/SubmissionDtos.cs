using System;
using System.Collections.Generic;
using MongoDB.Bson.Serialization.Attributes;

namespace W3ChampionsStatisticService.NetworkDiagnostics.Submission;

// C5 result submission. Read with SubmissionJson.Options (snake_case); leaf records are also stored in
// NetworkDiagnosticMeasurements and written camelCase by the admin API. Numbers the launcher measures
// are double? so an int-vs-float difference never fails the whole submission; the launcher's unsigned
// 32/64-bit integers (restarts, mtu, channel, pacing, snapshot samples) are long so values above int.MaxValue
// still bind (P10).

[BsonIgnoreExtraElements]
public record NetDiagSubmission
{
    public string MeasurementId { get; init; }
    public string LauncherVersion { get; init; }
    public NetDiagOs Os { get; init; }
    public DateTime? AttemptStartedAt { get; init; }
    public DateTime? WindowStartedAt { get; init; }
    public DateTime? WindowEndedAt { get; init; }
    public long Restarts { get; init; }
    public string PublicIpv4 { get; init; }
    public string DefaultGateway { get; init; }
    public NetDiagPacing Pacing { get; init; }
    public NetDiagSystem System { get; init; }
    public List<NetDiagFloSnapshotRow> FloSnapshot { get; init; }
    public List<NetDiagTargetDto> Targets { get; init; }
}

[BsonIgnoreExtraElements]
public record NetDiagOs
{
    public string Family { get; init; }
    public string Name { get; init; }
    public string Version { get; init; }
}

[BsonIgnoreExtraElements]
public record NetDiagPacing
{
    public long? MtrRoundMs { get; init; }
    public long? PingIntervalMs { get; init; }
    public long? Streams { get; init; }
}

[BsonIgnoreExtraElements]
public record NetDiagSystem
{
    public NetDiagCpu Cpu { get; init; }
    public NetDiagMemory Memory { get; init; }
    public double? UptimeS { get; init; }
    public double? ClockSkewMs { get; init; }
    public bool? LauncherProxyConfigured { get; init; }
    public List<NetDiagAdapter> Adapters { get; init; }
    public NetDiagWifi Wifi { get; init; }
    public NetDiagVpn Vpn { get; init; }
    public NetDiagAntivirus Antivirus { get; init; }
}

[BsonIgnoreExtraElements]
public record NetDiagCpu
{
    public string Model { get; init; }
    public double? Cores { get; init; }
    public double? UsagePct { get; init; }
}

[BsonIgnoreExtraElements]
public record NetDiagMemory
{
    public double? TotalMb { get; init; }
    public double? UsedMb { get; init; }
}

[BsonIgnoreExtraElements]
public record NetDiagAdapter
{
    public string Name { get; init; }
    public string Kind { get; init; }
    public double? LinkSpeedMbps { get; init; }
    public long? Mtu { get; init; }
    public string Gateway { get; init; }
    public List<string> Dns { get; init; }
    public bool? HasDefaultRoute { get; init; }
    /// <summary>Bits per second (A8).</summary>
    public double? RxBps { get; init; }
    public double? TxBps { get; init; }
    /// <summary>ISO-8601 kept as a string: a malformed value must not fail the whole result.</summary>
    public string UpSince { get; init; }
    public string UpSinceReason { get; init; }
}

[BsonIgnoreExtraElements]
public record NetDiagWifi
{
    public double? RssiDbm { get; init; }
    public double? NoiseDbm { get; init; }
    public long? Channel { get; init; }
    public string Band { get; init; }
    public double? TxRateMbps { get; init; }
    public string Reason { get; init; }
}

[BsonIgnoreExtraElements]
public record NetDiagVpn
{
    public bool? Suspected { get; init; }
    public List<string> Evidence { get; init; }
}

[BsonIgnoreExtraElements]
public record NetDiagAntivirus
{
    public List<string> ThirdParty { get; init; }
    public string Reason { get; init; }
}

[BsonIgnoreExtraElements]
public record NetDiagFloSnapshotRow
{
    public int NodeId { get; init; }
    public string NodeName { get; init; }
    public string RouteLabel { get; init; }
    public string Transport { get; init; }
    public string Health { get; init; }
    public string ControllerStatus { get; init; }
    public double? Min { get; init; }
    /// <summary>flo's p95, kept with its user-facing meaning (spec §2).</summary>
    public double? Max { get; init; }
    public double? Avg { get; init; }
    public double? Stddev { get; init; }
    public double? Current { get; init; }
    public double? LossRate { get; init; }
    public double? P50 { get; init; }
    public double? P90 { get; init; }
    public double? P99 { get; init; }
    public double? MaxTrue { get; init; }
    public long? Samples { get; init; }
    public double? WindowMs { get; init; }
}

[BsonIgnoreExtraElements]
public record NetDiagTargetDto
{
    public string TargetKey { get; init; }
    public string Label { get; init; }
    public List<NetDiagPathDto> Paths { get; init; }
}

[BsonIgnoreExtraElements]
public record NetDiagPathDto
{
    public string Path { get; init; }
    public string RouteLabel { get; init; }
    public NetDiagEndpoints Endpoints { get; init; }
    public List<NetDiagProbeDto> Probes { get; init; }
}

[BsonIgnoreExtraElements]
public record NetDiagEndpoints
{
    public string Icmp { get; init; }
    public string Tcp { get; init; }
    public string Udp { get; init; }
}

[BsonIgnoreExtraElements]
public record NetDiagProbeDto
{
    public string Method { get; init; }
    public string Outcome { get; init; }
    public string Reason { get; init; }
    public List<NetDiagHopDto> Hops { get; init; }
    /// <summary>[offset_ms, rtt_us | null] pairs.</summary>
    public List<long?[]> Samples { get; init; }
}

[BsonIgnoreExtraElements]
public record NetDiagHopDto
{
    public int Ttl { get; init; }
    public string Ip { get; init; }
    public List<long?[]> Samples { get; init; }
}
