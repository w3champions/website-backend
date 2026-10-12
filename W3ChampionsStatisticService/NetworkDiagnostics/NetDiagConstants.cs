using System;
using System.Collections.Generic;
using System.Globalization;

namespace W3ChampionsStatisticService.NetworkDiagnostics;

public static class NetDiagConstants
{
    public const string HubEventName = "NetworkDiagnosticsRequest";

    public const string RunsCollection = "NetworkDiagnosticRuns";
    public const string AssignmentsCollection = "NetworkDiagnosticAssignments";
    public const string MeasurementsCollection = "NetworkDiagnosticMeasurements";
    public const string RawSeriesCollection = "NetworkDiagnosticRawSeries";
    public const string IpInfoCollection = "NetworkDiagnosticIpInfo";
    public const string ObservationsCollection = "NetworkDiagnosticObservations";

    public const string IcmpTraceroute = "IcmpTraceroute";
    public const string IcmpMtr = "IcmpMtr";
    public const string TcpTraceroute = "TcpTraceroute";
    public const string TcpMtr = "TcpMtr";
    public const string TcpPing = "TcpPing";
    public const string FloUdpPing = "FloUdpPing";

    public static readonly IReadOnlySet<string> AllMethods = new HashSet<string>(StringComparer.Ordinal)
        { IcmpTraceroute, IcmpMtr, TcpTraceroute, TcpMtr, TcpPing, FloUdpPing };
    public static readonly IReadOnlySet<string> TracerouteMethods = new HashSet<string>(StringComparer.Ordinal) { IcmpTraceroute, TcpTraceroute };
    public static readonly IReadOnlySet<string> IcmpMethods = new HashSet<string>(StringComparer.Ordinal) { IcmpTraceroute, IcmpMtr };
    public static readonly IReadOnlySet<string> TcpMethods = new HashSet<string>(StringComparer.Ordinal) { TcpTraceroute, TcpMtr, TcpPing };

    public const string OutcomeUnreachable = "unreachable";

    public const string PathDirect = "direct";
    public const string PathProxied = "proxied";
    public static readonly IReadOnlySet<string> AllPaths = new HashSet<string>(StringComparer.Ordinal) { PathDirect, PathProxied };
    public const string FloTargetKeyPrefix = "flo:";
    /// <summary>The one spelling of flo node N's target key: "flo:N".</summary>
    public static string FloTargetKey(int nodeId) => FloTargetKeyPrefix + nodeId.ToString(CultureInfo.InvariantCulture);

    public static readonly TimeSpan ExplicitDeliveryWindow = TimeSpan.FromDays(14);
    public static readonly TimeSpan FleetWindow = TimeSpan.FromMinutes(30);
    /// <summary>Fleet DeliverBy = WindowEnd + this, so a slot drawn near WindowEnd can still finish and submit (Deviation 11, contract A6).</summary>
    public static readonly TimeSpan FleetSubmitGrace = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan Retention = TimeSpan.FromDays(365);
    public static readonly TimeSpan RawRetention = TimeSpan.FromDays(90);
    public static readonly TimeSpan DispatchInterval = TimeSpan.FromSeconds(5);
    /// <summary>
    /// How long a hub connect waits for the on-connect delivery. SignalR dispatches a connection's invocations only
    /// after OnConnectedAsync returns, so a stalled Mongo call must not hold the connection longer than this.
    /// </summary>
    public static readonly TimeSpan OnConnectDeliveryBound = TimeSpan.FromSeconds(5);

    public const int DefaultWindowSeconds = 60;
    public const int MinWindowSeconds = 30;
    public const int MaxWindowSeconds = 300;
    public const int SnapshotAllowanceSeconds = 15;
    public const int DefaultCustomTcpPort = 443;
    public const int MaxBattleTagsPerRun = 500;
    public const int MaxCustomTargets = 50;
    public const int MaxTargetsPerRun = 200;

    /// <summary>
    /// Most observation rows one admin query reads before it groups them (newest first). A query that reaches it
    /// answers with <see cref="TruncatedHeader"/>.
    /// </summary>
    public const int MaxScannedObservations = 200_000;
    /// <summary>
    /// Largest `total` a filtered measurement list reports: counting stops there, so a wider match answers exactly
    /// this number. An unfiltered list reports the collection's estimated size instead and is not capped.
    /// </summary>
    public const int MaxCountedMeasurements = 100_000;
    /// <summary>
    /// Server-side time limit of every admin read on the result collections. A query that exceeds it is answered
    /// with 503 <see cref="QueryTimeoutError"/> instead of holding a connection and a scan.
    /// </summary>
    public static readonly TimeSpan AdminQueryMaxTime = TimeSpan.FromSeconds(15);
    public const string QueryTimeoutError = "query_timeout";
    /// <summary>Response header, value "true": the answer was computed from the newest <see cref="MaxScannedObservations"/> matching rows only.</summary>
    public const string TruncatedHeader = "X-NetDiag-Truncated";
}
