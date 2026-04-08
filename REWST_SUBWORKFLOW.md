# Rewst subworkflow: validate then render

**Operators:** You need **your own** deployed Function App and **your own** `RENDER_API_KEY` / Rewst **`X-Api-Key`** (see the main [README](./README.md#fork-deploy-and-api-keys-operators)). Deploy with **[SETUP.md](./SETUP.md)**. No shared key or support is provided—buyer beware.

Use a **child workflow** (or sequential tasks) so every render is preceded by a validation call with the **same** `payload_json` and **same tier** route. That avoids decoding large base64 files when the contract is invalid and gives you structured `errors[]` with `path` for fixes.

## Pick the tier routes

| Inner contract shape | Validate | Render |
|---------------------|----------|--------|
| Root has `workbook` (tables, `conditional_formats`, `row_rules`, …) | `POST /api/rewst/tier1/validate` | `POST /api/rewst/tier1/render` |
| Root has `sheets` (+ usually `sources`) | `POST /api/rewst/tier2/validate` | `POST /api/rewst/tier2/render` |

Body for validate/render (all four tier routes) uses the same wrapper:

```json
{
  "payload_json": "<stringified inner JSON>"
}
```

## Subworkflow outline

1. **Build** `payload_json` in Rewst (serialize your object to a string).
2. **Call validate** for the correct tier.
3. **Branch** on `valid` from the JSON response (`true` / `false`).
4. If `valid` is **false**: handle `errors` (and optionally `warnings`); use `path` and [ERROR_CODES.md](./ERROR_CODES.md); do not call render.
5. If `valid` is **true**: **call render** with the **same** `payload_json` (and same tier).
6. **Decode** `content_base64` from the render response when you need the `.xlsx` file.
7. **Optional — SharePoint:** call **`POST /api/rewst/sharepoint/upload`** with a new `payload_json` containing `content_base64`, `file_name`, `folder_path`, site/drive resolution, plus Graph app settings on the Function App (see [SETUP.md](./SETUP.md)).

## Correlation id (optional)

To tie Rewst runs to Application Insights / logs:

- Send header **`X-Correlation-Id`** (or **`X-Request-Id`**) on validate and render requests, **or**
- Configure your HTTP integration in Rewst to add a static or **extracted** header so you do not need an extra field on each action.

The function logs a line like: `Rewst request RewstTier1Render correlation_id=<value>` when a supported header is present.

## OpenAPI import

Import **`GET /api/openapi-rewst.json`**. You should see **five** POST operations: tier1/tier2 × validate/render, plus **`POST /api/rewst/sharepoint/upload`**. Optional **`X-Correlation-Id`** is listed on each. **`X-Api-Key`** stays on the integration only.

The same document lives in the repo as **`ExcelRenderer.Functions/openapi-rewst.json`** (valid JSON with escaped `payload_json` strings). You can share that file or the hosted URL—see below.

## Sharing the spec safely

- **Examples are synthetic** (“Company A”, generic filenames, `UTC`). They are not real customers; still avoid pasting live data into examples if you fork the spec.
- **Never embed** `RENDER_API_KEY`, subscription IDs, tenant names, or internal hostnames in the OpenAPI file or descriptions.
- **Prefer** publishing **`/api/openapi-rewst.json`** from your Function App so partners import a URL; they never need your repo.
- If you **customize** `info.description`, keep it free of environment-specific URLs (you already dropped Azure portal links—that’s good).
- Schema **`minLength: 2`** on `payload_json` rejects a zero- or one-character string only; real payloads are always longer.

## Docs

- [REWST_PAYLOAD_GUIDE.md](./REWST_PAYLOAD_GUIDE.md) — inner JSON examples  
- [ERROR_CODES.md](./ERROR_CODES.md) — `code` / `path` reference  
