using System.Net;
using System.Text;
using System.Text.Json;
using ExcelRenderer.Functions.Models;
using ExcelRenderer.Functions.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ExcelRenderer.Functions.Functions;

public sealed class RewstSharePointUploadFunction
{
    private readonly GraphSharePointUploadService _upload;
    private readonly IConfiguration _config;
    private readonly ILogger<RewstSharePointUploadFunction> _logger;

    private static readonly JsonSerializerOptions DeserializeOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public RewstSharePointUploadFunction(
        GraphSharePointUploadService upload,
        IConfiguration config,
        ILogger<RewstSharePointUploadFunction> logger)
    {
        _upload = upload;
        _config = config;
        _logger = logger;
    }

    [Function(nameof(RewstSharePointUpload))]
    public async Task<HttpResponseData> RewstSharePointUpload(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "rewst/sharepoint/upload")] HttpRequestData req,
        FunctionContext _)
    {
        var authErr = await TryAuthResponseAsync(req);
        if (authErr is not null)
            return authErr;

        var correlationId = GetCorrelationId(req);
        if (!string.IsNullOrEmpty(correlationId))
            _logger.LogInformation("Rewst request RewstSharePointUpload correlation_id={CorrelationId}", correlationId);

        string raw;
        try
        {
            raw = await new StreamReader(req.Body, Encoding.UTF8).ReadToEndAsync();
        }
        catch
        {
            return await Json(req, HttpStatusCode.BadRequest, new { valid = false, error = "Request body could not be read." });
        }

        if (string.IsNullOrWhiteSpace(raw))
            return await Json(req, HttpStatusCode.BadRequest, new { valid = false, error = "Request body is empty." });

        RewstRequest? wrapper;
        try
        {
            wrapper = JsonSerializer.Deserialize<RewstRequest>(raw, DeserializeOpts);
        }
        catch (Exception ex)
        {
            return await Json(req, HttpStatusCode.BadRequest, new { valid = false, error = "Outer JSON is invalid: " + ex.Message });
        }

        if (wrapper is null || string.IsNullOrWhiteSpace(wrapper.PayloadJson))
        {
            return await Json(req, HttpStatusCode.BadRequest, new { valid = false, error = "Field payload_json is required." });
        }

        SharePointUploadPayload? inner;
        try
        {
            inner = JsonSerializer.Deserialize<SharePointUploadPayload>(wrapper.PayloadJson.Trim(), DeserializeOpts);
        }
        catch (Exception ex)
        {
            return await Json(req, HttpStatusCode.BadRequest, new { valid = false, error = "payload_json is not valid JSON: " + ex.Message });
        }

        if (inner is null)
            return await Json(req, HttpStatusCode.BadRequest, new { valid = false, error = "payload_json deserialized to null." });

        var result = await _upload.UploadAsync(inner, default);
        if (!result.Ok)
        {
            return await Json(req, HttpStatusCode.BadRequest, new
            {
                status = "error",
                error_code = result.ErrorCode,
                message = result.ErrorMessage
            });
        }

        return await Json(req, HttpStatusCode.OK, new
        {
            status = result.Status,
            web_url = result.WebUrl,
            path = result.Path,
            item_id = result.ItemId
        });
    }

    private static string? GetCorrelationId(HttpRequestData req)
    {
        foreach (var headerName in new[] { "X-Correlation-Id", "X-Request-Id", "Correlation-Id" })
        {
            if (!req.Headers.TryGetValues(headerName, out var values))
                continue;
            var v = values.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(v))
                return v.Trim();
        }

        return null;
    }

    private async Task<HttpResponseData> Json(HttpRequestData req, HttpStatusCode code, object obj)
    {
        var r = req.CreateResponse(code);
        r.Headers.Add("Content-Type", "application/json; charset=utf-8");
        await r.WriteStringAsync(JsonSerializer.Serialize(obj));
        return r;
    }

    private async Task<HttpResponseData?> TryAuthResponseAsync(HttpRequestData req)
    {
        switch (RenderApiKeyAuth.Validate(_config, req))
        {
            case RenderApiKeyAuthResult.Ok:
                return null;
            case RenderApiKeyAuthResult.MissingServerKey:
                return await Json(req, HttpStatusCode.ServiceUnavailable, new { error = "RENDER_API_KEY is not configured on the server." });
            default:
                return await Json(req, HttpStatusCode.Forbidden, new { error = "Invalid or missing API key." });
        }
    }
}
