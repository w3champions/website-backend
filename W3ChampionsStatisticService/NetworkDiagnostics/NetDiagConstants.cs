using System;
using System.Collections.Generic;

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
}
