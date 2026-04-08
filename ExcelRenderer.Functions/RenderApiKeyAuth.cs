using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;

namespace ExcelRenderer.Functions;

/// <summary>
/// Validates <c>X-Api-Key</c> or <c>Authorization: Bearer</c> against <c>RENDER_API_KEY</c>.
/// The app setting must be non-empty; there is no anonymous mode for protected routes.
/// </summary>
public enum RenderApiKeyAuthResult
{
    Ok,
    /// <summary><c>RENDER_API_KEY</c> is missing or whitespace in configuration.</summary>
    MissingServerKey,
    /// <summary>Client did not send a matching key.</summary>
    MissingOrInvalidClientKey
}

public static class RenderApiKeyAuth
{
    public static RenderApiKeyAuthResult Validate(IConfiguration config, HttpRequestData req)
    {
        var expected = config["RENDER_API_KEY"];
        string? xApiKey = null;
        if (req.Headers.TryGetValues("X-Api-Key", out var keys))
            xApiKey = keys.FirstOrDefault();

        string? bearerToken = null;
        if (req.Headers.TryGetValues("Authorization", out var auths))
        {
            var v = auths.FirstOrDefault();
            if (!string.IsNullOrEmpty(v) && v.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                bearerToken = v["Bearer ".Length..].Trim();
        }

        return ValidateCredentials(expected, xApiKey, bearerToken);
    }

    /// <summary>
    /// Validates credentials without HTTP types (used by unit tests).
    /// </summary>
    public static RenderApiKeyAuthResult ValidateCredentials(string? configuredKey, string? xApiKey, string? bearerToken)
    {
        if (string.IsNullOrWhiteSpace(configuredKey))
            return RenderApiKeyAuthResult.MissingServerKey;

        if (string.Equals(xApiKey, configuredKey, StringComparison.Ordinal))
            return RenderApiKeyAuthResult.Ok;

        if (string.Equals(bearerToken, configuredKey, StringComparison.Ordinal))
            return RenderApiKeyAuthResult.Ok;

        return RenderApiKeyAuthResult.MissingOrInvalidClientKey;
    }
}
