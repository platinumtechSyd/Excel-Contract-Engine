# Operational Status

| Field | Value |
|---|---|
| Classification | **Production / Live** |
| Production deployment | **Yes** |
| Platform | Azure Function App |
| Azure resource | `Rewst-Excel-Engine` |
| Production branch | `main` |
| Runtime | .NET 8 isolated |
| Deployment | GitHub Actions → Azure Functions; pushes to `main` deploy to the Production slot |
| Purpose | Excel rendering and delivery engine for Rewst contracts |
| Canonical repository | This repository |
| Supersedes | `SharepointUploader`, `report-renderer-function` |
| Operational warning | **Do not delete or repurpose this repository while `Rewst-Excel-Engine` is live.** |
| Last verified | 2026-09-17 |

## Notes

This repository is the production source for the Azure Function App above. The deployment workflow in `.github/workflows/main_rewst-excel-engine.yml` is the authoritative deployment path.

The older `SharepointUploader` and `report-renderer-function` repositories are superseded by this implementation and are cleanup candidates.
