namespace ExcelRenderer.Functions.Models;

public sealed class SharePointUploadResult
{
    public bool Ok { get; init; }
    public string Status { get; init; } = "error";
    public string? WebUrl { get; init; }
    public string? Path { get; init; }
    public string? ItemId { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }

    public static SharePointUploadResult Fail(string code, string message) =>
        new()
        {
            Ok = false,
            Status = "error",
            ErrorCode = code,
            ErrorMessage = message
        };

    public static SharePointUploadResult Success(string? webUrl, string? path, string? itemId) =>
        new()
        {
            Ok = true,
            Status = "ok",
            WebUrl = webUrl,
            Path = path,
            ItemId = itemId
        };
}
