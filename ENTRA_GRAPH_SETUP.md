# Entra app + Microsoft Graph for SharePoint upload

This guide sets up the **enterprise application** used by **`POST /api/rewst/sharepoint/upload`** (client credentials → Graph → SharePoint document library).

Official references:

- [Limit app access to specific SharePoint site collections](https://learn.microsoft.com/graph/auth-limit-app-access-to-specific-site) (Sites.Selected overview)
- [Upload small files — driveItem](https://learn.microsoft.com/graph/api/driveitem-put-content?view=graph-rest-1.0) (PUT `/content`, up to 250 MB)
- [Create permission](https://learn.microsoft.com/graph/api/site-post-permissions) (grant the app access to a site when using **Sites.Selected**)

---

## 1. Create an app registration

1. Entra admin center → **Identity** → **Applications** → **App registrations** → **New registration**.  
2. **Name:** e.g. `Excel Renderer SharePoint Upload`.  
3. **Supported account types:** *Accounts in this organizational directory only* (single-tenant) unless you have a reason for more.  
4. Register. Note:
   - **Application (client) ID** → maps to **`GRAPH_CLIENT_ID`**
   - **Directory (tenant) ID** → **`GRAPH_TENANT_ID`**

---

## 2. Client secret (or certificate)

1. **Certificates & secrets** → **New client secret** → choose expiry → **Add**.  
2. Copy the **Value** immediately (shown once) → **`GRAPH_CLIENT_SECRET`** on the Function App.  
3. In production, prefer storing the secret in **Key Vault** and using a **Key Vault reference** in app settings.

Certificates are more secure for long-lived production apps; the Function App code uses client secret today—swap to certificate-based auth only if you extend the code.

---

## 3. API permissions (choose one strategy)

### Strategy A — **Sites.Selected** (least privilege, recommended)

1. **API permissions** → **Add a permission** → **Microsoft Graph** → **Application permissions**.  
2. Add **`Sites.Selected`**.  
3. **Grant admin consent** for the tenant (button: *Grant admin consent for …*).

**Important:** `Sites.Selected` alone does **not** grant access to any site until you **explicitly grant** the application permission to each SharePoint site (see [section 5](#5-grant-access-to-a-specific-site-sitesselected)).

### Strategy B — Broader write access (simpler, higher risk)

1. Application permissions such as **`Sites.ReadWrite.All`** or **`Files.ReadWrite.All`**.  
2. Admin consent.

This allows the app to reach many sites/libraries without per-site grants. Use only if your security review accepts it.

---

## 4. What this app does *not* use

- **Delegated** permissions and user sign-in — upload uses **application** permissions + **client credentials** only.  
- **Logic Apps / Power Automate** — not required for this API.

---

## 5. Grant access to a specific site (Sites.Selected)

After admin consent on **`Sites.Selected`**, grant the app access to each site that should receive uploads.

Microsoft documents the **site permissions** API, e.g. [Create permission — site](https://learn.microsoft.com/graph/api/site-post-permissions):

- Call **`POST https://graph.microsoft.com/v1.0/sites/{site-id}/permissions`** with a body that assigns your app’s service principal a role (e.g. write) on that site.

You need:

- **`{site-id}`** — Graph site id (same idea as **`site_id`** in the upload payload, or resolve from **`site_url`** via Graph).  
- The **service principal object id** for your app registration (**Enterprise applications** → your app → **Object ID**), used in the permission payload per Microsoft’s schema.

Exact JSON varies with API version; follow the **Create permission** article above and your tenant’s Graph Explorer tests.

Until this grant exists, uploads may fail with **403** / access denied from Graph even though the app has **`Sites.Selected`** at the tenant level.

---

## 6. Map to Azure Function App settings

| App setting | Source |
|-------------|--------|
| `GRAPH_TENANT_ID` | Entra **Overview** → Directory (tenant) ID |
| `GRAPH_CLIENT_ID` | App registration **Overview** → Application (client) ID |
| `GRAPH_CLIENT_SECRET` | Certificates & secrets → client secret **Value** |

Redeploy or restart the Function App after changing settings.

---

## 7. Verify

- **Token:** Client credentials against `https://graph.microsoft.com/.default` should succeed (Entra token endpoint).  
- **Upload:** Call **`POST /api/rewst/sharepoint/upload`** with header **`X-Api-Key`** (same value as **`RENDER_API_KEY`** on the Function App). Body uses the Rewst wrapper: **`payload_json`** string whose inner JSON includes a small test file as **`content_base64`**, plus valid **`site_id`** or **`site_url`**, **`drive_id`** or **`library_name`**, and a **`folder_path`** that already exists in the library.

If Graph returns **403**, re-check: admin consent, Sites.Selected **site** grant, correct drive/library, and that the folder path already exists in the library.

---

## 8. Related repo docs

- [SETUP.md](./SETUP.md) — deploy Function App, app settings, and **Key Vault** references for **`GRAPH_CLIENT_SECRET`** (recommended)  
- [ERROR_CODES.md](./ERROR_CODES.md) — upload error codes (`GRAPH_*`, `SITE_NOT_FOUND`, …)  
- [README.md](./README.md) — overview and Rewst routes  

No guaranteed support—see README disclaimer.
