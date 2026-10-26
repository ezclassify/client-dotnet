#!/usr/bin/env bash
# Regenerate committed C# wire models from the vendored contract.
# Run via `make regen` from the repo root.
set -euo pipefail

SCHEMAS_DIR="${1:-vendor/spec/json-schemas}"
SCRIPT="EzClassify.Rest.Contract/scripts/gen.mjs"
OUTFILE="EzClassify.Rest.Contract/Models.g.cs"
OPENAPI_FILE="vendor/spec/openapi.yaml"

if [ ! -d "${SCHEMAS_DIR}" ]; then
  echo "Error: schemas directory not found: ${SCHEMAS_DIR}" >&2
  echo "Expected vendor/spec/json-schemas. That tree is written by the spec bot from ezclassify/contracts; a missing one means the checkout is incomplete, not that it needs re-vendoring by hand." >&2
  exit 1
fi

node "${SCRIPT}" "${SCHEMAS_DIR}" "${OPENAPI_FILE}" "${OUTFILE}"

echo "Regenerated committed C# wire models"
