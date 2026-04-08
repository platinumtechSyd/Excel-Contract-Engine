using System.Text.Json.Serialization;

namespace ExcelRenderer.Functions.Models;

/// <summary>Direct request body for SharePoint upload.</summary>
public sealed class SharePointUploadRequest
{
    [JsonPropertyName("site_id")]
    public string? SiteId { get; init; }

    [JsonPropertyName("folder_path")]
    public string? FolderPath { get; init; }

    [JsonPropertyName("file_name")]
    public string? FileName { get; init; }

    [JsonPropertyName("content_base64")]
    public string? ContentBase64 { get; init; }

    [JsonPropertyName("content_type")]
    public string? ContentType { get; init; }

    [JsonPropertyName("overwrite")]
    public bool Overwrite { get; init; } = true;
}
