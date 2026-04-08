# Excel Renderer Function App

This project accepts Rewst contracts and returns `.xlsx` files.

## Documentation map

| Audience | Start here |
|----------|------------|
| **Operators** (deploy, keys, monitoring) | [Fork, deploy, and API keys](#fork-deploy-and-api-keys-operators) → [SETUP.md](./SETUP.md) ([security checklist](./SETUP.md#security-and-operations-checklist)) |
| **CI** | GitHub Actions: **`dotnet test`** + **`dotnet build`** on push/PR (`.github/workflows/ci.yml`) |
| **Rewst authors** (validate → render, `payload_json`) | [REWST_SUBWORKFLOW.md](./REWST_SUBWORKFLOW.md), [REWST_PAYLOAD_GUIDE.md](./REWST_PAYLOAD_GUIDE.md) |
| **SharePoint / Graph** | [ENTRA_GRAPH_SETUP.md](./ENTRA_GRAPH_SETUP.md), [SETUP.md](./SETUP.md) (app settings) |
| **Error codes and `path`** | [ERROR_CODES.md](./ERROR_CODES.md) |

**Versioning:** Feature bullets below (e.g. v1.1) describe **contract and API behavior**. The **`info.version`** field inside **`/api/openapi-rewst.json`** is the **Rewst OpenAPI document** revision only; it may move independently of `schema_version` in your JSON contracts.

## Fork, deploy, and API keys (operators)

This is **not** a hosted product. If you use it, you are expected to **fork the repository**, **deploy your own** Azure Functions instance (or run it locally), and **operate it yourself**.

- **Your API key** — Create a secret (e.g. `RENDER_API_KEY` in app settings) and configure Rewst’s HTTP integration to send **`X-Api-Key`** with that value. **You generate and rotate keys; nothing is issued to you by this repo.** Prefer storing it in **Azure Key Vault** and referencing it from app settings (see [SETUP.md](./SETUP.md)).
- **Your environment** — You own configuration, networking, cost, and security hardening. **Secrets:** Key Vault references for `RENDER_API_KEY` and `GRAPH_CLIENT_SECRET` where possible. **Monitoring:** Application Insights alerts for 5xx and unusual traffic (recommended in [SETUP.md](./SETUP.md)). **Runbook:** Record base URL, Rewst integration details, and rotation ownership outside of git secrets. **Strongly recommended:** **restrict inbound traffic to Rewst’s outbound NAT IPs** for your region ([SETUP.md — Step 12](./SETUP.md#step-12--strongly-recommended-restrict-access-to-rewst-outbound-ips); [Rewst security policy](https://docs.rewst.help/security/security-policy)). On **Consumption** plans you may need **Premium / Dedicated** (or another edge) to enforce IP rules on the Function App.
- **Buyer beware** — Provided **as-is**, without warranty. **No guaranteed support**, SLA, or obligation to help with your fork, workflows, or deployments. Use at your own risk.

**Azure setup (Portal-first, optional Bicep):** [SETUP.md](./SETUP.md) — create the Function App in the **Azure Portal** step by step, or deploy **`infra/main.bicep`**, then follow the same security and Rewst steps.

## Contract tiers

- Tier 1: `workbook` + `worksheets` + `blocks` (direct renderer model)
- Tier 2: `sources` + `sheets` (simple and joined reports)

## v1.1.1 additions

- `strict_mode` on tier 2 contracts
- `POST /api/validate` contract validator endpoint
- coded warnings/errors (e.g. `SRC_NOT_FOUND`, `JOIN_KEY_MISSING`)
- render stats in JSON response (`sheet_count`, `block_count`, `row_count`)
- guardrails via app settings: `MAX_REQUEST_BYTES`, `MAX_ROWS_PER_SHEET`
- defaults support: `date_format`, `datetime_format`, `boolean_display`

## v1.1 additions

- `delivery.format`: `binary` or `base64` (alias of `base64_json` response mode)
- `row_rules`: row-level styling (`danger`, `warning`, `success`)
- source resilience: `on_empty` / `on_null` (supports `omit_columns` + `use_default` behavior)
- `defaults.null_display`: replacement value for null values in tier 2 transforms

## Response modes

- `binary` (default): HTTP body is Excel bytes, with `Content-Disposition` download header.
- `base64_json`: HTTP body is JSON with `content_base64`, `warnings`, and `stats`.

## API endpoints (generic HTTP)

These endpoints take the **inner contract JSON** as the request body (not the Rewst `payload_json` wrapper). Validation and rendering rules match the Rewst tier routes; generic routes do not enforce tier via the URL, so keep your inner JSON shape consistent with how you call **`/api/rewst/tier1/*`** vs **`/api/rewst/tier2/*`**.

- `POST /api/render`
- `POST /api/validate`
- `GET /api/health`
- `GET /api/openapi.json`

## Rewst integration (recommended)

Import **`GET /api/openapi-rewst.json`** into Rewst. Each generated action has one body field: **`payload_json`** (a string). Use **tier-specific** routes so the correct contract is enforced:

| Tier | Validate | Render | Inner JSON root |
|------|----------|--------|-----------------|
| 1 | `POST /api/rewst/tier1/validate` | `POST /api/rewst/tier1/render` | `workbook` |
| 2 | `POST /api/rewst/tier2/validate` | `POST /api/rewst/tier2/render` | `sheets` (array) |

**SharePoint (optional):** `POST /api/rewst/sharepoint/upload` — upload `content_base64` via Microsoft Graph (**`GRAPH_*`** app settings). Entra app + permissions: **[ENTRA_GRAPH_SETUP.md](./ENTRA_GRAPH_SETUP.md)**; deploy + settings: [SETUP.md](./SETUP.md).

Set **`X-Api-Key`** on the HTTP integration (not per action) to the **same value** you configured as `RENDER_API_KEY` on **your** Function App. Optional **`X-Correlation-Id`** is listed in the Rewst OpenAPI for tracing; you can instead configure headers on the integration.

**Docs:** [REWST_SUBWORKFLOW.md](./REWST_SUBWORKFLOW.md) (validate → render subworkflow), [REWST_PAYLOAD_GUIDE.md](./REWST_PAYLOAD_GUIDE.md) (examples), [ERROR_CODES.md](./ERROR_CODES.md) (codes and `path`). **Support:** see [Fork, deploy, and API keys (operators)](#fork-deploy-and-api-keys-operators) above.

## Validate example (generic `/api/validate`, tier 2 shape)

Use this body with **`POST /api/validate`** when not using the Rewst wrapper:

```http
POST /api/validate
Content-Type: application/json
X-Api-Key: <your key>
```

```json
{
  "schema_version": "1.0",
  "strict_mode": false,
  "delivery": { "format": "base64" },
  "defaults": {
    "null_display": "—",
    "date_format": "yyyy-mm-dd",
    "datetime_format": "yyyy-mm-dd hh:mm",
    "boolean_display": ["Yes", "No"],
    "freeze_header": true
  },
  "sources": {
    "users": {
      "data": [
        { "id": "u1", "displayName": "Alice", "enabled": true, "createdDate": "2026-04-01" }
      ],
      "key": "id"
    }
  },
  "sheets": [
    {
      "name": "Users",
      "primary_source": "users",
      "columns": {
        "displayName": { "header": "Name", "type": "string" },
        "enabled": { "header": "Enabled", "type": "boolean" },
        "createdDate": { "header": "Created", "type": "date" }
      }
    }
  ]
}
```

Example success response:

```json
{
  "valid": true,
  "response_mode": "base64_json",
  "errors": [],
  "warnings": []
}
```

## Required app settings

- `FUNCTIONS_WORKER_RUNTIME=dotnet-isolated`
- `RENDER_API_KEY` — **required** (non-empty). Clients must send **`X-Api-Key`** or **`Authorization: Bearer`** with the same value. If the app setting is **missing or empty**, protected routes return **503** (misconfiguration). For **local** runs, set it in **`local.settings.json`** (see **`ExcelRenderer.Functions/local.settings.json.example`**) or user secrets; never commit real keys. Prefer **Key Vault** references in Azure: [SETUP.md](./SETUP.md).
- `DEFAULT_TABLE_THEME` (optional, default `TableStyleMedium2`)
- `MAX_REQUEST_BYTES` (default 5000000)
- `MAX_ROWS_PER_SHEET` (default 20000)

**Hardening summary:** [SETUP.md — Security and operations checklist](./SETUP.md#security-and-operations-checklist).

## Local run

1. Copy **`ExcelRenderer.Functions/local.settings.json.example`** to **`local.settings.json`** and set **`RENDER_API_KEY`** (see [SETUP.md](./SETUP.md) Step 1).
2. From **`ExcelRenderer.Functions`**:

```bash
func start
```

3. Optional: with the host up, **`SMOKE_API_KEY=<same as RENDER_API_KEY>`** then run **`scripts/smoke-test.sh`** or **`scripts/smoke-test.ps1`**.

## Azure deployment

Full walkthrough (**Azure Portal** as the default path, **Bicep** optional, Key Vault, publish, smoke tests, Rewst): **[SETUP.md](./SETUP.md)**.
