using System.Collections.Generic;
using System.Text.Json;
using W3ChampionsStatisticService.NetworkDiagnostics.Submission;

namespace WC3ChampionsStatisticService.Tests.NetworkDiagnostics;

public static class SubmissionFixtures
{
    public const string MeasurementId = "6f1d0c9e-4c1b-4a8e-9d55-0b6b8f0f7a11";

    // The C5 example with every alternative replaced by a concrete value. One probe has neither "hops" nor
    // "samples" (the launcher skips both when a method returns no data) and window_ended_at carries 9 fraction
    // digits (chrono's SecondsFormat::AutoSi emits 0, 3, 6 or 9).
    public const string FullJson = """
    {
      "measurement_id": "6f1d0c9e-4c1b-4a8e-9d55-0b6b8f0f7a11",
      "launcher_version": "1.6.20",
      "os": { "family": "windows", "name": "Windows 11 Pro", "version": "10.0.26200" },
      "attempt_started_at": "2026-10-12T10:00:00Z",
      "window_started_at": "2026-10-12T10:00:15Z",
      "window_ended_at": "2026-10-12T10:01:15.123456789Z",
      "restarts": 1,
      "public_ipv4": "81.2.69.160",
      "default_gateway": "192.168.1.1",
      "pacing": { "mtr_round_ms": 6000, "ping_interval_ms": 1000, "streams": 330, "icmp_send_gap_us": 2000 },
      "system": {
        "cpu": { "model": "AMD Ryzen 7", "cores": 8, "usage_pct": 12.5 },
        "memory": { "total_mb": 32768, "used_mb": 14000 },
        "uptime_s": 123456,
        "clock_skew_ms": 12,
        "launcher_proxy_configured": false,
        "adapters": [{
          "name": "Ethernet 2", "kind": "ethernet",
          "link_speed_mbps": 1000, "mtu": 1500, "gateway": "192.168.1.1", "dns": ["192.168.1.1"],
          "has_default_route": true, "rx_bps": 12000, "tx_bps": 3000,
          "up_since": "2026-10-12T08:00:00Z", "up_since_reason": null
        }],
        "wifi": { "rssi_dbm": -55, "noise_dbm": -90, "channel": 36, "band": "5GHz", "tx_rate_mbps": 573, "reason": null },
        "vpn": { "suspected": true, "evidence": ["utun3 holds default route"] },
        "antivirus": { "third_party": ["Kaspersky"], "reason": null }
      },
      "flo_snapshot": [{
        "node_id": 12, "node_name": "US East 4", "route_label": "US East 4 -> US East 3",
        "transport": "QUIC", "health": "ok", "controller_status": "connected",
        "min": 10, "max": 15, "avg": 12, "stddev": 0.8, "current": 12, "loss_rate": 0.0,
        "p50": 12, "p90": 14, "p99": 16, "max_true": 40, "samples": 256, "window_ms": 3800000
      }],
      "targets": [{
        "target_key": "flo:12",
        "label": "US East 4",
        "paths": [{
          "path": "direct",
          "route_label": null,
          "endpoints": { "icmp": "1.2.3.4", "tcp": "1.2.3.4:3554", "udp": "1.2.3.4:3552" },
          "probes": [
            {
              "method": "IcmpMtr", "outcome": "ok", "reason": null,
              "hops": [
                { "ttl": 1, "ip": "192.168.1.1", "samples": [[0, 1234], [6000, null]] },
                { "ttl": 2, "ip": "1.128.0.1", "samples": [[0, 9000], [6000, 9100]] },
                { "ttl": 3, "ip": "1.2.3.4", "samples": [[0, 15000], [6000, 17000]] }
              ]
            },
            { "method": "TcpPing", "outcome": "ok", "reason": null, "samples": [[0, 15234], [1000, null]] },
            { "method": "TcpMtr", "outcome": "unsupported", "reason": "os_requires_admin", "hops": [] },
            { "method": "FloUdpPing", "outcome": "timeout", "reason": null }
          ]
        }]
      }]
    }
    """;

    // Shape of the launcher's real serializer output (tauri-plugin-netdiag result.rs, test
    // assembles_the_c5_wire_shape, extended with a populated `system` and a proxied path): every Option is an
    // explicit null, hops/samples are skipped when None, an unsupported ping carries "samples": [], timestamps
    // use chrono AutoSi, and the integer widths reach u32/u64 limits.
    public const string LauncherProducedJson = """
    {
      "measurement_id": "6f1d0c9e-4c1b-4a8e-9d55-0b6b8f0f7a11",
      "launcher_version": "1.6.20",
      "os": { "family": "macos", "name": "macOS 15.6", "version": "15.6" },
      "attempt_started_at": "2026-09-21T14:13:20Z",
      "window_started_at": "2026-09-21T14:13:22.250Z",
      "window_ended_at": "2026-09-21T14:14:22.250123Z",
      "restarts": 4294967295,
      "public_ipv4": "203.0.113.7",
      "default_gateway": null,
      "pacing": { "mtr_round_ms": 6000, "ping_interval_ms": 1000, "streams": 4294967295 },
      "system": {
        "cpu": { "model": null, "cores": 10, "usage_pct": null },
        "memory": { "total_mb": 16384, "used_mb": 9000 },
        "uptime_s": 18446744073709551615,
        "clock_skew_ms": -9223372036854775808,
        "launcher_proxy_configured": null,
        "adapters": [{
          "name": "en0", "kind": "wifi",
          "link_speed_mbps": null, "mtu": 4294967295, "gateway": null, "dns": [],
          "has_default_route": false, "rx_bps": null, "tx_bps": 0,
          "up_since": null, "up_since_reason": "not_available"
        }],
        "wifi": { "rssi_dbm": -61, "noise_dbm": null, "channel": 4294967295, "band": null, "tx_rate_mbps": 286.5, "reason": null },
        "vpn": { "suspected": false, "evidence": [] },
        "antivirus": { "third_party": [], "reason": "not_applicable" }
      },
      "flo_snapshot": [{
        "node_id": 3, "node_name": null, "route_label": null,
        "transport": null, "health": "controller_unreachable", "controller_status": null,
        "min": null, "max": null, "avg": null, "stddev": null, "current": null, "loss_rate": null,
        "p50": null, "p90": null, "p99": null, "max_true": null, "samples": 4294967295, "window_ms": null
      }],
      "targets": [
        {
          "target_key": "gateway",
          "label": "Default gateway",
          "paths": [{
            "path": "direct",
            "route_label": null,
            "endpoints": { "icmp": "192.168.1.1", "tcp": null, "udp": null },
            "probes": [
              {
                "method": "IcmpMtr", "outcome": "ok", "reason": null,
                "hops": [{ "ttl": 1, "ip": "192.168.1.1", "samples": [[0, 1234], [1000, null]] }]
              },
              { "method": "TcpPing", "outcome": "unsupported", "reason": "not_applicable", "samples": [] }
            ]
          }]
        },
        {
          "target_key": "flo:12",
          "label": "US East 4",
          "paths": [
            {
              "path": "proxied",
              "route_label": "US East 4 -> US East 3",
              "endpoints": { "icmp": "1.2.3.4", "tcp": "1.2.3.4:3554", "udp": "1.2.3.4:3552" },
              "probes": [
                { "method": "TcpTraceroute", "outcome": "error", "reason": "probe_task_failed", "hops": [] },
                { "method": "FloUdpPing", "outcome": "ok", "reason": null, "samples": [[0, 15234], [4294967295, 4294967294]] }
              ]
            }
          ]
        }
      ]
    }
    """;

    public static NetDiagSubmission Parse(string json) => JsonSerializer.Deserialize<NetDiagSubmission>(json, SubmissionJson.Options);

    public static NetDiagSubmission Minimal(string measurementId) => new()
    {
        MeasurementId = measurementId,
        Os = new NetDiagOs { Family = "macos" },
        Targets = new List<NetDiagTargetDto>(),
    };
}
