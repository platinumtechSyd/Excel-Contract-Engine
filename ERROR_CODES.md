# Validation and error codes

Responses from **`/api/rewst/tier1/*`**, **`/api/rewst/tier2/*`**, **`/api/validate`**, and failed **`/api/render`** calls may include `errors` and `warnings` arrays. Each item has:

| Field | Meaning |
|--------|---------|
| `code` | Stable machine-readable identifier |
| `message` | Human-readable detail |
| `path` | Where to fix the problem (see [REWST_PAYLOAD_GUIDE.md](./REWST_PAYLOAD_GUIDE.md)) |

## Path quick reference

| `path` | Meaning |
|--------|---------|
| `$` | Whole inner JSON document, or generic render failure |
| `payload_json` | Rewst wrapper field (body too large, etc.) |
| `workbook.…` | Tier 1 inner contract |
| `sheets[…].…` | Tier 2 inner contract (before normalization expands to workbook) |

## Code registry

| Code | Severity | Typical `path` | Meaning / fix |
|------|----------|----------------|---------------|
| `VALIDATION_PARSE_ERROR` | error | `$` or `payload_json` | Invalid JSON, unreadable body, deserialize failure, or unexpected exception during validation |
| `CONTRACT_ROOT_INVALID` | error | `$` | Inner JSON has neither `workbook` nor `sheets` (auto-detect / generic validate only) |
| `TIER_MISMATCH_TIER1_EXPECT_WORKBOOK` | error | `$` | Called **tier 1** Rewst route but inner JSON has no non-null `workbook` |
| `TIER_MISMATCH_TIER2_EXPECT_SHEETS` | error | `$` | Called **tier 2** Rewst route but inner JSON has no `sheets` array |
| `PAYLOAD_TOO_LARGE` | error | `payload_json` | Inner string exceeds `MAX_REQUEST_BYTES` |
| `UNSUPPORTED_SCHEMA_VERSION` | error | `schema_version` | Only `1.0` supported |
| `WORKBOOK_MISSING` | error | `workbook` | Tier 1 payload missing workbook object |
| `EMPTY_WORKBOOK` | error | `workbook.worksheets` | No worksheets |
| `EMPTY_SHEETS` | error | `sheets` | Tier 2: `sheets` array empty |
| `WORKSHEET_NO_BLOCKS` | error | `workbook.worksheets[i].blocks` | Worksheet has no blocks |
| `TABLE_NO_COLUMNS` | error | `workbook.worksheets[i].blocks[j].columns` | Table block needs at least one column |
| `INVALID_START_CELL` | error | `…blocks[j].start_cell` | Bad Excel A1 address |
| `CF_UNKNOWN_COLUMN` | error | `…conditional_formats[r].column_key` | Rule references unknown column key |
| `ROW_RULE_WHEN_INVALID` | error | `…row_rules[rr].when` | `when` must be a JSON object |
| `ROW_RULE_UNKNOWN_COLUMN` | error | `…row_rules[rr].when.<name>` | `when` key is not a column key |
| `SHEET_SOURCE_REQUIRED` | error | `sheets[i]` | Tier 2 sheet needs `data` or `primary_source` |
| `SRC_NOT_FOUND` | error/warning† | `sheets[i].columns.<key>` | Referenced source id missing |
| `JOIN_KEY_MISSING` | error/warning† | `sheets[i].columns.<key>` | Cannot resolve join keys for a source |
| `PRIMARY_SOURCE_NOT_FOUND` | error/warning† | `sheets[i].primary_source` | Primary source id missing |
| `COL_OMITTED_EMPTY_SOURCE` | warning | `sheets[i].columns.<key>` | Column dropped: source empty and `on_empty=omit_columns` |
| `RENDER_FAILED` | error | `$` | Renderer threw (e.g. row limit, internal error) |

† **Strict mode** (`strict_mode: true` on tier 2): these are **errors**. Otherwise they are **warnings** and rendering may still proceed.

## SharePoint upload (`POST /api/rewst/sharepoint/upload`)

Setup (Entra app, **Sites.Selected**, site grants): **[ENTRA_GRAPH_SETUP.md](./ENTRA_GRAPH_SETUP.md)**.

JSON responses use **`status`**, **`error_code`**, and **`message`** (not the `errors[]` contract above).

| Code | Meaning |
|------|---------|
| `GRAPH_NOT_CONFIGURED` | Missing `GRAPH_TENANT_ID` / `GRAPH_CLIENT_ID` / `GRAPH_CLIENT_SECRET`. |
| `GRAPH_AUTH_FAILED` | Token request to login.microsoftonline.com failed. |
| `VALIDATION_ERROR` | Bad `payload_json` (missing exclusivity of site/drive fields, bad base64, etc.). |
| `FILE_TOO_LARGE` | Decoded file exceeds **250 MB** (same cap as Microsoft Graph drive-item uploads). |
| `SITE_NOT_FOUND` | Graph could not resolve `site_id` or `site_url`. |
| `DRIVE_NOT_FOUND` | Could not match `library_name` or invalid `drive_id`. |
| `GRAPH_UPLOAD_FAILED` | Graph returned an error (permissions, path, conflict, etc.). |
| `GRAPH_HTTP_ERROR` | Network-level HTTP failure calling Graph. |

Success body: **`status`: `ok`**, **`web_url`**, **`path`** (when Graph returns it), **`item_id`**.

## Generic `/api/render` and `/api/validate`

The same codes apply to inner JSON validated through the non-Rewst endpoints. Rewst-specific codes are **`TIER_MISMATCH_*`** and issues whose `path` is **`payload_json`** (wrapper-only).
