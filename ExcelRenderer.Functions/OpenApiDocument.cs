namespace ExcelRenderer.Functions;

internal static class OpenApiDocument
{
    internal const string Json = """
{
  "openapi": "3.0.3",
  "info": {
    "title": "Excel Renderer",
    "version": "1.1.2",
    "description": "Rewst-friendly API for validating and rendering Excel from JSON contracts."
  },
  "servers": [{ "url": "/" }],
  "paths": {
    "/api/render": {
      "post": {
        "summary": "Render JSON contract to Excel",
        "operationId": "renderExcel",
        "parameters": [
          {
            "name": "X-Api-Key",
            "in": "header",
            "required": false,
            "schema": { "type": "string" },
            "description": "Required when RENDER_API_KEY is configured."
          }
        ],
        "requestBody": {
          "required": true,
          "content": {
            "application/json": {
              "schema": { "$ref": "#/components/schemas/ContractPayload" },
              "example": {
                "schema_version": "1.0",
                "strict_mode": false,
                "delivery": { "format": "base64" },
                "defaults": {
                  "null_display": "-",
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
            }
          }
        },
        "responses": {
          "200": {
            "description": "Rendered workbook (binary mode) or JSON envelope (base64_json mode)",
            "content": {
              "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet": {
                "schema": { "type": "string", "format": "binary" }
              },
              "application/json": {
                "schema": { "$ref": "#/components/schemas/RenderJsonResponse" }
              }
            }
          },
          "400": {
            "description": "Validation/render error",
            "content": {
              "application/json": {
                "schema": { "$ref": "#/components/schemas/ValidationResponse" }
              },
              "text/plain": {
                "schema": { "type": "string" }
              }
            }
          },
          "403": { "description": "Missing or invalid API key" }
        }
      }
    },
    "/api/validate": {
      "post": {
        "summary": "Validate contract without rendering",
        "operationId": "validateContract",
        "parameters": [
          {
            "name": "X-Api-Key",
            "in": "header",
            "required": false,
            "schema": { "type": "string" }
          }
        ],
        "requestBody": {
          "required": true,
          "content": {
            "application/json": {
              "schema": { "$ref": "#/components/schemas/ContractPayload" }
            }
          }
        },
        "responses": {
          "200": {
            "description": "Validation result",
            "content": {
              "application/json": {
                "schema": { "$ref": "#/components/schemas/ValidationResponse" }
              }
            }
          },
          "400": {
            "description": "Invalid JSON or contract",
            "content": {
              "application/json": {
                "schema": { "$ref": "#/components/schemas/ValidationResponse" }
              }
            }
          },
          "403": { "description": "Missing or invalid API key" }
        }
      }
    },
    "/api/health": {
      "get": {
        "summary": "Health check",
        "operationId": "health",
        "responses": {
          "200": {
            "description": "ok",
            "content": { "text/plain": { "schema": { "type": "string", "example": "ok" } } }
          }
        }
      }
    }
  },
  "components": {
    "schemas": {
      "ContractPayload": {
        "type": "object",
        "properties": {
          "schema_version": { "type": "string", "example": "1.0" },
          "report_name": { "type": "string" },
          "file_name": { "type": "string" },
          "strict_mode": { "type": "boolean", "default": false },
          "response_mode": { "type": "string", "enum": ["binary", "base64_json"] },
          "delivery": { "$ref": "#/components/schemas/Delivery" },
          "defaults": { "type": "object", "additionalProperties": true },
          "workbook": { "type": "object", "additionalProperties": true, "description": "Tier-1 contract root." },
          "sources": { "type": "object", "additionalProperties": true, "description": "Tier-2 source map." },
          "sheets": { "type": "array", "items": { "type": "object", "additionalProperties": true }, "description": "Tier-2 sheet list." }
        },
        "additionalProperties": true,
        "description": "Supports both tier-1 (workbook/worksheets/blocks) and tier-2 (sources/sheets) contracts."
      },
      "Issue": {
        "type": "object",
        "required": ["code", "message"],
        "properties": {
          "code": {
            "type": "string",
            "enum": [
              "SRC_NOT_FOUND",
              "JOIN_KEY_MISSING",
              "PRIMARY_SOURCE_NOT_FOUND",
              "COL_OMITTED_EMPTY_SOURCE",
              "VALIDATION_PARSE_ERROR"
            ]
          },
          "message": { "type": "string" },
          "path": { "type": "string" }
        }
      },
      "ValidationResponse": {
        "type": "object",
        "required": ["valid", "errors", "warnings"],
        "properties": {
          "valid": { "type": "boolean" },
          "response_mode": { "type": "string", "enum": ["binary", "base64_json"] },
          "errors": { "type": "array", "items": { "$ref": "#/components/schemas/Issue" } },
          "warnings": { "type": "array", "items": { "$ref": "#/components/schemas/Issue" } }
        }
      },
      "RenderStats": {
        "type": "object",
        "properties": {
          "sheetCount": { "type": "integer" },
          "blockCount": { "type": "integer" },
          "rowCount": { "type": "integer" }
        }
      },
      "RenderJsonResponse": {
        "type": "object",
        "required": ["status", "file_name", "content_type", "content_base64", "warnings", "stats"],
        "properties": {
          "status": { "type": "string", "example": "ok" },
          "file_name": { "type": "string" },
          "content_type": { "type": "string", "example": "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" },
          "content_base64": { "type": "string", "format": "byte" },
          "warnings": { "type": "array", "items": { "$ref": "#/components/schemas/Issue" } },
          "stats": { "$ref": "#/components/schemas/RenderStats" }
        }
      },
      "Delivery": {
        "type": "object",
        "properties": {
          "format": { "type": "string", "enum": ["binary", "base64", "base64_json"], "default": "binary" }
        }
      }
    }
  }
}
""";
}
