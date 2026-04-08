# Setup guide: Azure Functions

This walks you from **clone** to a **running Function App** on Azure, then a quick **smoke test** and **Rewst** hookup. You deploy and operate your own instance—see [README — Fork, deploy, and API keys](./README.md#fork-deploy-and-api-keys-operators).

## What gets deployed

- **Azure Functions** v4, **.NET 8** isolated worker  
- **Linux** Consumption plan (`Y1`) by default via `infra/main.bicep` (pay-per-use, cold start possible)  
- **Storage** account (required by Functions)  
- **Application Insights** (logging)  
- HTTP routes under **`https://<your-function-app>.azurewebsites.net/api/...`**

## Prerequisites

| Tool | Purpose |
|------|---------|
| [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli) | Create resources, deploy Bicep |
| [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) | Build the project |
| [Azure Functions Core Tools v4](https://learn.microsoft.com/azure/azure-functions/functions-run-local) | Local run and optional `func azure functionapp publish` |
| An **Azure subscription** where you can create resource groups and Function Apps | |

Sign in:

```bash
az login
az account set --subscription "<subscription-id-or-name>"
```

## 1. Clone and build (local sanity check)

```bash
git clone <your-fork-url>
cd "Excel-Renderder Tool/ExcelRenderer.Functions"
dotnet build -c Release
```

Optional local run (no Azure):

```bash
func start
```

Then open `http://localhost:7071/api/health` (port may differ; check the `func` output).

## 2. Create a resource group

Pick a region (example: `australiaeast`):

```bash
az group create --name <rg-name> --location australiaeast
```

## 3. Deploy infrastructure (Bicep)

From the **repository root** (where `infra/main.bicep` lives):

```bash
az deployment group create \
  --resource-group <rg-name> \
  --template-file infra/main.bicep \
  --parameters \
    namePrefix=excelrender \
    functionAppName=<globally-unique-function-app-name> \
    storageAccountName=<globally-unique-storage-lower-case> \
    appInsightsName=<globally-unique-app-insights-name> \
    renderApiKey=<your-secret-api-key>
```

**Parameters:**

| Parameter | Notes |
|-----------|--------|
| `namePrefix` | Prefix for the App Service plan name (e.g. `excelrender`). |
| `functionAppName` | Becomes `https://<functionAppName>.azurewebsites.net`. Must be globally unique. |
| `storageAccountName` | Lowercase letters and numbers only; globally unique. |
| `appInsightsName` | Globally unique Application Insights resource name. |
| `renderApiKey` | **Your** secret; HTTP clients send it as **`X-Api-Key`**. Use a long random string. Can be empty for dev-only (not recommended). |
| `defaultTableTheme` | Optional; default `TableStyleMedium2`. |

The template sets **`FUNCTIONS_WORKER_RUNTIME`**, **`RENDER_API_KEY`**, storage, and Insights. It does **not** set `MAX_REQUEST_BYTES` / `MAX_ROWS_PER_SHEET`—add those in Configuration if you want non-default limits (see below).

### Flex Consumption or custom plans

If you create or move the app to **Flex Consumption** (or another SKU), the portal may **reject** duplicate or conflicting app settings. If deployment fails with errors about **`FUNCTIONS_WORKER_RUNTIME`**, follow the portal’s guidance—some Flex setups manage worker settings differently than classic Consumption.

## 4. Deploy application code

The Bicep template creates an **empty** Function App shell. You must publish the **ExcelRenderer.Functions** project.

### Option A — Deployment Center (GitHub)

1. In [Azure Portal](https://portal.azure.com), open your **Function App**.  
2. **Deployment Center** → source **GitHub** → select your fork, branch, and the folder **`ExcelRenderer.Functions`** if the wizard asks for a path.  
3. Save; the first build/deploy may take several minutes.

### Option B — Azure Functions Core Tools (from your machine)

Requires [Azure Functions Core Tools](https://learn.microsoft.com/azure/azure-functions/functions-run-local#install-the-azure-functions-core-tools).

```bash
cd ExcelRenderer.Functions
func azure functionapp publish <functionAppName>
```

Use the same **function app name** as in Bicep.

### Option C — CI/CD you own

Build `dotnet publish` and deploy the output with your pipeline (ZIP deploy, GitHub Actions, etc.), as long as **`openapi-rewst.json`** is copied with the app (the `.csproj` includes `CopyToOutputDirectory`).

## 5. Application settings (verify in Portal)

**Function App** → **Configuration** → **Application settings**. Confirm or add:

| Name | Typical value | Notes |
|------|----------------|--------|
| `FUNCTIONS_EXTENSION_VERSION` | `~4` | Usually set by template/portal. |
| `FUNCTIONS_WORKER_RUNTIME` | `dotnet-isolated` | Required for this project. |
| `RENDER_API_KEY` | *(your secret)* | Must match what Rewst sends as `X-Api-Key`. |
| `DEFAULT_TABLE_THEME` | `TableStyleMedium2` | Optional override. |
| `MAX_REQUEST_BYTES` | `5000000` | Add if not present; caps inner JSON size. |
| `MAX_ROWS_PER_SHEET` | `20000` | Add if not present. |

Click **Save** when you change values (the app may restart).

## 6. Smoke test (production URL)

Replace `<func-host>` with `https://<functionAppName>.azurewebsites.net` and `<api-key>` with your `RENDER_API_KEY`.

**Health**

```bash
curl "<func-host>/api/health"
```

**Validate (generic API, tier 1 inner JSON)** — one line (bash, Git Bash, or WSL):

```bash
curl -X POST "<func-host>/api/validate" -H "Content-Type: application/json" -H "X-Api-Key: <api-key>" -d "{\"schema_version\":\"1.0\",\"workbook\":{\"worksheets\":[{\"name\":\"Sheet1\",\"blocks\":[{\"type\":\"table\",\"start_cell\":\"A1\",\"columns\":[{\"key\":\"a\",\"header\":\"A\",\"type\":\"string\"}],\"rows\":[{\"a\":\"ok\"}]}]}]}}"
```

**Rewst OpenAPI document**

```bash
curl "<func-host>/api/openapi-rewst.json"
```

You should see JSON with **`openapi": "3.0.3"`** and tier routes.

## 7. Rewst integration

1. In Rewst, add an HTTP integration pointing at your **`func-host`**.  
2. Import OpenAPI from **`https://<func-host>/api/openapi-rewst.json`** (or paste the repo file `ExcelRenderer.Functions/openapi-rewst.json`).  
3. Set **`X-Api-Key`** on the integration to your **`RENDER_API_KEY`**.  
4. Build workflows using **tier1/tier2 validate → render** as in [REWST_SUBWORKFLOW.md](./REWST_SUBWORKFLOW.md).

Prefer Rewst routes under **`/api/rewst/tier1/*`** and **`/api/rewst/tier2/*`** over legacy **`/api/render`** when you use the `payload_json` wrapper.

## 8. Troubleshooting

| Symptom | Things to check |
|---------|------------------|
| **403** on API | `RENDER_API_KEY` set in Azure and same value as `X-Api-Key` (or leave key empty in dev for both). |
| **404** on `/api/...` | Code not published; wrong Function App name; check **Functions** list in Portal. |
| **500** on first request | Cold start—retry; check **Log stream** and Application Insights. |
| **OpenAPI empty or old** | Ensure **`openapi-rewst.json`** is deployed next to the DLL (`CopyToOutputDirectory`). Redeploy. |
| **Validation errors with `path`** | See [ERROR_CODES.md](./ERROR_CODES.md). |

## 9. Operational notes

- **Consumption** plans scale to zero; first request after idle can be slow.  
- **Rotate** `RENDER_API_KEY` periodically; update Rewst when you change it.  
- Tune **`MAX_REQUEST_BYTES`** and **`MAX_ROWS_PER_SHEET`** for abuse protection.

---

**Disclaimer:** This guide is for convenience only; you are responsible for your Azure environment, costs, and security. No guaranteed support—see [README](./README.md).
