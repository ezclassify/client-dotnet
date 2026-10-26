# Agent Instructions

## Standards

Read [`STANDARDS.md`](./STANDARDS.md) before any feature, refactor, architectural decision, or task > 30 minutes. These engineering standards apply to you as they do to any contributor. Skip for: quick bug fixes, typos, documentation-only changes.

Read [`ARCHITECTURE.md`](./ARCHITECTURE.md) before changing project boundaries,
dependency direction, generated-code ownership, the vendored contract, or the
public wire surface. It is the repository's executable ownership contract.

## Repository Layout

| Path | Purpose |
|------|---------|
| [`EzClassify.Rest.Contract/`](EzClassify.Rest.Contract/) | Wire models generated from the local [`vendor/spec/`](vendor/spec/) contract mirror, plus response parsers |
| [`EzClassify.Rest.Client/`](EzClassify.Rest.Client/) | HTTP client (`EzClassifyClient`) for the REST API |
| [`EzClassify.Rest.Tests/`](EzClassify.Rest.Tests/) | xUnit test suite; run via `make test` |
| [`examples/server-tour/`](examples/server-tour/) | Runnable demo showing `EzClassifyClient` against a live REST API |

## Build and Test

```sh
make build   # dotnet build EzClassify.Rest.sln --configuration Release
make test    # dotnet test  EzClassify.Rest.sln --configuration Release --filter "Category!=Live"
make check-architecture # declaration ownership and central package versions
make check-generated    # committed generated-source freshness
```

The [`examples/server-tour/`](examples/server-tour/) project is included in the solution; build failures there break `make build` and `make test`.

## Running the Demo

```sh
# Start the Cedar Valley replay server (Node 22/24), then run the tour against it.
npx @ezclassify/conformance-server --scenario cedar-valley --port 4010
# Defaults: http://127.0.0.1:4010, Context "cedar-valley", version 1.0.0.
# Override with EZC_API_URL, EZC_CONTEXT_ID, EZC_CONTEXT_VERSION for a real runtime.
cd examples/server-tour && dotnet run
```
