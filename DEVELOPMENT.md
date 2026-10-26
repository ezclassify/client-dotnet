# Developing the EzClassify .NET SDK

Environment setup, the build and test commands, and the spec-vendoring workflow
for working on this repo. For the contribution process (sign-off, issues, pull
requests, commit format), see [CONTRIBUTING.md](CONTRIBUTING.md).

## Setup

A clean machine does not have the .NET SDK. Install the **.NET 10 SDK** (the SDK,
not just the runtime): the
[official installers](https://dotnet.microsoft.com/download/dotnet/10.0),
`brew install dotnet-sdk` on macOS, or your distro's package. Verify with
`dotnet --version`. The generated `Models.g.cs` is committed, so building needs
only the .NET SDK; Node.js is required only for `make regen` after a spec change.

```sh
dotnet build
make test   # dotnet test --filter "Category!=Live"
```

## Building and Testing

Standard `dotnet` CLI against [`EzClassify.Rest.sln`](EzClassify.Rest.sln); the `Makefile` wraps the
same commands for parity with the other SDKs.

| Action | Native command | Notes |
|--------|----------------|-------|
| Build | `dotnet build` | or `make build`. Builds every project in the solution. |
| Generate API reference | `make docs` | Builds the solution and generates HTML at `artifacts/docs/index.html`. |
| Test | `dotnet test --filter "Category!=Live"` | or `make test` (`make test-verbose` for detailed output). `make smoke URL=<server-url>` runs the `Live` category against a running, content-configured REST deployment. |
| Regenerate generated models | `make regen` | Rewrites the committed `*.g.cs` models after a spec-update PR. Wrapper-only changes don't touch [`vendor/`](vendor/). |
| Format | `dotnet format` | Ships with the SDK; CI runs `dotnet format --verify-no-changes`. |
| Package the NuGet | `dotnet pack -c Release` | Produces the `.nupkg` under each project's `bin/Release/`. |
| Clean | `make clean` | `make distclean` also removes local SBOM output under `artifacts/`. |

## API reference

`make docs` uses the SDK selected by [`global.json`](global.json) and restores pinned DocFX
2.78.5 from [`docs/.config/dotnet-tools.json`](docs/.config/dotnet-tools.json). The initial tool restore requires
NuGet access. The [reference overview](docs/index.md) explains public API
coverage, generated wire models and native XML documentation outputs. DocFX
metadata in `docs/api` and HTML in `artifacts/docs` are ignored build outputs;
documentation generation requires no live REST deployment.

## What to Know Before Changing Things

- **The spec is vendored, not authored here.** The model records in
  `EzClassify.Rest.Contract` are generated from the OpenAPI spec under
  [`vendor/spec/`](vendor/spec/). That mirror is owned by [ezclassify/contracts](https://github.com/ezclassify/contracts) and written into
  this repo by the EzClassify spec bot;
  `vendor-readonly.yml` rejects a vendor commit from anyone else.
  Don't hand-edit the generated `Models.g.cs`; regenerate it with `make regen`
  (needs Node.js). CI regenerates and diffs it so drift cannot land.
  Wrapper-only changes don't touch [`vendor/`](vendor/). Spec changes belong upstream in
  [ezclassify/contracts](https://github.com/ezclassify/contracts); report them through the [central product tracker](https://github.com/ezclassify/ezclassify/issues/new/choose).
- **Engine behavior belongs to EzClassify Runtime.** This SDK handles HTTP
  transport and decoding. Report suspected evaluation defects through the
  [central product tracker](https://github.com/ezclassify/ezclassify/issues/new/choose),
  with a synthetic marking, the scheme and Context versions, and sanitized
  request/response details. Runtime source access is not required.
- **Conformance plans define parity.** The client is conformance-tested against
  the same vectors as the TypeScript engine, through a self-contained HTTP
  replayer (`make test`, no server needed). A change that breaks a conformance
  plan needs the plan changed in [ezclassify/contracts](https://github.com/ezclassify/contracts) first.
