# Engineering Standards

Design, code quality, testing, and decision-making rules for this project. All contributors follow these.

---

## Design

- **Readability first; performance when it matters.** If a less-readable path has significant performance gains, take it, but document why with a comment explaining the tradeoff.
- **Purist design over quick fixes.** Invest in correct abstractions upfront. Architectural debt that compounds should be prevented, not queued.
- **Interfaces over concrete types.** Depend on interfaces and abstractions, never concrete classes. Factories accept implementations; they don't construct them across layers.
- **Own the blast radius of your changes.** When you change something, know the full ripple and address it in the same PR. Don't leave broken dependents "to fix later" without an explicit decision to do so.

## Code Quality

- **XML doc comments on all public API.** Every public type and member gets a `///` doc comment stating purpose and contract, not a description of what the code does. `GenerateDocumentationFile` is enabled in every packable project, so these comments ship in the NuGet package and drive IntelliSense.

  ```csharp
  /// <summary>
  /// Aggregates multiple marking inputs into a single rendered output.
  /// </summary>
  /// <param name="markings">Raw marking strings to combine.</param>
  /// <returns>The aggregate result, including the rendered rollup marking.</returns>
  /// <exception cref="EzClassifyClientException">The server rejected the request or could not be reached.</exception>
  ```

- **Comments explain WHY, not WHAT.** Only comment when the next developer might not understand why you didn't do the obvious thing. Explain constraints, workarounds, hidden dependencies, not logic flow.

  ```csharp
  // netstandard2.0 has no HashCode.Combine; hand-rolled so Contract stays dependency-free.
  ```

- **Zero trust at public boundaries.** Validate all inputs on the public API and parse server responses defensively. Result parsers tolerate unknown response fields and optional projections; missing required correctness fields and malformed values are refused. The contract is open-response and closed-request, so wire enums are extensible structs over their raw token: a value a newer server adds decodes with `IsKnown == false`, keeps its exact token, and goes back unchanged in the next request. The server, not the client, rejects a request token it does not accept. `Parse` and `TryParse` stay strict for tokens a caller types in, and a required enum that arrives as null is still malformed input. Trust internal calls between private methods. Fail fast with clear errors at boundaries.
- **Nullable reference types everywhere.** `<Nullable>enable</Nullable>` in every project. Resolve nullability warnings by design (annotations, guards), never with the `!` null-forgiving operator in shipped source. Prefix intentionally unused parameters with `_`.
- **Records for wire models.** Model types are immutable C# records; equality is structural. Deeply recursive or engine-owned payloads stay `System.Text.Json.JsonElement` (opaque, carried through untouched, and nullable only when the member is optional) rather than being deep-modeled here. Every other contract component is read into the record that models it.
- **Generated and handwritten code stay separated.** The five wire-model `*.g.cs` files named in [`vendor/spec/MANIFEST.json`](vendor/spec/MANIFEST.json) are generated from the vendored OpenAPI and JSON Schemas by `gen.mjs`; they are committed, regenerated only via `make regen`, and never hand-edited. CI pins the generator and all template inputs by hash, verifies freshness, and rejects shadow declarations. Handwritten code is limited to the seams named in [`ARCHITECTURE.md`](ARCHITECTURE.md).
- **Formatting is `dotnet format`.** No custom style layer on top. CI runs `dotnet format --verify-no-changes` and fails on drift.

## Testing

- **Order: Contract → Tests → Implementation.** Write the XML doc contract first, tests second, implementation third. Tests are specs, not afterthoughts.
- **One responsibility per method.** Single purpose, clear `<param>`/`<returns>`, no side effects unless that's the method's entire point.
- **Test behavior, not coverage percentages.** Each test verifies a specific path: happy path, bad path, or defensive guard. Skip tests that add no value.
- **Separate test types strictly.**
  - **Unit:** in-process xUnit tests; HTTP is stubbed at the `HttpMessageHandler` seam or a loopback replayer, never the real network.
  - **Conformance:** replay the vendored cross-SDK plans ([`vendor/spec/plans/`](vendor/spec/plans/)) against recorded responses; these prove wire fidelity, not local logic.
  - **Live smoke:** opt-in via `EZC_SMOKE_URL` against a running rest-api; never required for `make test` to pass.
- **The public API surface is frozen.** A change to the public surface of the shipped assemblies must regenerate `PublicApiSurface.golden.txt` (`make api-surface`) deliberately in the same commit, never to silence the gate.
- **Bug fixes require regression tests.** Every fix needs a test that fails before the fix and passes after. No exceptions.

## Decision-Making

- **Stable contracts, incremental changes.** Preserve published API compatibility. Prefer incremental, reversible changes and follow semantic versioning for breaking changes.
- **Inline small debt, backlog large debt.** Found debt under 30 min effort: fix inline. Larger refactors, dependency upgrades, or architectural changes: log to backlog, don't auto-execute.
- **Don't let tasks balloon.** If you discover work that significantly exceeds the original scope, stop, log it, and stay on scope. Surface it for explicit prioritization.
