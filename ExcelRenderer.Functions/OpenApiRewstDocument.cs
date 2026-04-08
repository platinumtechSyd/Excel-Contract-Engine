namespace ExcelRenderer.Functions;

/// <summary>Loads Rewst OpenAPI from <c>openapi-rewst.json</c> copied to the build output (synthetic examples, safe to share).</summary>
internal static class OpenApiRewstDocument
{
    private static readonly Lazy<string> JsonLazy = new(Load);

    internal static string Json => JsonLazy.Value;

    private static string Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "openapi-rewst.json");
        if (File.Exists(path))
            return File.ReadAllText(path);

        throw new InvalidOperationException(
            "openapi-rewst.json was not found next to the application binaries. " +
            "Ensure the project copies it to the output directory (see ExcelRenderer.Functions.csproj).");
    }
}
