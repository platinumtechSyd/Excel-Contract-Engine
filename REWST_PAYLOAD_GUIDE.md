# Rewst payload guide

This project’s **Rewst** integration (from **`/api/openapi-rewst.json`**) exposes **five** POST actions: tier 1 vs tier 2 × validate vs render, plus **SharePoint upload**. Validate/render actions use **`payload_json`** (stringified contract JSON). SharePoint upload uses **direct JSON fields only** (no wrapper).

**Operators:** Fork, deploy your own instance, and supply your own API key—see [README — Fork, deploy, and API keys](./README.md#fork-deploy-and-api-keys-operators) and **[SETUP.md](./SETUP.md)** (Azure). As-is; no guaranteed support.

## Endpoints and OpenAPI

| Purpose | Method | Route |
|---------|--------|--------|
| Validate tier 1 (inner JSON has `workbook`) | `POST` | `/api/rewst/tier1/validate` |
| Render tier 1 | `POST` | `/api/rewst/tier1/render` |
| Validate tier 2 (inner JSON has `sheets`) | `POST` | `/api/rewst/tier2/validate` |
| Render tier 2 | `POST` | `/api/rewst/tier2/render` |
| Upload file to SharePoint (Graph; direct JSON upload fields) | `POST` | `/api/rewst/sharepoint/upload` |
| Rewst OpenAPI spec | `GET` | `/api/openapi-rewst.json` |

Import **`/api/openapi-rewst.json`** into Rewst. Configure **API key** on the HTTP integration (`X-Api-Key`); it must not appear as a per-action input. Optional **`X-Correlation-Id`** (or integration-level headers) helps trace requests in logs—see [REWST_SUBWORKFLOW.md](./REWST_SUBWORKFLOW.md).

Recommended pattern: **validate then render** using matching tier routes ([REWST_SUBWORKFLOW.md](./REWST_SUBWORKFLOW.md)). Error codes: [ERROR_CODES.md](./ERROR_CODES.md).

## Rewst request shape

```json
{
  "payload_json": "{\"schema_version\":\"1.0\",\"workbook\":{...}}"
}
```

Build `payload_json` by serializing your contract object to a string (escape quotes as required). The **inner** JSON is what the sections below describe (except for SharePoint upload).

## SharePoint upload (optional)

**Route:** `POST /api/rewst/sharepoint/upload` — **direct body only**. This payload is **not** a workbook contract. It targets a library path and supplies file bytes. Configure **`GRAPH_*`** on the Function App and Entra per **[ENTRA_GRAPH_SETUP.md](./ENTRA_GRAPH_SETUP.md)**.

| Field | Required | Notes |
|-------|----------|--------|
| `file_name` | Yes | Target filename in the library. |
| `content_base64` | Yes | File bytes (e.g. from a prior **`/api/rewst/tier1/render`** response’s `content_base64`). |
| `site_id` | Yes | Graph site id (`hostname,siteCollectionId,siteId`). |
| `folder_path` | Typical | Path under the library root; **folders must already exist** (see [SETUP.md](./SETUP.md)). |
| `content_type` | No | e.g. MIME type for the upload. |
| `overwrite` | No | Default `true`. |

**Example (direct body; recommended):**

```json
{
  "site_id": "platinumtechnology.sharepoint.com,9cf956ec-c740-457f-b0f0-ba987a275175,06a76b0d-db58-4c14-a94b-04dd3d5819a0",
  "folder_path": "Invoicing/Technology Usage Reporting/Azure/2026/04",
  "file_name": "Azure_Billing_April_2026.xlsx",
  "content_base64": "<base64 from render step>",
  "overwrite": true
}
```

Errors use **`status`** / **`error_code`** (not the validate/render `errors[]` shape); see [ERROR_CODES.md](./ERROR_CODES.md).

## Inner contract: tier 1 (workbook) vs tier 2 (sources + sheets)

- **Tier 1** — Root has `workbook` with `worksheets`, each with `blocks` (usually `type: "table"`). You define `columns` and `rows` explicitly. This is the most direct way to use **`conditional_formats`** and **`row_rules`** on a block.
- **Tier 2** — Root has `sources` and `sheets`. The normalizer expands this into tier 1 before rendering. Row rules on tier 2 live on **`sheets[].row_rules`** (they are carried onto the generated table block).

Use **one** style per payload: either `workbook` **or** `sheets` at the top level (not both as competing roots).

## Validation errors and `path`

Validate and render responses include `errors` and `warnings` arrays. Each item has `code`, `message`, and usually **`path`**: where to look inside the **embedded** JSON (the parsed content of `payload_json`).

| `path` prefix | Meaning |
|---------------|---------|
| `$` | Whole document (invalid JSON, unexpected failure) or generic render failure |
| `payload_json` | Rewst wrapper only (e.g. body too large for configured limit) |
| `schema_version`, `workbook`, … | Standard JSON paths into the inner contract |
| `workbook.worksheets[i].blocks[j].…` | Tier 1 table block details |
| `sheets[i].…`, `sheets[i].columns.foo` | Tier 2 sheet / column issues |

Bracket indices are **0-based** and match array order in your JSON.

Newer structural codes include: `CONTRACT_ROOT_INVALID`, `WORKBOOK_MISSING`, `EMPTY_WORKBOOK`, `WORKSHEET_NO_BLOCKS`, `TABLE_NO_COLUMNS`, `INVALID_START_CELL`, `CF_UNKNOWN_COLUMN`, `ROW_RULE_WHEN_INVALID`, `ROW_RULE_UNKNOWN_COLUMN`, `EMPTY_SHEETS`, `SHEET_SOURCE_REQUIRED`, `UNSUPPORTED_SCHEMA_VERSION`, plus existing join/source codes on tier 2.

---

## Example 1 — Minimal single-sheet table

Inner JSON (place inside `payload_json` as a string):

```json
{
  "schema_version": "1.0",
  "file_name": "minimal-report.xlsx",
  "workbook": {
    "worksheets": [
      {
        "name": "Data",
        "blocks": [
          {
            "type": "table",
            "start_cell": "A1",
            "columns": [
              { "key": "name", "header": "Name", "type": "string" },
              { "key": "amount", "header": "Amount", "type": "number", "number_format": "#,##0.00" }
            ],
            "rows": [
              { "name": "Alice", "amount": 100.5 },
              { "name": "Bob", "amount": 200 }
            ]
          }
        ]
      }
    ]
  }
}
```

---

## Example 2 — Multi-sheet report

Inner JSON:

```json
{
  "schema_version": "1.0",
  "file_name": "multi-sheet.xlsx",
  "report_name": "Q1 Summary",
  "defaults": {
    "date_format": "yyyy-mm-dd",
    "boolean_display": ["Yes", "No"],
    "freeze_header": true
  },
  "workbook": {
    "worksheets": [
      {
        "name": "Summary",
        "blocks": [
          {
            "type": "table",
            "start_cell": "A1",
            "table_theme": "TableStyleMedium2",
            "columns": [
              { "key": "metric", "header": "Metric", "type": "string" },
              { "key": "value", "header": "Value", "type": "number" }
            ],
            "rows": [
              { "metric": "Tickets closed", "value": 42 },
              { "metric": "SLA %", "value": 98.2 }
            ]
          }
        ]
      },
      {
        "name": "Details",
        "blocks": [
          {
            "type": "table",
            "start_cell": "A1",
            "columns": [
              { "key": "id", "header": "ID", "type": "string" },
              { "key": "detail", "header": "Detail", "type": "string" }
            ],
            "rows": []
          }
        ]
      }
    ]
  }
}
```

Empty `rows` is allowed; you still get headers and an empty table body.

---

## Example 3 — Advanced: currency, dates, conditional formats, row rules

Inner JSON:

```json
{
  "schema_version": "1.0",
  "file_name": "advanced.xlsx",
  "defaults": {
    "date_format": "yyyy-mm-dd",
    "datetime_format": "yyyy-mm-dd hh:mm",
    "boolean_display": ["Y", "N"],
    "null_display": "—"
  },
  "workbook": {
    "worksheets": [
      {
        "name": "Invoices",
        "blocks": [
          {
            "type": "table",
            "start_cell": "A1",
            "columns": [
              { "key": "invoice_id", "header": "Invoice", "type": "string" },
              { "key": "amount", "header": "Amount", "type": "currency", "currency_code": "USD" },
              { "key": "due_date", "header": "Due", "type": "date" },
              { "key": "days_open", "header": "Days open", "type": "integer" },
              { "key": "paid", "header": "Paid", "type": "boolean" }
            ],
            "rows": [
              { "invoice_id": "INV-001", "amount": 1500, "due_date": "2026-01-15", "days_open": 45, "paid": false },
              { "invoice_id": "INV-002", "amount": 220.5, "due_date": "2026-04-01", "days_open": 5, "paid": true }
            ],
            "conditional_formats": [
              {
                "column_key": "days_open",
                "op": "greater_than",
                "value": 30,
                "fill_color": "light_red"
              },
              {
                "column_key": "amount",
                "op": "between",
                "value": 100,
                "value2": 2000,
                "fill_color": "#E2EFDA"
              }
            ],
            "row_rules": [
              {
                "when": { "paid": false, "days_open": 45 },
                "style": "warning"
              },
              {
                "when": { "paid": true },
                "style": "success"
              }
            ]
          }
        ]
      }
    ]
  }
}
```

**Notes:**

- **`conditional_formats`**: `column_key` must match a **`columns[].key`**. Supported `op` values include `greater_than`, `less_than`, `equal`, `between` (use `value` and `value2` for between).
- **`row_rules`**: `when` is an object whose property names are **column keys**. All listed keys must match the row (AND). The renderer also supports a few nested shapes on a property value (for example date rules); see `ExcelRenderService` for details.

---

## Output

Rewst **tier1/render** and **tier2/render** always return **`application/json`** with `content_base64` (spreadsheet bytes), `file_name`, `stats`, and `warnings`, never raw binary. Use the matching **validate** route first when you need to branch on `valid` before decoding the file ([REWST_SUBWORKFLOW.md](./REWST_SUBWORKFLOW.md)).

For the full generic contract reference (all fields, tier 2 joins, etc.), see the main **`/api/openapi.json`** schema and the C# models under `ExcelRenderer.Functions/Models/`.
