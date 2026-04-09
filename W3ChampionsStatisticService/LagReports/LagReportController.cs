using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using W3C.Contracts.Admin.Permission;
using W3C.Domain.Tracing;
using W3ChampionsStatisticService.WebApi.ActionFilters;

namespace W3ChampionsStatisticService.LagReports;

[ApiController]
[Route("api/lag-reports")]
[Trace]
public class LagReportController(LagReportRepository lagReportRepository, IFloStatsService floStatsService) : ControllerBase
{
    private readonly LagReportRepository _lagReportRepository = lagReportRepository;
    private readonly IFloStatsService _floStatsService = floStatsService;

    /// <summary>
    /// Submit a lag report — called by the launcher for each player (explicit or auto).
    /// Authenticated users only (any player, not admin-only).
    /// </summary>
    [HttpPost]
    [InjectActingPlayerAuthCode]
    public async Task<IActionResult> SubmitReport([FromBody] LagReportSubmissionDto dto, string actingPlayer)
    {
        var validationError = ValidateSubmission(dto);
        if (validationError != null)
        {
            return BadRequest(new { Error = validationError });
        }

        var playerData = MapToPlayer(dto, actingPlayer);

        var template = new LagReport
        {
            GameId = dto.GameMetadata.GameId,
            FloGameId = dto.GameMetadata.FloGameId,
            GameName = dto.GameMetadata.GameName,
            MapPath = dto.GameMetadata.MapPath,
            ServerNodeId = dto.ConnectionTopology.ServerNodeId,
            ServerNodeName = dto.ConnectionTopology.ServerNodeName,
        };

        var reportId = await _lagReportRepository.UpsertPlayerData(
            dto.GameMetadata.FloGameId,
            playerData,
            template
        );

        // Fire-and-forget: fetch server-side ping from flo-stats while data is still in LRU.
        // The match-finished handler is a fallback, but often runs before any player submits.
        _ = _floStatsService.FetchAndStoreIfNeeded(dto.GameMetadata.FloGameId, _lagReportRepository);

        return Ok(new LagReportSubmissionResponse { ReportId = reportId });
    }

    /// <summary>Admin: list lag reports with filters and pagination.</summary>
    [HttpGet]
    [BearerHasPermissionFilter(Permission = EPermission.Proxies)]
    public async Task<IActionResult> GetReports([FromQuery] LagReportQueryRequest req)
    {
        req.PageSize = Math.Clamp(req.PageSize, 1, 100);
        req.Page = Math.Max(req.Page, 0);

        var (items, total) = await _lagReportRepository.GetReports(req);

        var listItems = items.Select(r => new LagReportListItem
        {
            Id = r.Id,
            GameId = r.GameId,
            FloGameId = r.FloGameId,
            GameName = r.GameName,
            MapPath = r.MapPath,
            ServerNodeId = r.ServerNodeId,
            ServerNodeName = r.ServerNodeName,
            CreatedAt = r.CreatedAt,
            HasExplicitReport = r.HasExplicitReport,
            Players = r.Players.Select(p => new LagReportPlayerSummary
            {
                BattleTag = p.BattleTag,
                IsExplicit = p.IsExplicit,
                ConnectionType = p.ConnectionType,
                ProxyName = p.ProxyName,
                IssueCategories = p.IssueCategories,
                LagEventCount = p.Diagnostics?.LagEvents?.Count ?? 0,
                ConnectionEventCount = p.Diagnostics?.ConnectionEvents?.Count ?? 0,
            }).ToList(),
        }).ToList();

        return Ok(new { Items = listItems, Total = total });
    }

    /// <summary>Admin: get a single lag report with full diagnostics data.</summary>
    [HttpGet("{id}")]
    [BearerHasPermissionFilter(Permission = EPermission.Proxies)]
    public async Task<IActionResult> GetReport(string id)
    {
        var report = await _lagReportRepository.GetById(id);
        if (report == null)
        {
            return NotFound();
        }
        return Ok(report);
    }

    internal static string ValidateSubmission(LagReportSubmissionDto dto)
    {
        if (dto.Diagnostics == null) return "Missing diagnostics";
        if (dto.GameMetadata == null) return "Missing game_metadata";
        if (dto.ConnectionTopology == null) return "Missing connection_topology";

        var diag = dto.Diagnostics;
        if ((diag.LagEvents?.Count ?? 0) > 200) return "Too many lag_events";
        if ((diag.TargetMtr?.Count ?? 0) > 500) return "Too many target_mtr";
        if ((diag.AllServerBaselines?.Count ?? 0) > 1000) return "Too many all_server_baselines";
        if ((diag.ReverseMtr?.Count ?? 0) > 500) return "Too many reverse_mtr";
        if ((diag.PingHistory?.Count ?? 0) > 5000) return "Too many ping_history";
        if ((diag.ConnectionEvents?.Count ?? 0) > 200) return "Too many connection_events";
        if ((dto.Annotations?.Count ?? 0) > 200) return "Too many annotations";
        if ((dto.Categories?.Count ?? 0) > 20) return "Too many categories";

        if (dto.FreeText?.Length > 5000) return "free_text too long";
        if (dto.GameMetadata.GameName?.Length > 500) return "game_name too long";
        if (dto.GameMetadata.MapPath?.Length > 500) return "map_path too long";
        if (dto.ConnectionTopology.ServerNodeName?.Length > 500) return "server_node_name too long";
        if (dto.ConnectionTopology.ProxyName?.Length > 500) return "proxy_name too long";
        if (dto.ConnectionTopology.ProxyAddress?.Length > 500) return "proxy_address too long";
        if (dto.ConnectionTopology.ClientIp?.Length > 100) return "client_ip too long";

        foreach (var trace in (diag.TargetMtr ?? []).Concat(diag.ReverseMtr ?? []))
        {
            if (trace.Trace?.Hops?.Count > 64) return "Too many hops in trace";
        }
        foreach (var baseline in diag.AllServerBaselines ?? [])
        {
            if (baseline.Trace?.Hops?.Count > 64) return "Too many hops in baseline";
        }
        foreach (var annotation in dto.Annotations ?? [])
        {
            if (annotation.Text?.Length > 1000) return "Annotation text too long";
        }
        foreach (var lagEvent in diag.LagEvents ?? [])
        {
            if (lagEvent.Annotation?.Length > 1000) return "Lag event annotation too long";
        }

        return null;
    }

    internal static LagReportPlayer MapToPlayer(LagReportSubmissionDto dto, string battleTag)
    {
        var topo = dto.ConnectionTopology;

        // Parse "ip:port" into separate fields
        string proxyIp = null;
        int? proxyPort = null;
        if (!string.IsNullOrEmpty(topo.ProxyAddress))
        {
            var lastColon = topo.ProxyAddress.LastIndexOf(':');
            if (lastColon > 0)
            {
                proxyIp = topo.ProxyAddress[..lastColon];
                if (int.TryParse(topo.ProxyAddress[(lastColon + 1)..], out var port))
                {
                    proxyPort = port;
                }
            }
            else
            {
                proxyIp = topo.ProxyAddress;
            }
        }

        var diag = dto.Diagnostics;

        // Merge annotations into lag events
        var annotationsByOffset = (dto.Annotations ?? [])
            .ToDictionary(a => a.GameTimeOffsetMs, a => a.Text);

        return new LagReportPlayer
        {
            BattleTag = battleTag,
            ClientIp = topo.ClientIp,
            ConnectionType = topo.ConnectionType,
            ProxyName = topo.ProxyName,
            ProxyIp = proxyIp,
            ProxyPort = proxyPort,
            IsExplicit = dto.IsExplicit,
            IssueCategories = dto.Categories ?? [],
            FreeText = dto.FreeText ?? "",
            Annotations = (dto.Annotations ?? []).Select(a => new LagReportAnnotation
            {
                GameTimeOffsetMs = a.GameTimeOffsetMs,
                Text = a.Text,
            }).ToList(),
            Diagnostics = new PlayerDiagnostics
            {
                LagEvents = (diag.LagEvents ?? []).Select(e => new LagEvent
                {
                    Timestamp = e.Timestamp,
                    GameTimeOffsetMs = e.GameTimeOffsetMs,
                    Annotation = annotationsByOffset.GetValueOrDefault(e.GameTimeOffsetMs),
                }).ToList(),
                TargetMtr = (diag.TargetMtr ?? []).Where(t => t.Trace != null).Select(MapTrace).ToList(),
                AllServerBaselines = (diag.AllServerBaselines ?? []).Where(s => s.Trace != null).Select(s => new ServerBaseline
                {
                    Timestamp = s.Timestamp,
                    ServerId = s.ServerId,
                    ServerName = s.ServerName,
                    Target = s.Trace.Target,
                    Hops = (s.Trace.Hops ?? []).Select(MapHop).ToList(),
                }).ToList(),
                ReverseMtr = (diag.ReverseMtr ?? []).Where(t => t.Trace != null).Select(MapTrace).ToList(),
                PingHistory = (diag.PingHistory ?? []).Select(p => new PingSample
                {
                    Timestamp = p.Timestamp,
                    Min = (int?)p.Stats.Min,
                    Max = (int?)p.Stats.Max,
                    Avg = (int?)p.Stats.Avg,
                    Stddev = (float?)p.Stats.Stddev,
                    Current = (int?)p.Stats.Current,
                    LossRate = (float)p.Stats.LossRate,
                }).ToList(),
                ConnectionEvents = (diag.ConnectionEvents ?? []).Select(c => new ConnectionEventData
                {
                    Timestamp = c.Timestamp,
                    GameTimeOffsetMs = c.GameTimeOffsetMs,
                    EventType = c.EventType,
                    DurationMs = c.DurationMs,
                }).ToList(),
            },
        };
    }

    private static TraceMeasurement MapTrace(TimedTraceDto t) => new()
    {
        Timestamp = t.Timestamp,
        Target = t.Trace.Target,
        Hops = (t.Trace.Hops ?? []).Select(MapHop).ToList(),
    };

    private static HopData MapHop(HopDto h) => new()
    {
        HopNumber = h.HopNumber,
        Host = h.Host,
        AvgRttMs = h.AvgRttMs,
        MinRttMs = h.MinRttMs,
        MaxRttMs = h.MaxRttMs,
        StddevMs = h.StddevMs,
        LossPercent = h.LossPercent,
    };
}
