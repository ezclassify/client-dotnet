.PHONY: regen check-generated check-architecture test test-verbose smoke build clean distclean conformance-server api-surface check-api-surface notices check-notices

# Canonical JSON Schemas, committed to vendor/spec/json-schemas by the spec bot.
# `make regen` reads from here and rewrites the committed Models.g.cs; CI reruns
# the generator and diffs the tree, so a stale or hand-edited Models.g.cs fails CI.
VENDOR_SCHEMAS := vendor/spec/json-schemas
GENERATED_MODELS := EzClassify.Rest.Contract/Models.g.cs
GEN_SCRIPT := EzClassify.Rest.Contract/scripts/gen.mjs
DOTNET := dotnet

.PHONY: docs
# XML documentation is also emitted into each published library's build output.
docs: build
	cd docs && $(DOTNET) tool restore && $(DOTNET) tool run docfx docfx.json --warningsAsErrors

# There is deliberately no local re-vendor target. vendor/ is bot-owned: the
# bundle is mirrored out of ezclassify/contracts by the spec bot and lands as
# ezclassify-spec-bot. vendor-readonly.yml rejects a vendor/spec/ commit from
# any other identity, and DocumentSemanticConformanceTests requires the manifest
# to carry real hex digests plus a sourcePackages block, so a hand re-vendor
# cannot be committed green. To advance the spec, update contracts and let the
# spec bot propagate it, then run `make regen`.
regen:
	bash scripts/regen.sh "$(VENDOR_SCHEMAS)"

check-generated:
	node "$(GEN_SCRIPT)" "$(VENDOR_SCHEMAS)" vendor/spec/openapi.yaml "$(GENERATED_MODELS)" --check

check-architecture:
	node scripts/check-architecture.mjs

build:
	$(DOTNET) build EzClassify.Rest.sln --configuration Release

# The Live category needs a running server; `make smoke` runs it.
test:
	$(DOTNET) test EzClassify.Rest.sln --configuration Release --filter "Category!=Live"

test-verbose:
	$(DOTNET) test EzClassify.Rest.sln --configuration Release --filter "Category!=Live" --logger "console;verbosity=detailed" -m:1

# Requires a caller-provided licensed, content-configured REST deployment.
smoke:
	EZC_SMOKE_URL=$(URL) $(DOTNET) test EzClassify.Rest.sln --configuration Release --filter "Category=Live"

# 1.0 public-surface freeze. The public API of the two shipped assemblies is a
# locked contract; PublicApiSurfaceTests reflects it into a sorted text dump and
# diffs it against the committed golden EzClassify.Rest.Tests/PublicApiSurface.golden.txt.
# check-api-surface runs the gate (same command CI runs); api-surface rewrites the
# golden after a reviewed API change. A spec bump that changes generated models
# (a spec update followed by `make regen`) also changes the surface, so
# regenerate the golden in the same commit.
check-api-surface: build
	$(DOTNET) test EzClassify.Rest.sln --no-build --configuration Release --filter "FullyQualifiedName~PublicApiSurfaceTests"

api-surface: build
	UPDATE_API_SURFACE=1 $(DOTNET) test EzClassify.Rest.sln --no-build --configuration Release --filter "FullyQualifiedName~PublicApiSurfaceTests"
	@echo "Rewrote EzClassify.Rest.Tests/PublicApiSurface.golden.txt; review and commit it with your API change"

clean:
	find . -type d \( -name bin -o -name obj \) -exec rm -rf {} + 2>/dev/null || true

# distclean: clean plus everything derivable from a fresh clone: local CI
# outputs (SBOM). Models.g.cs is committed, so it survives distclean.
distclean: clean
	rm -rf artifacts

conformance-server: test-conformance-runner
	EZC_SMOKE_URL=$(URL) bash scripts/conformance-server.sh

# notices: refresh THIRD-PARTY-NOTICES.md from the transitive production
#   closure of the two published packages. check-notices runs the same
#   generation in --check mode and fails if the committed file is stale.
#   CI checks the license allowlist separately with:
#   python3 scripts/gen-notices.py check
notices:
	python3 scripts/gen-notices.py notices

check-notices:
	python3 scripts/gen-notices.py notices --check

# Pure helper tests for the explicit Python-backed live conformance lane.
.PHONY: test-conformance-runner
test-conformance-runner:
	PYTHONDONTWRITEBYTECODE=1 python3 -m unittest discover -s scripts -p test_conformance_server.py
