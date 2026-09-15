using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using W3C.Domain.Common;
using W3C.Domain.Maps;
using W3C.Domain.UpdateService.Contracts;
using W3C.Domain.Tracing;

namespace W3C.Domain.UpdateService;

[Trace]
public class UpdateServiceClient(IHttpClientFactory httpClientFactory)
{
    private static readonly string UpdateServiceUrl = Environment.GetEnvironmentVariable("UPDATE_API") ?? "https://update-service.test.w3champions.com";
    private static readonly string AdminSecret = Environment.GetEnvironmentVariable("ADMIN_SECRET") ?? "300C018C-6321-4BAB-B289-9CB3DB760CBB";
    private const string ServiceName = "update-service";

    /// <summary>
    /// update-service can spend minutes writing and parsing a 256 MiB map, well past HttpClient's
    /// 100 s default. Timeout is per-client, not per-request, so the upload path uses its own client.
    /// </summary>
    private static readonly TimeSpan UploadTimeout = TimeSpan.FromMinutes(10);

    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;
    private readonly HttpClient _httpClient = httpClientFactory.CreateClient();

    /// <summary>A success whose body is empty or not a JSON array of stored files throws with that success status.</summary>
    public async Task<MapFileData[]> GetMapFiles(int mapId)
    {
        var url = $"{UpdateServiceUrl}/api/content/maps?mapId={mapId}";
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        var response = await _httpClient.SendAsync(request);

        var content = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            ThrowUpstream(content, response.StatusCode);
        }

        return UpstreamContract.Deserialize<MapFileData[]>(content, response.StatusCode, ServiceName);
    }

    /// <summary>A success whose body is empty or not the stored-file JSON throws with that success status.</summary>
    public async Task<MapFileData> CreateMapFromFormAsync(HttpRequestMessage req, string uploadedBy)
    {
        // The admin upload is an opaque body pass-through (the multipart content object is moved onto
        // the outbound message verbatim), so the uploader cannot be injected as a form field without
        // buffering and re-encoding up to 128 MiB. It travels as a query parameter instead;
        // update-service accepts uploadedBy from either the form or the query string. Percent-encoded
        // per RFC 3986: a BattleTag's "#" would otherwise start a fragment and truncate the value.
        var url = $"{UpdateServiceUrl}/api/content/maps";
        if (!string.IsNullOrEmpty(uploadedBy))
        {
            url += $"?uploadedBy={Uri.EscapeDataString(uploadedBy)}";
        }

        var request = AdminRequest(HttpMethod.Post, url);
        request.Content = req.Content;
        var response = await _httpClient.SendAsync(request);

        var content = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            ThrowUpstream(content, response.StatusCode);
        }

        return UpstreamContract.Deserialize<MapFileData>(content, response.StatusCode, ServiceName);
    }

    /// <summary>A success whose body is empty or not the stored-file JSON throws with that success status.</summary>
    public async Task<MapFileData> GetMapFile(string fileId)
    {
        var url = $"{UpdateServiceUrl}/api/content/maps/{Uri.EscapeDataString(fileId)}";
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        var response = await _httpClient.SendAsync(request);

        var content = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            ThrowUpstream(content, response.StatusCode);
        }

        return UpstreamContract.Deserialize<MapFileData>(content, response.StatusCode, ServiceName);
    }

    public async Task DeleteMapFile(string fileId)
    {
        var url = $"{UpdateServiceUrl}/api/content/maps/{Uri.EscapeDataString(fileId)}";
        var request = AdminRequest(HttpMethod.Delete, url);
        var response = await _httpClient.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Unable to delete map file with id {fileId}", null, response.StatusCode);
        }
    }

    /// <summary>
    /// Streams a self-provided map into update-service. <paramref name="fileName"/> is the fileKey
    /// minus the "W3Champions/" prefix, e.g. "CustomGames/Legion TD-94ec3bda.w3x". update-service
    /// computes the mapProof from these bytes itself and stores only its hash — a client-provided
    /// value is never accepted. A duplicate target path surfaces as HttpStatusCode.Conflict; a success whose body is
    /// empty or not the stored-file JSON throws with that success status.
    /// <paramref name="mapFile"/> is consumed and disposed with the request, so open a fresh stream
    /// for every attempt.
    /// </summary>
    public async Task<MapFileData> UploadTemporaryMapAsync(
        Stream mapFile, string fileName, int mapId, string uploadedBy, CancellationToken cancellationToken)
    {
        using var uploadClient = _httpClientFactory.CreateClient();
        uploadClient.Timeout = UploadTimeout;

        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(mapId.ToString(CultureInfo.InvariantCulture)), "mapId");
        form.Add(new StringContent(fileName), "fileName");
        if (!string.IsNullOrEmpty(uploadedBy))
        {
            form.Add(new StringContent(uploadedBy), "uploadedBy");
        }

        var fileContent = new StreamContent(mapFile);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(fileContent, "mapFile", Path.GetFileName(fileName));

        var request = AdminRequest(HttpMethod.Post, $"{UpdateServiceUrl}/api/content/maps");
        request.Content = form;

        var response = await uploadClient.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            ThrowUpstream(content, response.StatusCode);
        }

        return UpstreamContract.Deserialize<MapFileData>(content, response.StatusCode, ServiceName);
    }

    /// <summary>
    /// Idempotent: update-service answers 204 whether or not the file existed. Only a file under
    /// W3Champions/CustomGames/ can be deleted: any other path, or one with a backslash or an empty, "." or ".."
    /// segment, throws ArgumentException before a request is built.
    /// </summary>
    public async Task DeleteMapFileByPathAsync(string filePath, CancellationToken cancellationToken)
    {
        RequireTemporaryMapFilePath(filePath);
        var url = $"{UpdateServiceUrl}/api/content/maps/file?filePath={HttpUtility.UrlEncode(filePath)}";
        var request = AdminRequest(HttpMethod.Delete, url);
        var response = await _httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Unable to delete map file at {filePath}", null, response.StatusCode);
        }
    }

    /// <summary>
    /// Lists stored files under a prefix, oldest-first, for orphan reconciliation. A page without its files array
    /// throws rather than reading as empty.
    /// </summary>
    public async Task<MapFileListingResponse> ListMapFilesAsync(
        string prefix, int olderThanHours, string after, int limit, CancellationToken cancellationToken)
    {
        var query = new List<string>
        {
            $"prefix={HttpUtility.UrlEncode(prefix)}",
            $"olderThanHours={olderThanHours.ToString(CultureInfo.InvariantCulture)}",
            $"limit={limit.ToString(CultureInfo.InvariantCulture)}",
        };
        if (!string.IsNullOrEmpty(after))
        {
            query.Add($"after={HttpUtility.UrlEncode(after)}");
        }

        var url = $"{UpdateServiceUrl}/api/content/maps/files?{string.Join("&", query)}";
        var request = AdminRequest(HttpMethod.Get, url);
        var response = await _httpClient.SendAsync(request, cancellationToken);

        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            ThrowUpstream(content, response.StatusCode);
        }

        return UpstreamContract.Deserialize<MapFileListingResponse>(content, response.StatusCode, ServiceName);
    }

    private static void RequireTemporaryMapFilePath(string filePath)
    {
        if (!TemporaryMapKeys.IsFilePath(filePath))
        {
            throw new ArgumentException(
                $"must be a file under {TemporaryMapKeys.PathPrefix} without backslashes or empty, '.' or '..' segments", nameof(filePath));
        }
    }

    private static HttpRequestMessage AdminRequest(HttpMethod method, string url)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add("x-admin-secret", AdminSecret);
        return request;
    }

    /// <summary>
    /// Turns an update-service error body into an HttpRequestException carrying its status code, and
    /// survives a body that is empty, not JSON, or has no message (deserialising ErrorData and
    /// dereferencing it unguarded throws NullReferenceException or JsonException instead).
    /// </summary>
    private static void ThrowUpstream(string content, HttpStatusCode statusCode)
    {
        string message = null;
        try
        {
            message = JsonConvert.DeserializeObject<ErrorData>(content)?.message;
        }
        catch (JsonException)
        {
            // Not JSON (e.g. a proxy error page): fall back to the status code below.
        }

        throw new HttpRequestException(message ?? $"update-service returned {(int)statusCode}", null, statusCode);
    }
}
