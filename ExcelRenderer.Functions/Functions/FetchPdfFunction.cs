using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ExcelRenderer.Functions.Functions;

public sealed class FetchPdfFunction
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<FetchPdfFunction> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public FetchPdfFunction(
        IHttpClientFactory httpClientFactory,
        IConfiguration config,
        ILogger<FetchPdfFunction> logger)
    {
        _httpClientFactory = httpClientFactory;
        _config = config;
        _logger = logger;
    }

    [Function(nameof(FetchPdf))]
    public async Task<HttpResponseData> FetchPdf(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "rewst/fetch-pdf")] HttpRequestData req,
        FunctionContext _)
    {
        // Auth check - reuses same API key pattern as the rest of the app
        switch (RenderApiKeyAuth.Validate(_config, req))
        {
            case RenderApiKeyAuthResult.MissingServerKey:
                return await Json(req, HttpStatusCode.ServiceUnavailable, new { error = "RENDER_API_KEY is not configured on the server." });
            case RenderApiKeyAuthResult.Ok:
                break;
            default:
                return await Json(req, HttpStatusCode.Forbidden, new { error = "Invalid or missing API key." });
        }

        // Parse request body
        string raw;
        try
        {
            raw = await new StreamReader(req.Body).ReadToEndAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "FetchPdf failed to read request body");
            return await Json(req, HttpStatusCode.BadRequest, new { error = "Could not read request body." });
        }

        FetchPdfRequest? payload;
        try
        {
            payload = JsonSerializer.Deserialize<FetchPdfRequest>(raw, JsonOptions);
        }
        catch (Exception ex)
        {
            return await Json(req, HttpStatusCode.BadRequest, new { error = "Invalid JSON: " + ex.Message });
        }

        if (payload is null || string.IsNullOrWhiteSpace(payload.Url))
        {
            return await Json(req, HttpStatusCode.BadRequest, new { error = "Field 'url' is required." });
        }

        if (!Uri.TryCreate(payload.Url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return await Json(req, HttpStatusCode.BadRequest, new { error = "Field 'url' must be a valid http/https URL." });
        }

        // Fetch the PDF
        byte[] pdfBytes;
        string? contentType;
        string? fileName;

        try
        {
            _logger.LogInformation("FetchPdf fetching {Url}", uri);

            var client = _httpClientFactory.CreateClient("FetchPdf");
            using var response = await client.GetAsync(uri);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("FetchPdf got {StatusCode} from {Url}", response.StatusCode, uri);
                return await Json(req, HttpStatusCode.BadGateway, new
                {
                    error = $"Remote URL returned {(int)response.StatusCode} {response.ReasonPhrase}."
                });
            }

            pdfBytes = await response.Content.ReadAsByteArrayAsync();
            contentType = response.Content.Headers.ContentType?.MediaType ?? "application/pdf";

            // Try to pull filename from Content-Disposition header
            fileName = response.Content.Headers.ContentDisposition?.FileNameStar
                       ?? response.Content.Headers.ContentDisposition?.FileName
                       ?? Path.GetFileName(uri.LocalPath);

            // Strip quotes if present
            fileName = fileName?.Trim('"', '\'');

            if (string.IsNullOrWhiteSpace(fileName))
                fileName = "document.pdf";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FetchPdf failed to fetch {Url}", uri);
            return await Json(req, HttpStatusCode.BadGateway, new { error = "Failed to fetch URL: " + ex.Message });
        }

        _logger.LogInformation("FetchPdf fetched {Bytes} bytes from {Url}", pdfBytes.Length, uri);

        return await Json(req, HttpStatusCode.OK, new
        {
            status = "ok",
            file_name = fileName,
            content_type = contentType,
            content_base64 = Convert.ToBase64String(pdfBytes),
            size_bytes = pdfBytes.Length
        });
    }

    private async Task<HttpResponseData> Json(HttpRequestData req, HttpStatusCode code, object obj)
    {
        var r = req.CreateResponse(code);
        r.Headers.Add("Content-Type", "application/json; charset=utf-8");
        await r.WriteStringAsync(JsonSerializer.Serialize(obj));
        return r;
    }
}

internal sealed class FetchPdfRequest
{
    public string? Url { get; set; }
}
