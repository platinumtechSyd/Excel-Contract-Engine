using System.Net.Http.Headers;
using System.Text.Json;
using ExcelRenderer.Functions.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ExcelRenderer.Functions.Services;

public sealed class GraphSharePointUploadService
{
    /// <summary>
    /// Matches Microsoft Graph <c>PUT …/content</c> limit for a single request (see
    /// <see href="https://learn.microsoft.com/en-us/graph/api/driveitem-put-content?view=graph-rest-1.0">Upload small files</see>).
    /// Larger files require an upload session; this API does not accept files above this size.
    /// </summary>
    private const long MaxFileBytes = 250L * 1024 * 1024;

    private readonly HttpClient _http;
    private readonly IConfiguration _config;
    private readonly ILogger<GraphSharePointUploadService> _logger;

    public GraphSharePointUploadService(HttpClient http, IConfiguration config, ILogger<GraphSharePointUploadService> logger)
    {
        _http = http;
        _config = config;
        _logger = logger;
    }

    public async Task<SharePointUploadResult> UploadAsync(SharePointUploadPayload payload, CancellationToken cancellationToken)
    {
        var tenantId = _config["GRAPH_TENANT_ID"];
        var clientId = _config["GRAPH_CLIENT_ID"];
        var clientSecret = _config["GRAPH_CLIENT_SECRET"];

        if (string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
        {
            return SharePointUploadResult.Fail(
                "GRAPH_NOT_CONFIGURED",
                "Set GRAPH_TENANT_ID, GRAPH_CLIENT_ID, and GRAPH_CLIENT_SECRET app settings.");
        }

        if (string.IsNullOrWhiteSpace(payload.FileName))
            return SharePointUploadResult.Fail("VALIDATION_ERROR", "file_name is required.");

        if (string.IsNullOrWhiteSpace(payload.ContentBase64))
            return SharePointUploadResult.Fail("VALIDATION_ERROR", "content_base64 is required.");

        var hasSiteId = !string.IsNullOrWhiteSpace(payload.SiteId);
        var hasSiteUrl = !string.IsNullOrWhiteSpace(payload.SiteUrl);
        if (hasSiteId == hasSiteUrl)
        {
            return SharePointUploadResult.Fail(
                "VALIDATION_ERROR",
                "Provide exactly one of site_id or site_url.");
        }

        var hasDriveId = !string.IsNullOrWhiteSpace(payload.DriveId);
        var hasLibrary = !string.IsNullOrWhiteSpace(payload.LibraryName);
        if (hasDriveId == hasLibrary)
        {
            return SharePointUploadResult.Fail(
                "VALIDATION_ERROR",
                "Provide exactly one of drive_id or library_name.");
        }

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(payload.ContentBase64.Trim());
        }
        catch (FormatException)
        {
            return SharePointUploadResult.Fail("VALIDATION_ERROR", "content_base64 is not valid base64.");
        }

        if (bytes.Length == 0)
            return SharePointUploadResult.Fail("VALIDATION_ERROR", "Decoded file is empty.");

        if (bytes.Length > MaxFileBytes)
        {
            return SharePointUploadResult.Fail(
                "FILE_TOO_LARGE",
                $"File size ({bytes.Length} bytes) exceeds maximum ({MaxFileBytes} bytes). Graph single-request uploads are limited to 250 MB; use an upload session for larger files.");
        }

        var token = await GetAppTokenAsync(tenantId, clientId, clientSecret, cancellationToken);
        if (token is null)
            return SharePointUploadResult.Fail("GRAPH_AUTH_FAILED", "Could not acquire Microsoft Graph access token.");

        try
        {
            var siteId = await ResolveSiteIdAsync(token, payload, cancellationToken);
            if (siteId is null)
                return SharePointUploadResult.Fail("SITE_NOT_FOUND", "Could not resolve SharePoint site.");

            var driveId = await ResolveDriveIdAsync(token, siteId, payload, cancellationToken);
            if (driveId is null)
                return SharePointUploadResult.Fail("DRIVE_NOT_FOUND", "Could not resolve document library / drive.");

            var relativePath = BuildRelativePath(payload.FolderPath, payload.FileName!);
            var contentType = string.IsNullOrWhiteSpace(payload.ContentType)
                ? "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
                : payload.ContentType.Trim();

            return await PutContentAsync(token, driveId, relativePath, bytes, contentType, payload.Overwrite, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Graph upload HTTP error");
            return SharePointUploadResult.Fail("GRAPH_HTTP_ERROR", ex.Message);
        }
    }

    private async Task<string?> GetAppTokenAsync(
        string tenantId,
        string clientId,
        string clientSecret,
        CancellationToken cancellationToken)
    {
        var url = $"https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/token";
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
            ["scope"] = "https://graph.microsoft.com/.default",
            ["grant_type"] = "client_credentials"
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
        using var response = await _http.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Token request failed: {Status} {Body}", response.StatusCode, body);
            return null;
        }

        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("access_token").GetString();
    }

    private async Task<string?> ResolveSiteIdAsync(string token, SharePointUploadPayload payload, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(payload.SiteId))
        {
            var id = payload.SiteId.Trim();
            var url = $"https://graph.microsoft.com/v1.0/sites/{Uri.EscapeDataString(id)}";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await _http.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Resolve site by id failed: {Status} {Body}", response.StatusCode, body);
                return null;
            }

            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.GetProperty("id").GetString();
        }

        var siteUrl = payload.SiteUrl!.Trim();
        if (!Uri.TryCreate(siteUrl, UriKind.Absolute, out var uri) || (uri.Scheme != "https" && uri.Scheme != "http"))
            return null;

        var host = uri.Host;
        var path = uri.AbsolutePath.TrimEnd('/');
        if (string.IsNullOrEmpty(path))
            path = "/";

        var sitePath = $"{host}:{path}";
        var requestUrl = $"https://graph.microsoft.com/v1.0/sites/{Uri.EscapeDataString(sitePath)}";
        using var req = new HttpRequestMessage(HttpMethod.Get, requestUrl);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var r = await _http.SendAsync(req, cancellationToken);
        var b = await r.Content.ReadAsStringAsync(cancellationToken);
        if (!r.IsSuccessStatusCode)
        {
            _logger.LogWarning("Resolve site by URL failed: {Status} {Body}", r.StatusCode, b);
            return null;
        }

        using var d = JsonDocument.Parse(b);
        return d.RootElement.GetProperty("id").GetString();
    }

    private async Task<string?> ResolveDriveIdAsync(string token, string siteId, SharePointUploadPayload payload, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(payload.DriveId))
            return payload.DriveId.Trim();

        var url = $"https://graph.microsoft.com/v1.0/sites/{Uri.EscapeDataString(siteId)}/drives";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await _http.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("List drives failed: {Status} {Body}", response.StatusCode, body);
            return null;
        }

        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("value", out var drives))
            return null;

        var target = payload.LibraryName!.Trim();
        foreach (var drive in drives.EnumerateArray())
        {
            if (!drive.TryGetProperty("name", out var nameEl))
                continue;
            var name = nameEl.GetString();
            if (string.Equals(name, target, StringComparison.OrdinalIgnoreCase))
                return drive.GetProperty("id").GetString();
        }

        return null;
    }

    private static string BuildRelativePath(string? folderPath, string fileName)
    {
        var folder = (folderPath ?? "").Trim().Replace('\\', '/').Trim('/');
        var name = fileName.Trim().Replace('\\', '/');
        if (string.IsNullOrEmpty(folder))
            return name;
        return $"{folder}/{name}";
    }

    /// <summary>PUT /drives/{id}/root:/{path}:/content — Graph supports up to 250 MB per request.</summary>
    private async Task<SharePointUploadResult> PutContentAsync(
        string token,
        string driveId,
        string relativePath,
        byte[] bytes,
        string contentType,
        bool overwrite,
        CancellationToken cancellationToken)
    {
        var encodedPath = EncodeGraphDrivePath(relativePath);
        var q = overwrite ? "?@microsoft.graph.conflictBehavior=replace" : "";
        var url = $"https://graph.microsoft.com/v1.0/drives/{Uri.EscapeDataString(driveId)}/root:/{encodedPath}:/content{q}";

        using var request = new HttpRequestMessage(HttpMethod.Put, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = new ByteArrayContent(bytes);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);

        using var response = await _http.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("PUT upload failed: {Status} {Body}", response.StatusCode, body);
            return SharePointUploadResult.Fail("GRAPH_UPLOAD_FAILED", $"{response.StatusCode}: {body}");
        }

        using var doc = JsonDocument.Parse(body);
        return MapGraphItem(doc.RootElement);
    }

    private static SharePointUploadResult MapGraphItem(JsonElement root)
    {
        var webUrl = root.TryGetProperty("webUrl", out var w) ? w.GetString() : null;
        var itemId = root.TryGetProperty("id", out var i) ? i.GetString() : null;
        string? path = null;
        if (root.TryGetProperty("parentReference", out var parent) && parent.ValueKind == JsonValueKind.Object &&
            parent.TryGetProperty("path", out var pathEl))
            path = pathEl.GetString();

        return SharePointUploadResult.Success(webUrl, path, itemId);
    }

    private static string EncodeGraphDrivePath(string relativePath)
    {
        var parts = relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return string.Join("/", parts.Select(Uri.EscapeDataString));
    }
}
