using System.Text.Json;
using System.Text.Json.Serialization;

namespace ExcelRenderer.Functions.Models;

public sealed class RenderPayload
{
    [JsonPropertyName("schema_version")]
    public string? SchemaVersion { get; init; }

    [JsonPropertyName("report_name")]
    public string? ReportName { get; init; }

    [JsonPropertyName("file_name")]
    public string? FileName { get; init; }

    [JsonPropertyName("response_mode")]
    public string? ResponseMode { get; init; }

    [JsonPropertyName("delivery")]
    public DeliveryPayload? Delivery { get; init; }

    [JsonPropertyName("defaults")]
    public ContractDefaults? Defaults { get; init; }

    [JsonPropertyName("workbook")]
    public WorkbookPayload Workbook { get; init; } = null!;

    [JsonPropertyName("table_theme")]
    public string? TableTheme { get; init; }
}

public sealed class DeliveryPayload
{
    [JsonPropertyName("format")]
    public string? Format { get; init; }
}

public sealed class WorkbookPayload
{
    [JsonPropertyName("author")]
    public string? Author { get; init; }

    [JsonPropertyName("display_timezone")]
    public string? DisplayTimezone { get; init; }

    [JsonPropertyName("worksheets")]
    public List<WorksheetPayload> Worksheets { get; init; } = [];
}

public sealed class WorksheetPayload
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = "Sheet1";

    [JsonPropertyName("freeze_panes")]
    public string? FreezePanes { get; init; }

    [JsonPropertyName("blocks")]
    public List<BlockPayload> Blocks { get; init; } = [];
}

public sealed class BlockPayload
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = "table";

    [JsonPropertyName("start_cell")]
    public string StartCell { get; init; } = "A1";

    [JsonPropertyName("columns")]
    public List<ColumnPayload> Columns { get; init; } = [];

    [JsonPropertyName("rows")]
    public List<Dictionary<string, JsonElement>> Rows { get; init; } = [];

    [JsonPropertyName("table_theme")]
    public string? TableTheme { get; init; }

    [JsonPropertyName("conditional_formats")]
    public List<ConditionalFormatRulePayload>? ConditionalFormats { get; init; }

    [JsonPropertyName("row_rules")]
    public List<RowRulePayload>? RowRules { get; init; }
}

public sealed class RowRulePayload
{
    [JsonPropertyName("when")]
    public JsonElement When { get; init; }

    [JsonPropertyName("style")]
    public string Style { get; init; } = "warning";
}

public sealed class ColumnPayload
{
    [JsonPropertyName("key")]
    public string Key { get; init; } = "";

    [JsonPropertyName("header")]
    public string Header { get; init; } = "";

    [JsonPropertyName("type")]
    public string Type { get; init; } = "string";

    [JsonPropertyName("currency_code")]
    public string? CurrencyCode { get; init; }

    [JsonPropertyName("number_format")]
    public string? NumberFormat { get; init; }

    [JsonPropertyName("width")]
    public double? Width { get; init; }
}

public sealed class ConditionalFormatRulePayload
{
    [JsonPropertyName("column_key")]
    public string ColumnKey { get; init; } = "";

    [JsonPropertyName("op")]
    public string Op { get; init; } = "greater_than";

    [JsonPropertyName("value")]
    public JsonElement? Value { get; init; }

    [JsonPropertyName("value2")]
    public JsonElement? Value2 { get; init; }

    [JsonPropertyName("fill_color")]
    public string? FillColor { get; init; }
}

public sealed class ContractV2Payload
{
    [JsonPropertyName("schema_version")]
    public string? SchemaVersion { get; init; }

    [JsonPropertyName("file_name")]
    public string? FileName { get; init; }

    [JsonPropertyName("report_name")]
    public string? ReportName { get; init; }

    [JsonPropertyName("author")]
    public string? Author { get; init; }

    [JsonPropertyName("timezone")]
    public string? Timezone { get; init; }

    [JsonPropertyName("response_mode")]
    public string? ResponseMode { get; init; }

    [JsonPropertyName("delivery")]
    public DeliveryPayload? Delivery { get; init; }

    [JsonPropertyName("strict_mode")]
    public bool? StrictMode { get; init; }

    [JsonPropertyName("defaults")]
    public ContractDefaults? Defaults { get; init; }

    [JsonPropertyName("sources")]
    public Dictionary<string, SourcePayload>? Sources { get; init; }

    [JsonPropertyName("sheets")]
    public List<SheetV2Payload>? Sheets { get; init; }
}

public sealed class ContractDefaults
{
    [JsonPropertyName("freeze_header")]
    public bool? FreezeHeader { get; init; }

    [JsonPropertyName("null_display")]
    public string? NullDisplay { get; init; }

    [JsonPropertyName("date_format")]
    public string? DateFormat { get; init; }

    [JsonPropertyName("datetime_format")]
    public string? DateTimeFormat { get; init; }

    [JsonPropertyName("boolean_display")]
    public List<string>? BooleanDisplay { get; init; }
}

public sealed class SourcePayload
{
    [JsonPropertyName("data")]
    public JsonElement Data { get; init; }

    [JsonPropertyName("key")]
    public string? Key { get; init; }

    [JsonPropertyName("foreign_key")]
    public string? ForeignKey { get; init; }

    [JsonPropertyName("on_empty")]
    public string? OnEmpty { get; init; }

    [JsonPropertyName("on_null")]
    public string? OnNull { get; init; }
}

public sealed class SheetV2Payload
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = "Sheet1";

    [JsonPropertyName("data")]
    public JsonElement? Data { get; init; }

    [JsonPropertyName("primary_source")]
    public string? PrimarySource { get; init; }

    [JsonPropertyName("filter")]
    public JsonElement? Filter { get; init; }

    [JsonPropertyName("sort_by")]
    public List<string>? SortBy { get; init; }

    [JsonPropertyName("row_rules")]
    public List<RowRulePayload>? RowRules { get; init; }

    [JsonPropertyName("columns")]
    public Dictionary<string, ColumnV2Payload> Columns { get; init; } = [];
}

public sealed class ColumnV2Payload
{
    [JsonPropertyName("header")]
    public string? Header { get; init; }

    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonPropertyName("source")]
    public string? Source { get; init; }

    [JsonPropertyName("flatten")]
    public string? Flatten { get; init; }

    [JsonPropertyName("separator")]
    public string? Separator { get; init; }

    [JsonPropertyName("currency_code")]
    public string? CurrencyCode { get; init; }

    [JsonPropertyName("number_format")]
    public string? NumberFormat { get; init; }

    [JsonPropertyName("width")]
    public double? Width { get; init; }
}

public sealed class ContractIssue
{
    public required string Code { get; init; }
    public required string Message { get; init; }
    public string? Path { get; init; }
}

public sealed class NormalizeResult
{
    public required RenderPayload Payload { get; init; }
    public required string ResponseMode { get; init; }
    public List<ContractIssue> Warnings { get; init; } = [];
    public List<ContractIssue> Errors { get; init; } = [];
}

public sealed class RenderStats
{
    public int SheetCount { get; init; }
    public int BlockCount { get; init; }
    public int RowCount { get; init; }
}

public sealed class RenderOutput
{
    public required byte[] Bytes { get; init; }
    public required RenderStats Stats { get; init; }
}
