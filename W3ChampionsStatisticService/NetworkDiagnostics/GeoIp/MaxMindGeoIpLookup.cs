using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using MaxMind.Db;
using MaxMind.GeoIP2;
using MaxMind.GeoIP2.Responses;
using Microsoft.Extensions.Logging;
using W3ChampionsStatisticService.NetworkDiagnostics.Results;

namespace W3ChampionsStatisticService.NetworkDiagnostics.GeoIp;

public interface IGeoIpLookup
{
    /// <summary>Every input IP is a key; the value is null when nothing is known. Never throws.</summary>
    IReadOnlyDictionary<string, ClientGeo> Lookup(IEnumerable<string> ips);
}

/// <summary>
/// GeoLite2 City + ASN readers. The geoipupdate sidecar replaces the files by renaming a new file over the old one;
/// the readers are reopened when a file's mtime or size changes (checked at most every <see cref="RecheckInterval"/>).
/// Missing files give null geo; a file that cannot be opened keeps the previous reader.
/// </summary>
public sealed class MaxMindGeoIpLookup(string cityPath, string asnPath, TimeProvider time, ILogger<MaxMindGeoIpLookup> log)
    : IGeoIpLookup, IDisposable
{
    public static readonly TimeSpan RecheckInterval = TimeSpan.FromSeconds(60);

    private readonly object _gate = new();
    private readonly ReaderSlot _city = new(cityPath);
    private readonly ReaderSlot _asn = new(asnPath);
    private DateTimeOffset? _nextCheck;
    private bool _warnedMissing;

    public IReadOnlyDictionary<string, ClientGeo> Lookup(IEnumerable<string> ips)
    {
        var result = new Dictionary<string, ClientGeo>();
        try
        {
            lock (_gate)
            {
                RefreshIfDue();
                foreach (var ip in ips)
                {
                    if (ip == null || result.ContainsKey(ip)) continue;
                    result[ip] = NetDiagIp.IsStrictIpv4(ip) ? Resolve(IPAddress.Parse(ip)) : null;
                }
            }
        }
        catch (Exception e)
        {
            // Nothing here may reach the submit path.
            log.LogWarning(e, "GeoIP lookup failed");
            foreach (var ip in ips)
            {
                if (ip != null) result.TryAdd(ip, null);
            }
        }
        return result;
    }

    private ClientGeo Resolve(IPAddress address)
    {
        CityResponse city = null;
        AsnResponse asn = null;
        try
        {
            _city.Reader?.TryCity(address, out city);
            _asn.Reader?.TryAsn(address, out asn);
        }
        catch (Exception e)
        {
            log.LogWarning(e, "GeoIP lookup failed for {Ip}", address);
            return null;
        }
        if (city == null && asn == null) return null;
        return new ClientGeo
        {
            Country = city?.Country?.IsoCode,
            City = city?.City?.Name,
            Lat = city?.Location?.Latitude,
            Lon = city?.Location?.Longitude,
            Asn = asn?.AutonomousSystemNumber,
            AsnOrg = asn?.AutonomousSystemOrganization,
            GeoDbBuild = (_city.Reader ?? _asn.Reader)?.Metadata.BuildDate.ToString("yyyy-MM-dd"),
        };
    }

    private void RefreshIfDue()
    {
        var now = time.GetUtcNow();
        if (_nextCheck is { } next && now < next) return;
        _nextCheck = now + RecheckInterval;

        _city.Refresh(log);
        _asn.Refresh(log);

        var missing = _city.Reader == null || _asn.Reader == null;
        if (missing && !_warnedMissing)
        {
            log.LogWarning("GeoLite2 database missing (city: {CityPath} {CityLoaded}, asn: {AsnPath} {AsnLoaded}); network-diagnostics geo fields stay null until the files appear",
                _city.Path, _city.Reader != null, _asn.Path, _asn.Reader != null);
        }
        _warnedMissing = missing;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _city.Dispose();
            _asn.Dispose();
        }
    }

    private sealed class ReaderSlot(string path) : IDisposable
    {
        public string Path { get; } = path;
        public DatabaseReader Reader { get; private set; }
        private (DateTime Mtime, long Length)? _loaded;

        public void Refresh(ILogger log)
        {
            var info = new FileInfo(Path);
            if (!info.Exists)
            {
                Swap(null, null);
                return;
            }
            var signature = (info.LastWriteTimeUtc, info.Length);
            if (Reader != null && signature == _loaded) return;
            try
            {
                // Memory mode reads the whole file up front, so the reader never depends on the file afterwards.
                Swap(new DatabaseReader(Path, FileAccessMode.Memory), signature);
                log.LogInformation("Loaded GeoLite2 database {Path} (build {Build:yyyy-MM-dd})", Path, Reader.Metadata.BuildDate);
            }
            catch (Exception e)
            {
                // A half-written or corrupt file: keep the old reader and retry at the next check.
                log.LogWarning(e, "Could not open GeoLite2 database {Path}; keeping the previous one if any", Path);
            }
        }

        private void Swap(DatabaseReader next, (DateTime, long)? signature)
        {
            var old = Reader;
            Reader = next;
            _loaded = signature;
            old?.Dispose();
        }

        public void Dispose() => Swap(null, null);
    }
}
