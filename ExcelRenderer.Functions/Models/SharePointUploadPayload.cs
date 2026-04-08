using System.Text.Json.Serialization;

namespace ExcelRenderer.Functions.Models;

/// <summary>Inner JSON for SharePoint upload (inside Rewst <c>payload_json</c> string).</summary>
public sealed class SharePointUploadPayload
{
    [JsonPropertyName("site_id")]
    public string? SiteId { get; init; }

    [JsonPropertyName("site_url")]
    public string? SiteUrl { get; init; }

    [JsonPropertyName("drive_id")]
    public string? DriveId { get; init; }

    [JsonPropertyName("library_name")]
    public string? LibraryName { get; init; }

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
