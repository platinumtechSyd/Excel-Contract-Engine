using System.Text.Json;
using ExcelRenderer.Functions.Models;
using ExcelRenderer.Functions.Services;
using Xunit;

namespace ExcelRenderer.Functions.Tests;

public sealed class ExcelRenderServiceTests
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private static ExcelRenderService CreateService() => new();

    // Mirrors the failing payload shape: string "equal" conditional format
    // (e.g. Enabled = "No", Quota Status = "Healthy") must not throw RENDER_FAILED.
    [Fact]
    public void Render_StringEqualConditionalFormat_DoesNotThrow()
    {
        var json = """
        {
          "file_name": "test.xlsx",
          "response_mode": "base64_json",
          "schema_version": "1.0",
          "workbook": {
            "author": "Test",
            "worksheets": [
              {
                "name": "Users",
                "freeze_panes": "A2",
                "blocks": [
                  {
                    "type": "table",
                    "start_cell": "A1",
                    "columns": [
                      { "header": "Name",    "key": "Name",    "type": "string" },
                      { "header": "Enabled", "key": "Enabled", "type": "string" }
                    ],
                    "conditional_formats": [
                      { "column_key": "Enabled", "fill_color": "#FFC7CE", "op": "equal", "value": "No" }
                    ],
                    "row_rules": [
                      { "style": "warning", "when": { "Enabled": "No" } }
                    ],
                    "rows": [
                      { "Name": "Alice", "Enabled": "Yes" },
                      { "Name": "Bob",   "Enabled": "No"  }
                    ]
                  }
                ]
              }
            ]
          }
        }
        """;

        var payload = JsonSerializer.Deserialize<RenderPayload>(json, JsonOpts)!;
        var output = CreateService().Render(payload, null, 20000);

        Assert.NotNull(output.Bytes);
        Assert.True(output.Bytes.Length > 0);
    }

    // Multiple string-equality CFs on the same column (Quota Status) must all render.
    [Fact]
    public void Render_MultipleStringEqualConditionalFormats_DoesNotThrow()
    {
        var json = """
        {
          "file_name": "mailboxes.xlsx",
          "response_mode": "base64_json",
          "workbook": {
            "worksheets": [
              {
                "name": "Mailboxes",
                "freeze_panes": "A2",
                "blocks": [
                  {
                    "type": "table",
                    "start_cell": "A1",
                    "columns": [
                      { "header": "Name",         "key": "Name",         "type": "string" },
                      { "header": "Quota Status", "key": "Quota Status", "type": "string" },
                      { "header": "Storage (GB)", "key": "Storage",      "type": "number" }
                    ],
                    "conditional_formats": [
                      { "column_key": "Quota Status", "fill_color": "#C6EFCE", "op": "equal",        "value": "Healthy"                      },
                      { "column_key": "Quota Status", "fill_color": "#FFEB9C", "op": "equal",        "value": "Warning (Approaching Limit)"  },
                      { "column_key": "Quota Status", "fill_color": "#FFC7CE", "op": "equal",        "value": "Full"                         },
                      { "column_key": "Storage",      "fill_color": "#FFEB9C", "op": "greater_than", "value": 40                             },
                      { "column_key": "Storage",      "fill_color": "#FFC7CE", "op": "greater_than", "value": 70                             }
                    ],
                    "rows": [
                      { "Name": "Alice", "Quota Status": "Healthy", "Storage": 10.5 },
                      { "Name": "Bob",   "Quota Status": "Full",    "Storage": 75.0 }
                    ]
                  }
                ]
              }
            ]
          }
        }
        """;

        var payload = JsonSerializer.Deserialize<RenderPayload>(json, JsonOpts)!;
        var output = CreateService().Render(payload, null, 20000);

        Assert.NotNull(output.Bytes);
        Assert.True(output.Bytes.Length > 0);
    }
}
