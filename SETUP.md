# Setup guide: Azure Functions

This guide takes you from **clone** to a **running Function App**, **secure configuration**, **smoke tests**, and **Rewst** hookup. You deploy and operate your own instance—see [README — Fork, deploy, and API keys](./README.md#fork-deploy-and-api-keys-operators).

## What you are deploying

- **Azure Functions** v4, **.NET 8** isolated worker on **Linux**
- **Consumption** hosting (pay-per-use, cold start possible) unless you choose otherwise
- **Storage** account (required by Functions)
- **Application Insights** (logging and metrics)
- HTTP routes under **`https://<your-function-app>.azurewebsites.net/api/...`**

## How this guide is organized

| Path | Use it when |
|------|-------------|
| **[Path A — Azure Portal (recommended)](#path-a--azure-portal-recommended)** | You want **clear, single-instance** creation in the portal step by step (best default for a rebuild). |
| **[Path B — Bicep (optional)](#path-b--bicep-optional)** | You want **repeatable** infrastructure-as-code using `infra/main.bicep`. |

**After resources exist**, both paths share the same ideas: **managed identity**, **Key Vault** for secrets (recommended), **app settings**, **publish code**, **smoke tests**, **Rewst**, **monitoring**, and **ongoing** checks.

## Prerequisites

| Tool | Purpose |
|------|---------|
| [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli) | Optional for resource group and Path B; `az login` |
| [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) | Build the project |
| [Azure Functions Core Tools v4](https://learn.microsoft.com/azure/azure-functions/functions-run-local) | Local run and `func azure functionapp publish` |

Sign in (needed for CLI and local publish):

```bash
az login
az account set --subscription "<subscription-id-or-name>"
```

---

## Path A — Azure Portal (recommended)

Follow these steps in order.

### Step 1 — Clone and build (local sanity check)

```bash
git clone <your-fork-url>
cd <repository-root>    # folder created by clone (e.g. Excel-Renderder-Tool)
cd ExcelRenderer.Functions
dotnet build -c Release
```

Optional local run (no Azure):

```bash
func start
```

Open `http://localhost:7071/api/health` (port may differ; see `func` output).

### Step 2 — Create a resource group

1. In [Azure Portal](https://portal.azure.com), search for **Resource groups** → **Create**.
2. Choose a **Region** (for example `Australia East`).
3. Name the group (for example `rg-excel-render-prod`) → **Review + create**.

You can also create the group inside the Function App wizard in the next step.

### Step 3 — Create the Function App (single wizard)

1. **Create a resource** → search **Function App** → **Create**.
2. **Basics**
   - **Subscription** / **Resource group** — use the group from Step 2 (or create new).
   - **Function App name** — globally unique; becomes `https://<name>.azurewebsites.net`.
   - **Publish**: **Code**.
   - **Runtime stack**: **.NET**.
   - **Version**: **8** (LTS).
   - **Worker model** (if shown): **Isolated** / **.NET isolated** — required for this repo.
   - **Region** — same as the resource group (or your chosen region).
3. **Hosting**
   - **Operating System**: **Linux**.
   - **Plan type**: **Consumption (Serverless)** unless you have a reason to use Premium or Dedicated.
   - Let the wizard create a **new** storage account and (if offered) **Application Insights** — enable Insights for logs and [monitoring](#step-10--application-insights-monitoring-and-alerts-recommended).
4. **Review + create** → **Create**. Wait until deployment finishes.

### Step 4 — Enable managed identity

Secrets in Key Vault are easiest when the Function App has a **managed identity**.

1. Open your **Function App** → **Identity** (under *Settings*).
2. **System assigned** → **Status: On** → **Save**.
3. Note the **Object (principal) ID** — you use it when granting the app access to Key Vault secrets.

### Step 5 — Store secrets in Azure Key Vault (recommended)

**Goal:** Avoid putting `RENDER_API_KEY` or `GRAPH_CLIENT_SECRET` in plain text in Application settings.

1. **Create a Key Vault** (same subscription; same region as the Function App is typical):
   - **Create a resource** → **Key Vault** → name, region, RBAC or access policy model (either works if permissions are set correctly).
2. **Add secrets** (names are yours; examples below):
   - `render-api-key` — long random string; same logical value Rewst will send as `X-Api-Key`.
   - `graph-client-secret` — only if you use SharePoint upload; from Entra app registration.
3. **Grant the Function App access** to read secrets:
   - **RBAC model:** assign the Function App’s managed identity the role **Key Vault Secrets User** on the Key Vault (or on individual secrets).
   - **Access policy model:** add a policy for the app identity: **Get** (and **List** for listing, if required by your flow).
4. **Reference secrets from app settings** using [Key Vault references](https://learn.microsoft.com/azure/app-service/app-service-key-vault-references):
   - In **Function App** → **Configuration** → **Application settings**, add:
     - **Name** `RENDER_API_KEY`  
       **Value** `@Microsoft.KeyVault(SecretUri=https://<vault-name>.vault.azure.net/secrets/<secret-name>/)`  
       (include a **versioned** URI in production if you pin versions.)
     - **Name** `GRAPH_CLIENT_SECRET`  
       **Value** `@Microsoft.KeyVault(SecretUri=https://<vault-name>.vault.azure.net/secrets/<secret-name>/)`  
       when you use Graph upload.
5. **Save** Configuration. If a reference fails to resolve, the app may not start—check **Configuration** for errors and Key Vault firewall (allow trusted Microsoft services / your app’s access as needed).

**Development only:** You may set `RENDER_API_KEY` to a plain string temporarily; **do not** commit secrets to git. Prefer Key Vault or user secrets locally.

### Step 6 — Application settings (complete the list)

**Function App** → **Configuration** → **Application settings**. Add or verify:

| Name | Typical value | Notes |
|------|----------------|--------|
| `FUNCTIONS_EXTENSION_VERSION` | `~4` | Usually set by default. |
| `FUNCTIONS_WORKER_RUNTIME` | `dotnet-isolated` | Required. |
| `RENDER_API_KEY` | Key Vault reference or plain (dev) | Must match Rewst `X-Api-Key`. |
| `DEFAULT_TABLE_THEME` | `TableStyleMedium2` | Optional. |
| `MAX_REQUEST_BYTES` | `5000000` | Abuse guard; add if missing. |
| `MAX_ROWS_PER_SHEET` | `20000` | Abuse guard; add if missing. |
| `GRAPH_TENANT_ID` | Entra tenant GUID | For **`POST /api/rewst/sharepoint/upload`**. |
| `GRAPH_CLIENT_ID` | App registration (client) ID | Same. |
| `GRAPH_CLIENT_SECRET` | Key Vault reference (recommended) | Same. |

**Microsoft Graph + SharePoint:** Full Entra steps—**[ENTRA_GRAPH_SETUP.md](./ENTRA_GRAPH_SETUP.md)**.

Summary: register app, Graph **application** permissions (prefer **`Sites.Selected`** + per-site grant), admin consent, secret → map to `GRAPH_*`. Upload limit **250 MB** per single PUT; larger files need upload sessions (not implemented here). Folder path must exist.

Click **Save** when done (app restarts).

### Step 7 — Deploy application code

The Function App starts as an **empty** shell until you publish **ExcelRenderer.Functions**.

**Option A — Deployment Center (GitHub)**  
**Function App** → **Deployment Center** → **GitHub** → your fork, branch, and project folder **`ExcelRenderer.Functions`** if asked.

**Option B — Azure Functions Core Tools**

```bash
cd ExcelRenderer.Functions
func azure functionapp publish <functionAppName>
```

**Option C — Your CI/CD**  
`dotnet publish` + ZIP deploy / GitHub Actions; ensure **`openapi-rewst.json`** ships with the build (`CopyToOutputDirectory` in `.csproj`).

### Step 8 — Smoke test (production URL)

Replace `<func-host>` with `https://<functionAppName>.azurewebsites.net` and `<api-key>` with the value behind `RENDER_API_KEY`.

**Health**

```bash
curl "<func-host>/api/health"
```

**Validate (tier 1 sample)** — one line (bash, Git Bash, or WSL):

```bash
curl -X POST "<func-host>/api/validate" -H "Content-Type: application/json" -H "X-Api-Key: <api-key>" -d "{\"schema_version\":\"1.0\",\"workbook\":{\"worksheets\":[{\"name\":\"Sheet1\",\"blocks\":[{\"type\":\"table\",\"start_cell\":\"A1\",\"columns\":[{\"key\":\"a\",\"header\":\"A\",\"type\":\"string\"}],\"rows\":[{\"a\":\"ok\"}]}]}]}}"

```

**Rewst OpenAPI**

```bash
curl "<func-host>/api/openapi-rewst.json"
```

Expect **`"openapi": "3.0.3"`** and tier routes.

### Step 9 — Rewst integration and runbook record

1. In Rewst, add an **HTTP integration** with base URL **`func-host`** (HTTPS).
2. Import OpenAPI from **`https://<func-host>/api/openapi-rewst.json`** or from **`ExcelRenderer.Functions/openapi-rewst.json`** in the repo.
3. Set **`X-Api-Key`** on the integration to match **`RENDER_API_KEY`** (or the Key Vault–backed secret).
4. Build workflows per **[REWST_SUBWORKFLOW.md](./REWST_SUBWORKFLOW.md)**. Prefer **`/api/rewst/tier1/*`** and **`/api/rewst/tier2/*`** when using `payload_json`.

**Runbook / documentation (recommended):** Store in your team wiki or repo notes (not secrets): **Function App name**, **public base URL**, **Rewst integration name**, **which OpenAPI URL** you imported, **approximate date** of last OpenAPI refresh, and **who** rotates **`RENDER_API_KEY`**. After tenant moves or URL changes, update Rewst and this record.

### Step 10 — Application Insights monitoring and alerts (recommended)

Insights is usually attached during Function App creation. Use it for **logs**, **failures**, and **usage**.

**Recommended alerts** (tune thresholds to your environment):

| Signal | Why |
|--------|-----|
| **HTTP 5xx** rate or count | Catch regressions and dependency failures early. |
| **Sudden spike in requests** | Possible abuse or a runaway workflow. |
| **Failed requests** / **exceptions** | Surfaces cold-start issues and bad payloads. |

Create rules in **Azure Portal** → your **Application Insights** (or **Monitor** → **Alerts**) using KQL or metric criteria. [Azure Functions monitoring](https://learn.microsoft.com/azure/azure-functions/functions-monitoring) describes built-in integration.

### Step 11 — Ongoing: Microsoft Graph and SharePoint (if used)

- **Sites.Selected** grants are **per site**. After **site renames**, **migrations**, or **new libraries**, re-check grants in Entra and paths in workflows—see **[ENTRA_GRAPH_SETUP.md](./ENTRA_GRAPH_SETUP.md)**.
- **Rotate** `GRAPH_CLIENT_SECRET` in Entra and update Key Vault; app settings references usually stay the same if the secret **name** in Key Vault is unchanged (or update the URI).

### Step 12 — Optional: Restrict access to Rewst outbound IPs

Defense in depth **on top of** HTTPS and **`X-Api-Key`**: allowlist **Rewst’s static outbound NAT IPs** for your region.

**Source of truth (IPs can change):** [Rewst — Incoming and outgoing domains and IPs](https://docs.rewst.help/security/security-policy)

| Region | Outbound IPs (verify in doc) |
|--------|------------------------------|
| North America (US) | `13.58.15.14`, `18.218.107.198`, `3.139.170.31` |
| Europe (UK) | `18.132.221.226`, `18.171.196.2`, `3.9.62.134` |
| Europe (DE) | `3.67.166.23`, `3.69.14.222`, `3.76.128.23` |
| Australia | `13.210.158.91`, `13.237.143.171`, `52.65.55.149` |

**Consumption** plans **do not** support **inbound access restrictions** on the Function App itself. To enforce IP allowlists at the app, use **Premium (Elastic)** or **App Service (Dedicated)** and **Networking** → **Access restrictions**, or another **network design** your organization uses. Re-test workflows after any change.

**Operators:** Admins using a browser against `azurewebsites.net` may need different rules than Rewst traffic—plan accordingly.

---

## Path B — Bicep (optional)

Use this when you want **repeatable** deployments from the repo template.

### B.1 — Resource group

```bash
az group create --name <rg-name> --location australiaeast
```

### B.2 — Deploy `infra/main.bicep`

From the **repository root**:

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

| Parameter | Notes |
|-----------|--------|
| `namePrefix` | App Service plan name prefix (e.g. `excelrender`). |
| `functionAppName` | Hostname `https://<functionAppName>.azurewebsites.net`. |
| `storageAccountName` | Lowercase; globally unique. |
| `appInsightsName` | Globally unique. |
| `renderApiKey` | Plain parameter—fine for first deploy; **replace with Key Vault references** in the portal afterward for production (see [Step 5](#step-5--store-secrets-in-azure-key-vault-recommended)). |
| `defaultTableTheme` | Optional; default `TableStyleMedium2`. |

The template sets runtime, storage, Insights, and `RENDER_API_KEY`. It does **not** create Key Vault—add that separately.

### B.3 — Continue Path A from Step 4 onward

1. **Step 4** — Enable **managed identity** on the created Function App.  
2. **Step 5** — **Key Vault** and move secrets off plain app settings.  
3. **Steps 6–12** — App settings, deploy code, smoke tests, Rewst, monitoring, Graph maintenance, optional IP notes.

### Flex Consumption or custom plans

If you move the app to **Flex Consumption** or another SKU, the portal may manage **`FUNCTIONS_WORKER_RUNTIME`** differently. If deployment fails with conflicting worker settings, follow the portal’s guidance for that plan.

---

## Troubleshooting

| Symptom | Things to check |
|---------|------------------|
| **403** on API | `RENDER_API_KEY` matches `X-Api-Key`; Key Vault reference resolves (no error on **Configuration**). |
| **404** on `/api/...` | Code not published; wrong app name; check **Functions** in Portal. |
| **500** on first request | Cold start—retry; **Log stream** / Application Insights. |
| **OpenAPI empty or old** | **`openapi-rewst.json`** deployed with the DLL. Redeploy. |
| **Validation errors with `path`** | [ERROR_CODES.md](./ERROR_CODES.md). |
| **SharePoint / Graph 403** | [ENTRA_GRAPH_SETUP.md](./ENTRA_GRAPH_SETUP.md): consent, **Sites.Selected**, paths. |
| **Failure after IP allowlisting** | Correct Rewst region IPs; default deny rules; if you use a proxy, **X-Forwarded-For** behavior. |

## Operational notes

- **Consumption** scales to zero; first request after idle can be slow.  
- **Rotate** `RENDER_API_KEY` and update Rewst; update Key Vault secret values or versions as you use them.  
- Tune **`MAX_REQUEST_BYTES`** and **`MAX_ROWS_PER_SHEET`** for abuse protection.

---

**Disclaimer:** This guide is for convenience only; you are responsible for your Azure environment, costs, and security. No guaranteed support—see [README](./README.md).
