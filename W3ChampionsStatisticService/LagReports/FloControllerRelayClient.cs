using System;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Net.Client;
using Serilog;
using Serilog.Events;
using W3C.Domain.Tracing;
using W3ChampionsStatisticService.LagReports.FloControllerGrpc;

namespace W3ChampionsStatisticService.LagReports;

/// <summary>Calls the flo controller's GetGameRelayTelemetry RPC.</summary>
public interface IFloControllerRelayClient
{
    /// <summary>Returns null when the call fails or the client is not configured.</summary>
    Task<GetGameRelayTelemetryReply> GetGameRelayTelemetry(int floGameId, int floPlayerId);
}

[Trace]
public sealed class FloControllerRelayClient : IFloControllerRelayClient, IDisposable
{
    private static readonly string Url =
        Environment.GetEnvironmentVariable("FLO_CONTROLLER_GRPC_URL") ?? "http://service.w3flo.com:3549";
    private static readonly string Secret = Environment.GetEnvironmentVariable("FLO_CONTROLLER_SECRET");

    // The controller gives each walk 10 s plus up to 1 s waiting for a permit.
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(12);

    // Worst case reply: 8 connections × 5 legs × 2 series × 2,400 buckets × 19 B ≈ 3.6 MB,
    // too close to gRPC's 4 MB default.
    private const int MaxReplyBytes = 8 * 1024 * 1024;

    // These mean the deployment is misconfigured, not that the controller is briefly unwell.
    private static readonly StatusCode[] ConfigurationErrors = [StatusCode.Unauthenticated, StatusCode.PermissionDenied, StatusCode.Unimplemented];

    private readonly GrpcChannel _channel;
    private readonly FloController.FloControllerClient _client;

    public FloControllerRelayClient()
    {
        if (string.IsNullOrEmpty(Secret))
        {
            Log.Warning("FloControllerRelayClient: FLO_CONTROLLER_SECRET is not set; lag reports will have no relay telemetry");
            return;
        }

        _channel = GrpcChannel.ForAddress(Url, new GrpcChannelOptions { MaxReceiveMessageSize = MaxReplyBytes });
        _client = new FloController.FloControllerClient(_channel);
    }

    public async Task<GetGameRelayTelemetryReply> GetGameRelayTelemetry(int floGameId, int floPlayerId)
    {
        if (_client == null)
        {
            return null;
        }

        try
        {
            return await _client.GetGameRelayTelemetryAsync(
                new GetGameRelayTelemetryRequest { GameId = floGameId, PlayerId = floPlayerId },
                new Metadata { { "x-flo-secret", Secret } },
                DateTime.UtcNow.Add(Deadline));
        }
        catch (RpcException ex)
        {
            var level = Array.IndexOf(ConfigurationErrors, ex.StatusCode) >= 0 ? LogEventLevel.Error : LogEventLevel.Warning;
            Log.Write(level, "FloControllerRelayClient: GetGameRelayTelemetry for game {GameId} player {PlayerId} failed: {StatusCode} {Detail}",
                floGameId, floPlayerId, ex.StatusCode, ex.Status.Detail);
            return null;
        }
    }

    public void Dispose() => _channel?.Dispose();
}
