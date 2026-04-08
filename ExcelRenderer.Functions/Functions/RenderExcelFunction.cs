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

public sealed class RenderExcelFunction
{
    private readonly ExcelRenderService _renderer;
    private readonly ContractNormalizationService _normalizer;
    private readonly IConfiguration _config;
    private readonly ILogger<RenderExcelFunction> _logger;

    public RenderExcelFunction(
        ExcelRenderService renderer,
        ContractNormalizationService normalizer,
        IConfiguration config,
        ILogger<RenderExcelFunction> logger)
    {
        _renderer = renderer;
        _normalizer = normalizer;
        _config = config;
        _logger = logger;
    }

    [Function(nameof(RenderExcel))]
    public async Task<HttpResponseData> RenderExcel(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "render")] HttpRequestData req,
        FunctionContext _)
    {
        if (!await AuthorizeAsync(req))
            return await Text(req, HttpStatusCode.Forbidden, "Invalid or missing API key.");

        var body = await ReadBody(req);
        if (!body.ok)
            return await Text(req, HttpStatusCode.BadRequest, body.error!);

        var maxRequestBytes = ReadIntSetting("MAX_REQUEST_BYTES", 5_000_000);
        if (Encoding.UTF8.GetByteCount(body.json!) > maxRequestBytes)
            return await Text(req, HttpStatusCode.BadRequest, $"Request exceeds MAX_REQUEST_BYTES ({maxRequestBytes}).");

        NormalizeResult normalized;
        try
        {
            normalized = _normalizer.Normalize(body.json!);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Invalid contract");
            return await Text(req, HttpStatusCode.BadRequest, ex.Message);
        }

        if (normalized.Errors.Count > 0)
            return await Json(req, HttpStatusCode.BadRequest, new { valid = false, errors = normalized.Errors, warnings = normalized.Warnings });

        var payload = normalized.Payload;
        if (payload.Workbook?.Worksheets is null || payload.Workbook.Worksheets.Count == 0)
            return await Text(req, HttpStatusCode.BadRequest, "Payload must include at least one worksheet.");

        RenderOutput output;
        try
        {
            var defaultTheme = _config["DEFAULT_TABLE_THEME"];
            var maxRowsPerSheet = ReadIntSetting("MAX_ROWS_PER_SHEET", 20000);
            output = _renderer.Render(payload, defaultTheme, maxRowsPerSheet);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Render failed");
            return await Text(req, HttpStatusCode.BadRequest, ex.Message);
        }

        var fileName = SanitizeFileName(payload.FileName ?? "report.xlsx");
        if (!fileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
            fileName += ".xlsx";

        if (string.Equals(normalized.ResponseMode, "base64_json", StringComparison.OrdinalIgnoreCase))
        {
            return await Json(req, HttpStatusCode.OK, new
            {
                status = "ok",
                file_name = fileName,
                content_type = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                content_base64 = Convert.ToBase64String(output.Bytes),
                warnings = normalized.Warnings,
                stats = output.Stats
            });
        }

        var ok = req.CreateResponse(HttpStatusCode.OK);
        ok.Headers.Add("Content-Type", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        ok.Headers.Add("Content-Disposition", $"attachment; filename=\"{fileName}\"");
        if (normalized.Warnings.Count > 0)
            ok.Headers.Add("X-Render-Warnings", string.Join(" | ", normalized.Warnings.Take(3).Select(w => w.Code)));
        await ok.WriteBytesAsync(output.Bytes);
        return ok;
    }

    [Function(nameof(Validate))]
    public async Task<HttpResponseData> Validate(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "validate")] HttpRequestData req,
        FunctionContext _)
    {
        if (!await AuthorizeAsync(req))
            return await Text(req, HttpStatusCode.Forbidden, "Invalid or missing API key.");

        var body = await ReadBody(req);
        if (!body.ok)
            return await Text(req, HttpStatusCode.BadRequest, body.error!);

        try
        {
            var normalized = _normalizer.Normalize(body.json!);
            return await Json(req, HttpStatusCode.OK, new
            {
                valid = normalized.Errors.Count == 0,
                errors = normalized.Errors,
                warnings = normalized.Warnings,
                response_mode = normalized.ResponseMode
            });
        }
        catch (Exception ex)
        {
            return await Json(req, HttpStatusCode.BadRequest, new
            {
                valid = false,
                errors = new[] { new ContractIssue { Code = "VALIDATION_PARSE_ERROR", Message = ex.Message, Path = "$" } },
                warnings = Array.Empty<ContractIssue>()
            });
        }
    }

    [Function(nameof(Health))]
    public async Task<HttpResponseData> Health([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "health")] HttpRequestData req, FunctionContext _) => await Text(req, HttpStatusCode.OK, "ok");

    [Function(nameof(OpenApi))]
    public async Task<HttpResponseData> OpenApi([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "openapi.json")] HttpRequestData req, FunctionContext _)
    {
        var r = req.CreateResponse(HttpStatusCode.OK);
        r.Headers.Add("Content-Type", "application/json; charset=utf-8");
        await r.WriteStringAsync(OpenApiDocument.Json);
        return r;
    }

    private async Task<(bool ok, string? json, string? error)> ReadBody(HttpRequestData req)
    {
        try { return (true, await new StreamReader(req.Body, Encoding.UTF8).ReadToEndAsync(), null); }
        catch { return (false, null, "Request body could not be read."); }
    }

    private async Task<HttpResponseData> Text(HttpRequestData req, HttpStatusCode code, string text)
    {
        var r = req.CreateResponse(code);
        await r.WriteStringAsync(text);
        return r;
    }

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
