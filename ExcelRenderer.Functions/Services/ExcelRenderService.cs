using System.Globalization;
using System.Text.Json;
using ClosedXML.Excel;
using ExcelRenderer.Functions.Models;

namespace ExcelRenderer.Functions.Services;

public sealed class ExcelRenderService
{
    public RenderOutput Render(RenderPayload payload, string? defaultTableTheme, int maxRowsPerSheet)
    {
        if (payload.SchemaVersion is not null && payload.SchemaVersion != "1.0")
            throw new InvalidOperationException($"Unsupported schema_version: {payload.SchemaVersion}");

        using var workbook = new XLWorkbook();
        if (!string.IsNullOrWhiteSpace(payload.Workbook.Author))
            workbook.Properties.Author = payload.Workbook.Author;

        var theme = payload.TableTheme ?? defaultTableTheme ?? "TableStyleMedium2";
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var totalRows = 0;
        var totalBlocks = 0;
        foreach (var sheetModel in payload.Workbook.Worksheets)
        {
            var sheetName = UniqueSheetName(SanitizeSheetName(sheetModel.Name), usedNames);
            var ws = workbook.Worksheets.Add(sheetName);
            var sheetRows = 0;

            foreach (var block in sheetModel.Blocks)
            {
                if (!string.Equals(block.Type, "table", StringComparison.OrdinalIgnoreCase))
                    continue;
                totalBlocks++;
                sheetRows += block.Rows.Count;
                if (sheetRows > maxRowsPerSheet)
                    throw new InvalidOperationException($"Sheet '{sheetName}' exceeds max rows per sheet ({maxRowsPerSheet}).");

                var start = ParseCell(block.StartCell);
                var blockTheme = block.TableTheme ?? theme;
                WriteTable(ws, block, start, blockTheme, payload.Defaults);
            }
            totalRows += sheetRows;

            ApplyFreeze(ws, sheetModel.FreezePanes);
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return new RenderOutput
        {
            Bytes = stream.ToArray(),
            Stats = new RenderStats
            {
                SheetCount = payload.Workbook.Worksheets.Count,
                BlockCount = totalBlocks,
                RowCount = totalRows
            }
        };
    }

    private static void WriteTable(IXLWorksheet ws, BlockPayload block, (int col, int row) start, string tableThemeName, ContractDefaults? defaults)
    {
        var colCount = block.Columns.Count;
        if (colCount == 0)
            return;

        var headerRow = start.row;
        var dataStartRow = headerRow + 1;
        var lastRow = headerRow + block.Rows.Count;

        for (var c = 0; c < colCount; c++)
        {
            var colDef = block.Columns[c];
            var excelColumn = ws.Column(start.col + c);
            if (colDef.Width.HasValue && colDef.Width.Value > 0)
                excelColumn.Width = colDef.Width.Value;

            var cell = ws.Cell(headerRow, start.col + c);
            cell.Value = colDef.Header;
            cell.Style.Font.Bold = true;
        }

        for (var r = 0; r < block.Rows.Count; r++)
        {
            var rowDict = block.Rows[r];
            for (var c = 0; c < colCount; c++)
            {
                var colDef = block.Columns[c];
                var cell = ws.Cell(dataStartRow + r, start.col + c);
                if (!rowDict.TryGetValue(colDef.Key, out var el))
                {
                    cell.Clear(XLClearOptions.AllContents);
                    continue;
                }

                ApplyCellValue(cell, colDef, el, defaults);
            }
        }

        if (block.Rows.Count == 0)
            lastRow = headerRow;

        var range = ws.Range(headerRow, start.col, lastRow, start.col + colCount - 1);
        if (block.Rows.Count >= 0 && colCount > 0)
        {
            var table = range.CreateTable();
            table.Theme = ParseTableTheme(tableThemeName);
            table.ShowAutoFilter = true;
        }

        if (block.ConditionalFormats is { Count: > 0 })
            ApplyConditionalFormats(ws, block, start, headerRow, dataStartRow, lastRow);

        if (block.RowRules is { Count: > 0 })
            ApplyRowRules(ws, block, start, dataStartRow);

        for (var c = 0; c < colCount; c++)
        {
            var colDef = block.Columns[c];
            if (colDef.Width is { } w && w > 0)
                continue;
            AutosizeColumn(ws, start.col + c, colDef, headerRow, lastRow);
        }
    }

    /// <summary>
    /// ClosedXML's AdjustToContents often underestimates Excel's rendered width: formatted
    /// numbers show ####### when too narrow; text + bold headers + table filter glyphs look clipped
    /// (e.g. "Subscription") when the metric is short. We derive a floor from the longest formatted
    /// string in the column, then add padding.
    /// </summary>
    private static void AutosizeColumn(IXLWorksheet ws, int col, ColumnPayload colDef, int headerRow, int lastRow)
    {
        var maxDisplayLen = colDef.Header.Length;
        for (var r = headerRow; r <= lastRow; r++)
        {
            var formatted = ws.Cell(r, col).GetFormattedString(CultureInfo.InvariantCulture);
            if (formatted.Length > maxDisplayLen)
                maxDisplayLen = formatted.Length;
        }

        // ~Excel column width vs character count; +8 leaves room for AutoFilter dropdown on headers.
        var floorWidth = Math.Clamp(maxDisplayLen * 1.12 + 8.0, 12.0, 120.0);

        var column = ws.Column(col);
        column.AdjustToContents(headerRow, lastRow);
        var extra = FormattedValueColumn(colDef) ? 5.0 : 4.0;
        column.Width = Math.Min(Math.Max(column.Width + extra, floorWidth), 255);
    }

    private static bool FormattedValueColumn(ColumnPayload colDef)
    {
        return colDef.Type.ToLowerInvariant() switch
        {
            "number" or "integer" or "currency" or "date" or "datetime" => true,
            _ => false
        };
    }

    private static void ApplyConditionalFormats(
        IXLWorksheet ws,
        BlockPayload block,
        (int col, int row) start,
        int headerRow,
        int dataStartRow,
        int lastRow)
    {
        foreach (var rule in block.ConditionalFormats!)
        {
            var colIndex = block.Columns.FindIndex(x => x.Key == rule.ColumnKey);
            if (colIndex < 0)
                continue;

            if (lastRow < dataStartRow)
                continue;

            var colAddr = start.col + colIndex;
            var range = ws.Range(dataStartRow, colAddr, lastRow, colAddr);
            var cf = range.AddConditionalFormat();
            var fill = ParseFillColor(rule.FillColor);

            switch (rule.Op.ToLowerInvariant())
            {
                case "greater_than":
                    if (rule.Value is { } v1 && TryDouble(v1, out var d1))
                        cf.WhenGreaterThan(d1).Fill.SetBackgroundColor(fill);
                    break;
                case "less_than":
                    if (rule.Value is { } v2 && TryDouble(v2, out var d2))
                        cf.WhenLessThan(d2).Fill.SetBackgroundColor(fill);
                    break;
                case "equal":
                    if (rule.Value is { } v3 && TryDouble(v3, out var d3))
                        cf.WhenEquals(d3).Fill.SetBackgroundColor(fill);
                    break;
                case "between":
                    if (rule.Value is { } va && rule.Value2 is { } vb
                        && TryDouble(va, out var a) && TryDouble(vb, out var b))
                        cf.WhenBetween(a, b).Fill.SetBackgroundColor(fill);
                    break;
                default:
                    break;
            }
        }
    }

    private static XLColor ParseFillColor(string? fillColor)
    {
        if (string.IsNullOrWhiteSpace(fillColor))
            return XLColor.FromArgb(255, 255, 204, 204);

        fillColor = fillColor.Trim();
        if (fillColor.Equals("light_red", StringComparison.OrdinalIgnoreCase))
            return XLColor.FromArgb(255, 255, 204, 204);

        // RRGGBB or AARRGGBB hex without #
        var hex = fillColor.TrimStart('#');
        if (hex.Length == 6)
            hex = "FF" + hex;
        if (hex.Length == 8 && int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var argb))
            return XLColor.FromArgb(argb);

        return XLColor.FromArgb(255, 255, 204, 204);
    }

    private static bool TryDouble(JsonElement el, out double d)
    {
        return el.ValueKind switch
        {
            JsonValueKind.Number => el.TryGetDouble(out d),
            JsonValueKind.String => double.TryParse(el.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out d),
            _ => double.TryParse(el.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out d)
        };
    }

    private static void ApplyRowRules(IXLWorksheet ws, BlockPayload block, (int col, int row) start, int dataStartRow)
    {
        for (var r = 0; r < block.Rows.Count; r++)
        {
            var row = block.Rows[r];
            var excelRow = dataStartRow + r;
            foreach (var rule in block.RowRules!)
            {
                if (!RowMatches(rule.When, row))
                    continue;

                var fill = RuleStyleFill(rule.Style);
                var range = ws.Range(excelRow, start.col, excelRow, start.col + block.Columns.Count - 1);
                range.Style.Fill.BackgroundColor = fill;
                break;
            }
        }
    }

    private static bool RowMatches(JsonElement when, Dictionary<string, JsonElement> row)
    {
        if (when.ValueKind != JsonValueKind.Object)
            return false;

        foreach (var p in when.EnumerateObject())
        {
            if (!row.TryGetValue(p.Name, out var value))
                return false;

            if (p.Value.ValueKind == JsonValueKind.Object)
            {
                if (p.Value.TryGetProperty("older_than_days", out var daysEl) && daysEl.TryGetInt32(out var days))
                {
                    if (!IsOlderThanDays(value, days))
                        return false;
                    continue;
                }
            }

            if (!JsonElementEquals(value, p.Value))
                return false;
        }

        return true;
    }

    private static bool IsOlderThanDays(JsonElement value, int days)
    {
        if (value.ValueKind != JsonValueKind.String)
            return false;
        if (!DateTime.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dt))
            return false;
        return dt.Date < DateTime.UtcNow.Date.AddDays(-days);
    }

    private static bool JsonElementEquals(JsonElement a, JsonElement b)
    {
        if (a.ValueKind == JsonValueKind.Null || b.ValueKind == JsonValueKind.Null)
            return a.ValueKind == b.ValueKind;
        return string.Equals(a.ToString(), b.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private static XLColor RuleStyleFill(string style)
    {
        return style.ToLowerInvariant() switch
        {
            "danger" => XLColor.FromArgb(255, 255, 199, 206),
            "warning" => XLColor.FromArgb(255, 255, 235, 156),
            "success" => XLColor.FromArgb(255, 198, 239, 206),
            _ => XLColor.FromArgb(255, 255, 235, 156)
        };
    }

    private static void ApplyCellValue(IXLCell cell, ColumnPayload col, JsonElement el, ContractDefaults? defaults)
    {
        var t = col.Type.ToLowerInvariant();
        switch (t)
        {
            case "string":
                cell.SetValue(el.ValueKind == JsonValueKind.Null ? "" : el.ToString());
                break;
            case "number":
            case "integer":
                if (el.ValueKind == JsonValueKind.Null)
                    cell.Clear();
                else if (el.TryGetDouble(out var num))
                    cell.Value = num;
                else
                    cell.SetValue(el.ToString());
                if (!string.IsNullOrEmpty(col.NumberFormat))
                    cell.Style.NumberFormat.Format = col.NumberFormat!;
                break;
            case "currency":
                if (el.ValueKind == JsonValueKind.Null)
                    cell.Clear();
                else if (el.TryGetDouble(out var cur))
                    cell.Value = cur;
                else
                    cell.SetValue(el.ToString());
                cell.Style.NumberFormat.Format = CurrencyFormat(col.CurrencyCode);
                break;
            case "date":
            case "datetime":
                if (el.ValueKind == JsonValueKind.Null)
                    cell.Clear();
                else
                {
                    var dt = ParseDate(el);
                    if (dt.HasValue)
                    {
                        cell.Value = dt.Value;
                        var fallback = t == "datetime" ? (defaults?.DateTimeFormat ?? "dd/mm/yyyy hh:mm") : (defaults?.DateFormat ?? "dd/mm/yyyy");
                        cell.Style.DateFormat.Format = col.NumberFormat ?? fallback;
                    }
                    else
                        cell.SetValue(el.ToString());
                }
                break;
            case "boolean":
                var trueLabel = defaults?.BooleanDisplay is { Count: > 0 } ? defaults.BooleanDisplay[0] : "true";
                var falseLabel = defaults?.BooleanDisplay is { Count: > 1 } ? defaults.BooleanDisplay[1] : "false";
                var boolVal = el.ValueKind == JsonValueKind.True || (el.ValueKind == JsonValueKind.String &&
                    bool.TryParse(el.GetString(), out var b) && b);
                cell.Value = boolVal ? trueLabel : falseLabel;
                break;
            default:
                cell.SetValue(el.ToString());
                break;
        }
    }

    private static DateTime? ParseDate(JsonElement el)
    {
        if (el.ValueKind == JsonValueKind.String)
        {
            var s = el.GetString();
            if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var u))
                return DateTime.SpecifyKind(u, DateTimeKind.Unspecified);
        }
        return null;
    }

    private static string CurrencyFormat(string? code)
    {
        return code?.ToUpperInvariant() switch
        {
            "USD" => "$#,##0.00",
            "AUD" => "$#,##0.00",
            "EUR" => "€#,##0.00",
            "GBP" => "£#,##0.00",
            _ => "$#,##0.00"
        };
    }

    private static XLTableTheme ParseTableTheme(string name)
    {
        try
        {
            return XLTableTheme.FromName(name) ?? XLTableTheme.TableStyleMedium2;
        }
        catch
        {
            return XLTableTheme.TableStyleMedium2;
        }
    }

    private static void ApplyFreeze(IXLWorksheet ws, string? freezePanes)
    {
        if (string.IsNullOrWhiteSpace(freezePanes))
            return;

        var (col, row) = ParseCell(freezePanes);
        var freezeRows = Math.Max(0, row - 1);
        var freezeCols = Math.Max(0, col - 1);
        if (freezeRows > 0)
            ws.SheetView.FreezeRows(freezeRows);
        if (freezeCols > 0)
            ws.SheetView.FreezeColumns(freezeCols);
    }

    /// <summary>1-based column and row (Excel A1).</summary>
    internal static (int col, int row) ParseCell(string a1)
    {
        a1 = a1.Trim().ToUpperInvariant();
        var i = 0;
        while (i < a1.Length && a1[i] is >= 'A' and <= 'Z')
            i++;
        if (i == 0 || i >= a1.Length)
            throw new FormatException($"Invalid cell address: {a1}");

        var letters = a1[..i];
        if (!int.TryParse(a1[i..], NumberStyles.None, CultureInfo.InvariantCulture, out var row) || row < 1)
            throw new FormatException($"Invalid cell address: {a1}");

        var col = 0;
        foreach (var ch in letters)
            col = col * 26 + (ch - 'A' + 1);

        return (col, row);
    }

    private static string SanitizeSheetName(string name)
    {
        var invalid = new[] { '\\', '/', '*', '?', ':', '[', ']' };
        var s = name;
        foreach (var c in invalid)
            s = s.Replace(c, '_');
        s = s.Trim();
        if (string.IsNullOrEmpty(s))
            s = "Sheet";
        if (s.Length > 31)
            s = s[..31];
        return s;
    }

    private static string UniqueSheetName(string baseName, HashSet<string> used)
    {
        var name = baseName;
        var n = 2;
        while (used.Contains(name))
        {
            var suffix = $" ({n})";
            var max = 31 - suffix.Length;
            var stem = baseName.Length <= max ? baseName : baseName[..max];
            name = stem + suffix;
            n++;
        }
        used.Add(name);
        return name;
    }
}

