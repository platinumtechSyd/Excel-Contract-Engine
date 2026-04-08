#!/usr/bin/env bash
# Smoke test against a running Functions host (e.g. func start).
# Usage:
#   export SMOKE_BASE_URL=http://localhost:7071   # optional
#   export SMOKE_API_KEY=<same as RENDER_API_KEY>
#   ./scripts/smoke-test.sh
set -euo pipefail

BASE_URL="${SMOKE_BASE_URL:-http://localhost:7071}"
BASE_URL="${BASE_URL%/}"
KEY="${SMOKE_API_KEY:-}"

if [[ -z "$KEY" ]]; then
  echo "Set SMOKE_API_KEY to match RENDER_API_KEY (e.g. from local.settings.json)." >&2
  exit 1
fi

BODY='{"schema_version":"1.0","workbook":{"worksheets":[{"name":"Sheet1","blocks":[{"type":"table","start_cell":"A1","columns":[{"key":"a","header":"A","type":"string"}],"rows":[{"a":"ok"}]}]}]}}'

echo "GET ${BASE_URL}/api/health"
code=$(curl -s -o /tmp/health.json -w "%{http_code}" "${BASE_URL}/api/health")
if [[ "$code" != "200" ]]; then echo "Expected 200 from health, got $code" >&2; exit 1; fi
grep -q '"status"' /tmp/health.json && grep -q 'auth_configured' /tmp/health.json || { cat /tmp/health.json; exit 1; }

echo "POST /api/validate without X-Api-Key (expect 403)"
code=$(curl -s -o /dev/null -w "%{http_code}" -X POST "${BASE_URL}/api/validate" \
  -H "Content-Type: application/json" -d "$BODY")
if [[ "$code" != "403" ]]; then echo "Expected 403 without API key, got $code" >&2; exit 1; fi

echo "POST /api/validate with X-Api-Key (expect 200)"
code=$(curl -s -o /tmp/val.json -w "%{http_code}" -X POST "${BASE_URL}/api/validate" \
  -H "Content-Type: application/json" -H "X-Api-Key: ${KEY}" -d "$BODY")
if [[ "$code" != "200" ]]; then echo "Expected 200 with API key, got $code" >&2; cat /tmp/val.json; exit 1; fi
grep -qE '"valid"( )*:( )*true' /tmp/val.json || { cat /tmp/val.json; exit 1; }

echo "Smoke test passed."
