# EzClassify .NET Server Tour

A console tour of the main `EzClassifyClient` calls. It runs unchanged against the Cedar Valley replay server on your machine, then against your runtime.

Cedar Valley is a fictional school district policy for teaching. Use only synthetic content with it.

| Call | Description |
|--------|-------------|
| `root.PingAsync()` | Health check. Returns engine version, git tag, and git SHA. |
| `client.Scheme.ProcessAsync(marking)` | The author's call. Returns `ProcessResult`: the verdicts, fix paths, and the `Notices` list, already populated. |
| `client.Scheme.ValidateAsync(marking)` | The reader's call. Returns `ValidateResult`: whether the marking stands, whether it was right as written, and its `Errors`. |
| `client.Scheme.SuggestAsync(assertions)` | Returns `SuggestResult`: the `Suggestions` a picker may offer, and any `Warnings`. |
| `client.Scheme.AggregateAsync([...markings])` | Returns `AggregateResult` rollup, `Notices` included. |
| `client.Guide.ScanAsync(text)` | Scans free text against the guide. Returns each matching `GuideScanResult` rule with the keywords and spans that matched. |

## Start with the Cedar Valley replay server

The replay server answers over real HTTP with responses recorded from a real runtime for `cedar-valley@1.0.0`. You need the .NET 10 SDK (the version selected by the repository's [`global.json`](../../global.json)) and Node.js 22 or 24 for the server only. You do not need a runtime, a license or a credential.

```sh
npx @ezclassify/conformance-server --scenario cedar-valley --port 4010
```

Leave the server running. In a second terminal:

```sh
cd examples/server-tour
dotnet run
```

The tour defaults to `http://127.0.0.1:4010`, Context `cedar-valley`, version `1.0.0`. It prints a section per call. The first three look like this:

```text
  1. root.PingAsync()
  Engine version:        1.0.0-dev.30

  2. client.Scheme.ProcessAsync("CONFIDENTIAL")
  Rendered:              CONFIDENTIAL
  Valid:                 True
  Assertions:
    • CONFIDENTIAL (category: level            confidence: certain)
    • CEDAR_VALLEY_DISTRICT (category: allow_sharing_with confidence: inferred)

  3. client.Scheme.ProcessAsync("UNRECOGNIZED")
  Rendered:              UNRECOGNIZED
  Valid:                 False
  ✗ Choose a protection level.
```

The remaining sections show a validation, a picker's suggestions, a two-marking rollup and a guide scan, and the tour ends with a "Tour Complete" banner. The engine version comes from the server, so yours may differ from the line above.

The replay server answers a fixed set of recorded requests, so keep the tour's markings and scan text as they are. Any other request gets a 501 diagnostic, not an invented answer. If port 4010 is taken, start the server on another port and set `EZC_API_URL` to match.

If no server is reachable the tour exits after the ping attempt. `dotnet build` still succeeds, so compilation errors remain CI-visible.

## Switch to your runtime

Same code. Point the tour at your runtime with environment variables. Your platform team gives you the runtime URL, the Context ID and exact version that are published for your app, and the two credentials: the caller token that identifies your backend, and the signed Context token. Leave the token variables out if your runtime does not require them.

```sh
export EZC_API_URL=https://runtime.agency.example
export EZC_CONTEXT_ID=cedar-valley
export EZC_CONTEXT_VERSION=1.0.0
export EZC_ACCESS_TOKEN=<caller identity token>
export EZC_CONTEXT_TOKEN=<signed Context token>
dotnet run
```

A runtime evaluates any marking, so you are no longer limited to what the replay server recorded. Change the markings and scan text in [`Program.cs`](Program.cs) to ones your Context recognizes. Keep tokens out of source control and shell history; in your own application, read them from your secret store as the [SDK quick start](../../README.md#swap-to-your-runtime) shows.
