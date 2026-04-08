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

public sealed class RewstExcelFunction
{
    private readonly ExcelRenderService _renderer;
    private readonly ContractNormalizationService _normalizer;
    private readonly IConfiguration _config;
    private readonly ILogger<RewstExcelFunction> _logger;

    private static readonly JsonSerializerOptions RewstDeserializeOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public RewstExcelFunction(
        ExcelRenderService renderer,
        ContractNormalizationService normalizer,
        IConfiguration config,
        ILogger<RewstExcelFunction> logger)
    {
        _renderer = renderer;
        _normalizer = normalizer;
        _config = config;
        _logger = logger;
    }

    [Function(nameof(RewstTier1Validate))]
    public Task<HttpResponseData> RewstTier1Validate(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "rewst/tier1/validate")] HttpRequestData req,
        FunctionContext _) =>
        RunValidateAsync(req, ContractTierExpectation.Tier1Workbook, nameof(RewstTier1Validate));

    [Function(nameof(RewstTier1Render))]
    public Task<HttpResponseData> RewstTier1Render(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "rewst/tier1/render")] HttpRequestData req,
        FunctionContext _) =>
        RunRenderAsync(req, ContractTierExpectation.Tier1Workbook, nameof(RewstTier1Render));

    [Function(nameof(RewstTier2Validate))]
    public Task<HttpResponseData> RewstTier2Validate(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "rewst/tier2/validate")] HttpRequestData req,
        FunctionContext _) =>
        RunValidateAsync(req, ContractTierExpectation.Tier2Sheets, nameof(RewstTier2Validate));

    [Function(nameof(RewstTier2Render))]
    public Task<HttpResponseData> RewstTier2Render(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "rewst/tier2/render")] HttpRequestData req,
        FunctionContext _) =>
        RunRenderAsync(req, ContractTierExpectation.Tier2Sheets, nameof(RewstTier2Render));

    [Function(nameof(OpenApiRewst))]
    public async Task<HttpResponseData> OpenApiRewst(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "openapi-rewst.json")] HttpRequestData req,
        FunctionContext _)
    {
        var r = req.CreateResponse(HttpStatusCode.OK);
        r.Headers.Add("Content-Type", "application/json; charset=utf-8");
        await r.WriteStringAsync(OpenApiRewstDocument.Json);
        return r;
    }

    private async Task<HttpResponseData> RunValidateAsync(
        HttpRequestData req,
        ContractTierExpectation tier,
        string operationName)
    {
        if (!await AuthorizeAsync(req))
            return await Json(req, HttpStatusCode.Forbidden, new { error = "Invalid or missing API key." });

        LogRewstRequest(req, operationName);

        var unwrap = await TryUnwrapPayloadJsonAsync(req);
        if (!unwrap.ok)
            return await Json(req, unwrap.code, unwrap.body!);

        var maxRequestBytes = ReadIntSetting("MAX_REQUEST_BYTES", 5_000_000);
        if (Encoding.UTF8.GetByteCount(unwrap.innerJson!) > maxRequestBytes)
        {
            return await Json(req, HttpStatusCode.BadRequest, new
            {
                valid = false,
                errors = new[]
                {
                    new ContractIssue
                    {
                        Code = "PAYLOAD_TOO_LARGE",
                        Message = $"Inner payload_json exceeds MAX_REQUEST_BYTES ({maxRequestBytes}).",
                        Path = "payload_json"
                    }
                },
                warnings = Array.Empty<ContractIssue>(),
                response_mode = "base64_json"
            });
        }

        try
        {
            var normalized = _normalizer.Normalize(unwrap.innerJson!, tier);
            return await Json(req, HttpStatusCode.OK, new
            {
                valid = normalized.Errors.Count == 0,
                errors = normalized.Errors,
                warnings = normalized.Warnings,
                response_mode = "base64_json"
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Rewst {Operation} validate failed", operationName);
            return await Json(req, HttpStatusCode.BadRequest, new
            {
                valid = false,
                errors = new[] { new ContractIssue { Code = "VALIDATION_PARSE_ERROR", Message = ex.Message, Path = "$" } },
                warnings = Array.Empty<ContractIssue>(),
                response_mode = "base64_json"
            });
        }
    }

    private async Task<HttpResponseData> RunRenderAsync(
        HttpRequestData req,
        ContractTierExpectation tier,
        string operationName)
    {
        if (!await AuthorizeAsync(req))
            return await Json(req, HttpStatusCode.Forbidden, new { error = "Invalid or missing API key." });

        LogRewstRequest(req, operationName);

        var unwrap = await TryUnwrapPayloadJsonAsync(req);
        if (!unwrap.ok)
            return await Json(req, unwrap.code, unwrap.body!);

        var maxRequestBytes = ReadIntSetting("MAX_REQUEST_BYTES", 5_000_000);
        if (Encoding.UTF8.GetByteCount(unwrap.innerJson!) > maxRequestBytes)
        {
            return await Json(req, HttpStatusCode.BadRequest, ValidationFailure(
                "PAYLOAD_TOO_LARGE",
                $"Inner payload_json exceeds MAX_REQUEST_BYTES ({maxRequestBytes}).",
                "payload_json"));
        }

        NormalizeResult normalized;
        try
        {
            normalized = _normalizer.Normalize(unwrap.innerJson!, tier);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Rewst {Operation} normalize failed", operationName);
            return await Json(req, HttpStatusCode.BadRequest, ValidationFailure("VALIDATION_PARSE_ERROR", ex.Message, "$"));
        }

        if (normalized.Errors.Count > 0)
        {
            return await Json(req, HttpStatusCode.BadRequest, new
            {
                valid = false,
                errors = normalized.Errors,
                warnings = normalized.Warnings,
                response_mode = "base64_json"
            });
        }

        var payload = normalized.Payload;
        if (payload.Workbook?.Worksheets is null || payload.Workbook.Worksheets.Count == 0)
        {
            return await Json(req, HttpStatusCode.BadRequest, new
            {
                valid = false,
                errors = new[]
                {
                    new ContractIssue
                    {
                        Code = "EMPTY_WORKBOOK",
                        Message = "Payload must include at least one worksheet.",
                        Path = "workbook.worksheets"
                    }
                },
                warnings = normalized.Warnings,
                response_mode = "base64_json"
            });
        }

        RenderOutput output;
        try
        {
            var defaultTheme = _config["DEFAULT_TABLE_THEME"];
            var maxRowsPerSheet = ReadIntSetting("MAX_ROWS_PER_SHEET", 20000);
            output = _renderer.Render(payload, defaultTheme, maxRowsPerSheet);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Rewst {Operation} render failed", operationName);
            return await Json(req, HttpStatusCode.BadRequest, new
            {
                valid = false,
                errors = new[]
                {
                    new ContractIssue { Code = "RENDER_FAILED", Message = ex.Message, Path = "$" }
                },
                warnings = normalized.Warnings,
                response_mode = "base64_json"
            });
        }

        var fileName = SanitizeFileName(payload.FileName ?? "report.xlsx");
        if (!fileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
            fileName += ".xlsx";

        return await Json(req, HttpStatusCode.OK, new
        {
            status = "ok",
            file_name = fileName,
            content_type = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            content_base64 = Convert.ToBase64String(output.Bytes),
            response_mode = "base64_json",
            warnings = normalized.Warnings,
            stats = output.Stats
        });
    }

    private void LogRewstRequest(HttpRequestData req, string operationName)
    {
        var correlationId = GetCorrelationId(req);
        if (string.IsNullOrEmpty(correlationId))
            _logger.LogInformation("Rewst request {Operation}", operationName);
        else
            _logger.LogInformation("Rewst request {Operation} correlation_id={CorrelationId}", operationName, correlationId);
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

    private async Task<(bool ok, HttpStatusCode code, object? body, string? innerJson)> TryUnwrapPayloadJsonAsync(HttpRequestData req)
    {
        string raw;
        try
        {
            raw = await new StreamReader(req.Body, Encoding.UTF8).ReadToEndAsync();
        }
        catch
        {
            return (false, HttpStatusCode.BadRequest, ValidationFailure("VALIDATION_PARSE_ERROR", "Request body could not be read."), null);
        }

        if (string.IsNullOrWhiteSpace(raw))
        {
            return (false, HttpStatusCode.BadRequest, ValidationFailure("VALIDATION_PARSE_ERROR", "Request body is empty."), null);
        }

        RewstRequest? wrapper;
        try
        {
            wrapper = JsonSerializer.Deserialize<RewstRequest>(raw, RewstDeserializeOptions);
        }
        catch (Exception ex)
        {
            return (false, HttpStatusCode.BadRequest, ValidationFailure("VALIDATION_PARSE_ERROR", "Outer JSON is invalid: " + ex.Message), null);
        }

        if (wrapper is null || string.IsNullOrWhiteSpace(wrapper.PayloadJson))
        {
            return (false, HttpStatusCode.BadRequest, ValidationFailure("VALIDATION_PARSE_ERROR", "Field payload_json is required and must be a non-empty string."), null);
        }

        return (true, default, null, wrapper.PayloadJson.Trim());
    }

    private static object ValidationFailure(string code, string message, string path = "payload_json") => new
    {
        valid = false,
        errors = new[] { new ContractIssue { Code = code, Message = message, Path = path } },
        warnings = Array.Empty<ContractIssue>(),
        response_mode = "base64_json"
    };

    private async Task<HttpResponseData> Json(HttpRequestData req, HttpStatusCode code, object obj)
    {
        var r = req.CreateResponse(code);
        r.Headers.Add("Content-Type", "application/json; charset=utf-8");
        await r.WriteStringAsync(JsonSerializer.Serialize(obj));
        return r;
    }

    private int ReadIntSetting(string name, int fallback) => int.TryParse(_config[name], out var v) ? v : fallback;

    private Task<bool> AuthorizeAsync(HttpRequestData req)
    {
        var expected = _config["RENDER_API_KEY"];
        if (string.IsNullOrEmpty(expected)) return Task.FromResult(true);
        if (req.Headers.TryGetValues("X-Api-Key", out var keys) && string.Equals(keys.FirstOrDefault(), expected, StringComparison.Ordinal)) return Task.FromResult(true);
        if (req.Headers.TryGetValues("Authorization", out var auths))
        {
            var v = auths.FirstOrDefault();
            if (!string.IsNullOrEmpty(v) && v.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) && string.Equals(v["Bearer ".Length..].Trim(), expected, StringComparison.Ordinal)) return Task.FromResult(true);
        }
        return Task.FromResult(false);
    }

    private static string SanitizeFileName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return string.IsNullOrWhiteSpace(name) ? "report.xlsx" : name.Trim();
    }
}
