using System.Text.Json.Serialization;

namespace ExcelRenderer.Functions.Models;

public sealed class RewstRequest
{
    [JsonPropertyName("payload_json")]
    public string? PayloadJson { get; init; }
}
