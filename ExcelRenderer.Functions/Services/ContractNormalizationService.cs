using System.Globalization;
using System.Text.Json;
using ExcelRenderer.Functions.Models;

namespace ExcelRenderer.Functions.Services;

public sealed class ContractNormalizationService
{
    public NormalizeResult Normalize(string json) => Normalize(json, ContractTierExpectation.Any);

    public NormalizeResult Normalize(string json, ContractTierExpectation expectation)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            return InvalidResult(
                "$",
                "VALIDATION_PARSE_ERROR",
                "Invalid JSON: " + ex.Message);
        }

        using (doc)
        {
            var root = doc.RootElement;

            if (expectation == ContractTierExpectation.Tier1Workbook)
            {
                if (!root.TryGetProperty("workbook", out var wbEl)
                    || wbEl.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                {
                    return InvalidResult(
                        "$",
                        "TIER_MISMATCH_TIER1_EXPECT_WORKBOOK",
                        "This endpoint expects tier 1: the JSON root must include a non-null \"workbook\" object.");
                }
            }

            if (expectation == ContractTierExpectation.Tier2Sheets)
            {
                if (!root.TryGetProperty("sheets", out var shEl) || shEl.ValueKind != JsonValueKind.Array)
                {
                    return InvalidResult(
                        "$",
                        "TIER_MISMATCH_TIER2_EXPECT_SHEETS",
                        "This endpoint expects tier 2: the JSON root must include a \"sheets\" array.");
                }
            }

            var useTier1 = expectation switch
            {
                ContractTierExpectation.Tier1Workbook => true,
                ContractTierExpectation.Tier2Sheets => false,
                _ => root.TryGetProperty("workbook", out _)
            };

            if (useTier1)
            {
                var v1 = JsonSerializer.Deserialize<RenderPayload>(json, JsonOptions());
                if (v1 is null)
                {
                    return InvalidResult(
                        "$",
                        "VALIDATION_PARSE_ERROR",
                        "Unable to deserialize tier 1 payload (workbook contract).");
                }

                var tier1Errors = new List<ContractIssue>();
                ValidateTier1(v1, tier1Errors);
                var mode = ResolveResponseMode(v1.ResponseMode, v1.Delivery?.Format);
                return new NormalizeResult { Payload = v1, ResponseMode = mode, Errors = tier1Errors };
            }

            if (expectation == ContractTierExpectation.Any && !root.TryGetProperty("sheets", out _))
            {
                return InvalidResult(
                    "$",
                    "CONTRACT_ROOT_INVALID",
                    "Payload must include either 'workbook' (tier 1) or 'sheets' (tier 2).");
            }

            var v2 = JsonSerializer.Deserialize<ContractV2Payload>(json, JsonOptions());
            if (v2 is null)
            {
                return InvalidResult(
                    "$",
                    "VALIDATION_PARSE_ERROR",
                    "Unable to deserialize tier 2 payload.");
            }

            var strict = v2.StrictMode == true;
            var warnings = new List<ContractIssue>();
            var errors = new List<ContractIssue>();

            if (v2.Sheets is null || v2.Sheets.Count == 0)
            {
                errors.Add(new ContractIssue
                {
                    Code = "EMPTY_SHEETS",
                    Message = "Tier 2 payload requires at least one entry in sheets.",
                    Path = "sheets"
                });
                var shell = EmptyTier2Shell(v2);
                var mode = ResolveResponseMode(v2.ResponseMode, v2.Delivery?.Format);
                return new NormalizeResult { Payload = shell, ResponseMode = mode, Warnings = warnings, Errors = errors };
            }

            var payload = ToV1(v2, strict, warnings, errors);
            var responseMode = ResolveResponseMode(v2.ResponseMode, v2.Delivery?.Format);

            return new NormalizeResult { Payload = payload, ResponseMode = responseMode, Warnings = warnings, Errors = errors };
        }
    }

    private static NormalizeResult InvalidResult(string path, string code, string message) =>
        new()
        {
            Payload = EmptyTier1Shell(),
            ResponseMode = "binary",
            Errors =
            [
                new ContractIssue { Code = code, Message = message, Path = path }
            ]
        };

    private static RenderPayload EmptyTier1Shell() =>
        new()
        {
            SchemaVersion = "1.0",
            Workbook = new WorkbookPayload { Worksheets = [] }
        };

    private static RenderPayload EmptyTier2Shell(ContractV2Payload v2) =>
        new()
        {
            SchemaVersion = v2.SchemaVersion ?? "1.0",
            FileName = v2.FileName,
            ReportName = v2.ReportName,
            ResponseMode = v2.ResponseMode,
            Delivery = v2.Delivery,
            Defaults = v2.Defaults,
            Workbook = new WorkbookPayload
            {
                Author = v2.Author,
                DisplayTimezone = v2.Timezone,
                Worksheets = []
            }
        };

    /// <summary>Structural checks for tier 1 (embedded workbook contract). Paths are relative to the inner JSON root.</summary>
    private static void ValidateTier1(RenderPayload v1, List<ContractIssue> errors)
    {
        if (v1.SchemaVersion is not null && v1.SchemaVersion != "1.0")
        {
            errors.Add(new ContractIssue
            {
                Code = "UNSUPPORTED_SCHEMA_VERSION",
                Message = $"Only schema_version \"1.0\" is supported (got \"{v1.SchemaVersion}\").",
                Path = "schema_version"
            });
        }

        if (v1.Workbook is null)
        {
            errors.Add(new ContractIssue
            {
                Code = "WORKBOOK_MISSING",
                Message = "Tier 1 payload requires a workbook object.",
                Path = "workbook"
            });
            return;
        }

        var wss = v1.Workbook.Worksheets;
        if (wss is null || wss.Count == 0)
        {
            errors.Add(new ContractIssue
            {
                Code = "EMPTY_WORKBOOK",
                Message = "workbook.worksheets must contain at least one worksheet.",
                Path = "workbook.worksheets"
            });
            return;
        }

        for (var i = 0; i < wss.Count; i++)
        {
            var ws = wss[i];
            var blocks = ws.Blocks;
            if (blocks is null || blocks.Count == 0)
            {
                errors.Add(new ContractIssue
                {
                    Code = "WORKSHEET_NO_BLOCKS",
                    Message = $"Worksheet '{ws.Name}' has no blocks.",
                    Path = $"workbook.worksheets[{i}].blocks"
                });
                continue;
            }

            for (var b = 0; b < blocks.Count; b++)
            {
                var block = blocks[b];
                if (!string.Equals(block.Type, "table", StringComparison.OrdinalIgnoreCase))
                    continue;

                var basePath = $"workbook.worksheets[{i}].blocks[{b}]";
                if (!string.IsNullOrWhiteSpace(block.StartCell))
                {
                    try
                    {
                        ExcelRenderService.ParseCell(block.StartCell);
                    }
                    catch (FormatException)
                    {
                        errors.Add(new ContractIssue
                        {
                            Code = "INVALID_START_CELL",
                            Message = $"Invalid table start_cell address '{block.StartCell}' (expected Excel A1 notation).",
                            Path = $"{basePath}.start_cell"
                        });
                    }
                }

                if (block.Columns is null || block.Columns.Count == 0)
                {
                    errors.Add(new ContractIssue
                    {
                        Code = "TABLE_NO_COLUMNS",
                        Message = "Table block requires at least one column definition.",
                        Path = $"{basePath}.columns"
                    });
                    continue;
                }

                var colKeys = new HashSet<string>(block.Columns.Select(c => c.Key), StringComparer.OrdinalIgnoreCase);

                if (block.ConditionalFormats is { Count: > 0 })
                {
                    for (var r = 0; r < block.ConditionalFormats.Count; r++)
                    {
                        var rule = block.ConditionalFormats[r];
                        if (string.IsNullOrWhiteSpace(rule.ColumnKey) || !colKeys.Contains(rule.ColumnKey))
                        {
                            errors.Add(new ContractIssue
                            {
                                Code = "CF_UNKNOWN_COLUMN",
                                Message =
                                    $"conditional_formats[{r}] column_key '{rule.ColumnKey}' does not match any column key on this table.",
                                Path = $"{basePath}.conditional_formats[{r}].column_key"
                            });
                        }
                    }
                }

                if (block.RowRules is { Count: > 0 })
                {
                    for (var rr = 0; rr < block.RowRules.Count; rr++)
                    {
                        var rule = block.RowRules[rr];
                        if (rule.When.ValueKind != JsonValueKind.Object)
                        {
                            errors.Add(new ContractIssue
                            {
                                Code = "ROW_RULE_WHEN_INVALID",
                                Message = "row_rules[].when must be a JSON object whose keys are column keys.",
                                Path = $"{basePath}.row_rules[{rr}].when"
                            });
                            continue;
                        }

                        foreach (var p in rule.When.EnumerateObject())
                        {
                            if (!colKeys.Contains(p.Name))
                            {
                                errors.Add(new ContractIssue
                                {
                                    Code = "ROW_RULE_UNKNOWN_COLUMN",
                                    Message =
                                        $"row_rules[{rr}].when references unknown column '{p.Name}'.",
                                    Path = $"{basePath}.row_rules[{rr}].when.{p.Name}"
                                });
                            }
                        }
                    }
                }
            }
        }
    }

    private static RenderPayload ToV1(ContractV2Payload v2, bool strict, List<ContractIssue> warnings, List<ContractIssue> errors)
    {
        var sources = v2.Sources ?? new Dictionary<string, SourcePayload>(StringComparer.OrdinalIgnoreCase);
        var worksheets = new List<WorksheetPayload>();

        for (var i = 0; i < v2.Sheets!.Count; i++)
        {
            var sheet = v2.Sheets![i];
            var rows = ResolveSheetRows(sheet, i, sources, strict, warnings, errors);
            var columns = BuildColumns(sheet.Columns, i, sources, warnings);
            var dataRows = BuildRowDictionaries(rows, sheet, i, sources, v2.Defaults?.NullDisplay, strict, warnings, errors, columns.Select(c => c.Key).ToHashSet(StringComparer.OrdinalIgnoreCase));

            worksheets.Add(new WorksheetPayload
            {
                Name = sheet.Name,
                FreezePanes = v2.Defaults?.FreezeHeader == true ? "A2" : null,
                Blocks =
                [
                    new BlockPayload
                    {
                        Type = "table",
                        StartCell = "A1",
                        Columns = columns,
                        Rows = dataRows,
                        RowRules = sheet.RowRules
                    }
                ]
            });
        }

        return new RenderPayload
        {
            SchemaVersion = v2.SchemaVersion ?? "1.0",
            FileName = v2.FileName,
            ReportName = v2.ReportName,
            ResponseMode = v2.ResponseMode,
            Delivery = v2.Delivery,
            Defaults = v2.Defaults,
            Workbook = new WorkbookPayload
            {
                Author = v2.Author,
                DisplayTimezone = v2.Timezone,
                Worksheets = worksheets
            }
        };
    }

    private static List<ColumnPayload> BuildColumns(
        Dictionary<string, ColumnV2Payload> columns,
        int sheetIndex,
        Dictionary<string, SourcePayload> sources,
        List<ContractIssue> warnings)
    {
        var result = new List<ColumnPayload>();
        foreach (var kvp in columns)
        {
            if (!string.IsNullOrWhiteSpace(kvp.Value.Source)
                && sources.TryGetValue(kvp.Value.Source, out var src)
                && ShouldOmitColumn(src))
            {
                warnings.Add(new ContractIssue
                {
                    Code = "COL_OMITTED_EMPTY_SOURCE",
                    Path = $"sheets[{sheetIndex}].columns.{kvp.Key}",
                    Message = $"Column omitted because source '{kvp.Value.Source}' is empty/null and on_empty=omit_columns."
                });
                continue;
            }

            result.Add(new ColumnPayload
            {
                Key = kvp.Key,
                Header = string.IsNullOrWhiteSpace(kvp.Value.Header) ? kvp.Key : kvp.Value.Header!,
                Type = string.IsNullOrWhiteSpace(kvp.Value.Type) ? "string" : kvp.Value.Type!,
                CurrencyCode = kvp.Value.CurrencyCode,
                NumberFormat = kvp.Value.NumberFormat,
                Width = kvp.Value.Width
            });
        }
        return result;
    }

    private static bool ShouldOmitColumn(SourcePayload source)
    {
        var policy = source.OnEmpty?.ToLowerInvariant();
        if (policy != "omit_columns")
            return false;

        if (source.Data.ValueKind == JsonValueKind.Null || source.Data.ValueKind == JsonValueKind.Undefined)
            return true;

        return source.Data.ValueKind == JsonValueKind.Array && source.Data.GetArrayLength() == 0;
    }

    private static List<Dictionary<string, JsonElement>> BuildRowDictionaries(
        List<Dictionary<string, JsonElement>> baseRows,
        SheetV2Payload sheet,
        int sheetIndex,
        Dictionary<string, SourcePayload> sources,
        string? nullDisplay,
        bool strict,
        List<ContractIssue> warnings,
        List<ContractIssue> errors,
        HashSet<string> allowedColumns)
    {
        var output = new List<Dictionary<string, JsonElement>>(baseRows.Count);
        foreach (var baseRow in baseRows)
        {
            var outRow = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
            foreach (var col in sheet.Columns)
            {
                if (!allowedColumns.Contains(col.Key))
                    continue;

                var value = ResolveColumnValue(baseRow, col.Key, col.Value, sheetIndex, sheet, sources, nullDisplay, strict, warnings, errors);
                outRow[col.Key] = value;
            }
            output.Add(outRow);
        }

        return output;
    }

    private static JsonElement ResolveColumnValue(
        Dictionary<string, JsonElement> baseRow,
        string path,
        ColumnV2Payload meta,
        int sheetIndex,
        SheetV2Payload sheet,
        Dictionary<string, SourcePayload> sources,
        string? nullDisplay,
        bool strict,
        List<ContractIssue> warnings,
        List<ContractIssue> errors)
    {
        if (string.IsNullOrWhiteSpace(meta.Source))
            return NormalizeNullValue(GetPath(baseRow, path), nullDisplay);

        if (!sources.TryGetValue(meta.Source, out var source))
        {
            AddIssue(strict, warnings, errors, "SRC_NOT_FOUND", $"Source '{meta.Source}' not found.", $"sheets[{sheetIndex}].columns.{path}");
            return JsonSerializer.SerializeToElement<object?>(nullDisplay);
        }

        var sourceRows = ParseRows(source.Data);
        var primarySource = sheet.PrimarySource is not null && sources.TryGetValue(sheet.PrimarySource, out var p) ? p : null;
        var baseKeyPath = source.ForeignKey ?? source.Key ?? primarySource?.Key;
        var sourceKeyPath = source.Key;
        if (string.IsNullOrWhiteSpace(baseKeyPath) || string.IsNullOrWhiteSpace(sourceKeyPath))
        {
            AddIssue(strict, warnings, errors, "JOIN_KEY_MISSING", $"Could not resolve join keys for source '{meta.Source}'.", $"sheets[{sheetIndex}].columns.{path}");
            return JsonSerializer.SerializeToElement<object?>(nullDisplay);
        }

        var baseKeyValue = GetPath(baseRow, baseKeyPath!);
        var matches = sourceRows.Where(r => JsonEquals(GetPath(r, sourceKeyPath!), baseKeyValue)).ToList();
        if (matches.Count == 0)
            return JsonSerializer.SerializeToElement<object?>(nullDisplay);

        var innerPath = path.StartsWith(meta.Source + ".", StringComparison.OrdinalIgnoreCase)
            ? path[(meta.Source.Length + 1)..]
            : path;

        if (string.Equals(meta.Flatten, "join", StringComparison.OrdinalIgnoreCase))
        {
            var sep = meta.Separator ?? ", ";
            var text = string.Join(sep, matches.Select(m => ElementToString(GetPath(m, innerPath))).Where(s => !string.IsNullOrWhiteSpace(s)));
            return JsonSerializer.SerializeToElement(string.IsNullOrWhiteSpace(text) ? nullDisplay : text);
        }

        return NormalizeNullValue(GetPath(matches[0], innerPath), nullDisplay);
    }

    private static List<Dictionary<string, JsonElement>> ResolveSheetRows(
        SheetV2Payload sheet,
        int sheetIndex,
        Dictionary<string, SourcePayload> sources,
        bool strict,
        List<ContractIssue> warnings,
        List<ContractIssue> errors)
    {
        List<Dictionary<string, JsonElement>> rows;

        if (sheet.Data.HasValue && sheet.Data.Value.ValueKind == JsonValueKind.Array)
        {
            rows = ParseRows(sheet.Data.Value);
        }
        else
        {
            if (string.IsNullOrWhiteSpace(sheet.PrimarySource))
            {
                errors.Add(new ContractIssue
                {
                    Code = "SHEET_SOURCE_REQUIRED",
                    Message =
                        $"Sheet '{sheet.Name}' requires 'data' (JSON array of row objects) or 'primary_source' (source id).",
                    Path = $"sheets[{sheetIndex}]"
                });
                return [];
            }

            if (!sources.TryGetValue(sheet.PrimarySource, out var source))
            {
                AddIssue(strict, warnings, errors, "PRIMARY_SOURCE_NOT_FOUND", $"Primary source '{sheet.PrimarySource}' not found.", $"sheets[{sheetIndex}].primary_source");
                return [];
            }
            rows = ParseRows(source.Data);
        }

        rows = ApplyFilter(rows, sheet.Filter);
        rows = ApplySort(rows, sheet.SortBy);

        return rows;
    }

    private static void AddIssue(bool strict, List<ContractIssue> warnings, List<ContractIssue> errors, string code, string message, string path)
    {
        var issue = new ContractIssue { Code = code, Message = message, Path = path };
        if (strict) errors.Add(issue); else warnings.Add(issue);
    }

    private static JsonElement NormalizeNullValue(JsonElement value, string? nullDisplay)
    {
        if (value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return JsonSerializer.SerializeToElement<object?>(nullDisplay);
        return value;
    }

    private static string ResolveResponseMode(string? responseMode, string? deliveryFormat)
    {
        var mode = (deliveryFormat ?? responseMode ?? "binary").ToLowerInvariant();
        return mode is "base64" or "base64_json" ? "base64_json" : "binary";
    }

    private static List<Dictionary<string, JsonElement>> ApplyFilter(List<Dictionary<string, JsonElement>> rows, JsonElement? filter)
    {
        if (!filter.HasValue || filter.Value.ValueKind != JsonValueKind.Object)
            return rows;

        return rows.Where(row =>
        {
            foreach (var p in filter.Value.EnumerateObject())
            {
                if (!JsonEquals(GetPath(row, p.Name), p.Value))
                    return false;
            }
            return true;
        }).ToList();
    }

    private static List<Dictionary<string, JsonElement>> ApplySort(List<Dictionary<string, JsonElement>> rows, List<string>? sortBy)
    {
        if (sortBy is null || sortBy.Count == 0)
            return rows;

        IOrderedEnumerable<Dictionary<string, JsonElement>>? ordered = null;
        for (var i = 0; i < sortBy.Count; i++)
        {
            var key = sortBy[i];
            if (i == 0)
                ordered = rows.OrderBy(r => ElementToString(GetPath(r, key)), StringComparer.OrdinalIgnoreCase);
            else
                ordered = ordered!.ThenBy(r => ElementToString(GetPath(r, key)), StringComparer.OrdinalIgnoreCase);
        }

        return ordered?.ToList() ?? rows;
    }

    private static List<Dictionary<string, JsonElement>> ParseRows(JsonElement data)
    {
        var rows = new List<Dictionary<string, JsonElement>>();
        if (data.ValueKind != JsonValueKind.Array)
            return rows;

        foreach (var row in data.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Object)
                continue;
            var dict = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in row.EnumerateObject())
                dict[p.Name] = p.Value.Clone();
            rows.Add(dict);
        }

        return rows;
    }

    private static JsonElement GetPath(Dictionary<string, JsonElement> row, string path)
    {
        var segments = path.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
            return JsonSerializer.SerializeToElement<object?>(null);

        if (!row.TryGetValue(segments[0], out var current))
            return JsonSerializer.SerializeToElement<object?>(null);

        for (var i = 1; i < segments.Length; i++)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segments[i], out current))
                return JsonSerializer.SerializeToElement<object?>(null);
        }

        return current;
    }

    private static bool JsonEquals(JsonElement a, JsonElement b)
    {
        if (a.ValueKind == JsonValueKind.Null || b.ValueKind == JsonValueKind.Null)
            return a.ValueKind == b.ValueKind;
        return string.Equals(ElementToString(a), ElementToString(b), StringComparison.OrdinalIgnoreCase);
    }

    private static string ElementToString(JsonElement el)
    {
        return el.ValueKind switch
        {
            JsonValueKind.String => el.GetString() ?? string.Empty,
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Number => el.ToString(),
            JsonValueKind.Null => string.Empty,
            JsonValueKind.Undefined => string.Empty,
            _ => el.ToString()
        };
    }

    private static JsonSerializerOptions JsonOptions() => new() { PropertyNameCaseInsensitive = true };
}
