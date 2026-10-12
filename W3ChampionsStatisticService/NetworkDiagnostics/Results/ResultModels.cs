using System;
using System.Collections.Generic;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using W3ChampionsStatisticService.NetworkDiagnostics.Submission;

namespace W3ChampionsStatisticService.NetworkDiagnostics.Results;

/// <summary>GeoLite2 enrichment of one IP (C6 clientGeo). Also the GeoIP lookup result.</summary>
[BsonIgnoreExtraElements]
public record ClientGeo
{
    public string Country { get; init; }
    public string City { get; init; }
    public double? Lat { get; init; }
    public double? Lon { get; init; }
    public long? Asn { get; init; }
    public string AsnOrg { get; init; }
    public string GeoDbBuild { get; init; }
}

[BsonIgnoreExtraElements]
public record MeasuredHop
{
    public int Ttl { get; init; }
    public string Ip { get; init; }
    public StatsDto Stats { get; init; }
}

[BsonIgnoreExtraElements]
public record MeasuredProbe
{
    public string Method { get; init; }
    public string Outcome { get; init; }
    public string Reason { get; init; }
    /// <summary>Null for traceroute-only methods (C6).</summary>
    public StatsDto Stats { get; init; }
    public List<MeasuredHop> Hops { get; init; } = [];
}

[BsonIgnoreExtraElements]
public record MeasuredPath
{
    public string Path { get; init; }
    public string RouteLabel { get; init; }
    public NetDiagEndpoints Endpoints { get; init; }
    public List<MeasuredProbe> Probes { get; init; } = [];
}

[BsonIgnoreExtraElements]
public record MeasuredTarget
{
    public string TargetKey { get; init; }
    public string Label { get; init; }
    public List<MeasuredPath> Paths { get; init; } = [];
}

/// <summary>One submitted result without raw samples (spec §4.3). _id = measurementId.</summary>
[BsonIgnoreExtraElements]
public record NetworkDiagnosticMeasurement
{
    [BsonId] public string Id { get; init; }
    public string RunId { get; init; }
    public string BattleTag { get; init; }
    public DateTime SubmittedAt { get; init; }
    public DateTime ExpiresAt { get; init; }
    public string ClientIp { get; init; }
    public ClientGeo ClientGeo { get; init; }
    public string Os { get; init; }
    public NetDiagOs OsInfo { get; init; }
    public string PrimaryAdapterType { get; init; }
    public bool? VpnSuspected { get; init; }
    public string LauncherVersion { get; init; }
    public DateTime? AttemptStartedAt { get; init; }
    public DateTime? WindowStartedAt { get; init; }
    public DateTime? WindowEndedAt { get; init; }
    public long Restarts { get; init; }
    public string PublicIpv4 { get; init; }
    public string DefaultGateway { get; init; }
    public NetDiagPacing Pacing { get; init; }
    public NetDiagSystem System { get; init; }
    public List<NetDiagFloSnapshotRow> FloSnapshot { get; init; } = [];
    public int TargetCount { get; init; }
    public List<MeasuredTarget> Targets { get; init; } = [];
}

/// <summary>One packed series. HopIndex null = probe-level series (TcpPing/FloUdpPing).</summary>
[BsonIgnoreExtraElements]
public record RawSeriesEntry
{
    public int TargetIndex { get; init; }
    public int PathIndex { get; init; }
    public int ProbeIndex { get; init; }
    public int? HopIndex { get; init; }
    /// <summary>BinData: RawSeriesCodec pairs.</summary>
    public byte[] Data { get; init; }
}

/// <summary>Raw series in their own collection: Mongo TTL deletes whole documents, and these live 90 d (spec §4.4).</summary>
[BsonIgnoreExtraElements]
public record NetworkDiagnosticRawSeries
{
    [BsonId] public string Id { get; init; }
    public DateTime SubmittedAt { get; init; }
    public DateTime ExpiresAt { get; init; }
    public List<RawSeriesEntry> Series { get; init; } = [];
}

/// <summary>Hop/client enrichment, deduplicated per (RunId, Ip) with $setOnInsert (spec §4.5).</summary>
[BsonIgnoreExtraElements]
public record NetworkDiagnosticIpInfo
{
    [BsonId] public ObjectId Id { get; init; }
    public string RunId { get; init; }
    public string Ip { get; init; }
    public long? Asn { get; init; }
    public string AsnOrg { get; init; }
    public string Country { get; init; }
    public string City { get; init; }
    public double? Lat { get; init; }
    public double? Lon { get; init; }
    public string GeoDbBuild { get; init; }
    public DateTime FirstSeenAt { get; init; }
    public DateTime ExpiresAt { get; init; }
}

/// <summary>Flat row per (target × path × probe) for querying and aggregation (spec §4.6, C6 ObservationDto).</summary>
[BsonIgnoreExtraElements]
public record NetworkDiagnosticObservation
{
    [BsonId] public ObjectId Id { get; init; }
    public string MeasurementId { get; init; }
    public string RunId { get; init; }
    public string BattleTag { get; init; }
    public DateTime SubmittedAt { get; init; }
    public string ClientIp { get; init; }
    public string Country { get; init; }
    public string City { get; init; }
    public double? Lat { get; init; }
    public double? Lon { get; init; }
    public long? Asn { get; init; }
    public string AsnOrg { get; init; }
    public string Os { get; init; }
    public string PrimaryAdapterType { get; init; }
    public bool? VpnSuspected { get; init; }
    public string TargetKey { get; init; }
    public int? NodeId { get; init; }
    public string TargetLabel { get; init; }
    public string TargetIp { get; init; }
    public string Path { get; init; }
    public string RouteLabel { get; init; }
    public string Method { get; init; }
    public string Outcome { get; init; }
    public string Reason { get; init; }
    public int Sent { get; init; }
    public int Received { get; init; }
    public double? LossPct { get; init; }
    public double? MinMs { get; init; }
    public double? AvgMs { get; init; }
    public double? MaxMs { get; init; }
    public double? StddevMs { get; init; }
    public double? P50Ms { get; init; }
    public double? P90Ms { get; init; }
    public double? P95Ms { get; init; }
    public double? P99Ms { get; init; }
    public double? JitterMs { get; init; }
    public int HopCount { get; init; }
    public string LastHopIp { get; init; }
    public long? LastHopAsn { get; init; }
    public DateTime ExpiresAt { get; init; }
}
