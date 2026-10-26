# <img src="assets/icon.svg" alt="" width="32"> EzClassify .NET SDK

[![CI](https://github.com/ezclassify/client-dotnet/actions/workflows/ci.yml/badge.svg)](https://github.com/ezclassify/client-dotnet/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/EzClassify.Rest.Client?label=EzClassify.Rest.Client)](https://www.nuget.org/packages/EzClassify.Rest.Client)
[![license: Apache-2.0](https://img.shields.io/badge/license-Apache--2.0-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-netstandard2.0%20%7C%20net8.0-512BD4.svg)](EzClassify.Rest.sln)

Apache-licensed HTTP client for a licensed EzClassify REST runtime. It contains generated models and client ergonomics, not the proprietary engine or enterprise content. Most apps only need `EzClassify.Rest.Client`, which pulls in `EzClassify.Rest.Contract`.

The [ezclassify/contracts](https://github.com/ezclassify/contracts) repository owns
document and wire types; EzClassify Runtime owns
marking and access evaluation. This client owns HTTP transport, response decoding, caching, and
language-native conveniences. Evaluation operations address a published Context;
health and Context discovery use the deployment endpoint.

## Install

```sh
dotnet add package EzClassify.Rest.Client
```

`EzClassify.Rest.Contract` comes in as a dependency; add it directly if you only need the model records. Both packages target `netstandard2.0`, and `Client` also ships a `net8.0` build. CI builds and tests on .NET 10; the `netstandard2.0` output is what a .NET Framework 4.7.2+ host would load, but no job exercises it there.

## Quick start

Start the Cedar Valley replay server. It serves recorded responses for the Cedar Valley teaching Context, so you can prove your integration works before you have a runtime. Node.js 22 or 24 is needed only for this server, not for the client.

```sh
npx @ezclassify/conformance-server --scenario cedar-valley --port 4010
```

Then connect with the normal client. The Context is `cedar-valley`, version `1.0.0`, and the server asks for no credentials by default.

```csharp
using EzClassify;

using var ctx = await EzClassifyClient.ConnectAsync(
    "http://127.0.0.1:4010", "cedar-valley", "1.0.0");

var result = (await ctx.Scheme!.ProcessAsync("INTERNAL")).Result;
Console.WriteLine($"{result.Rendered} valid={result.Valid}");

var decision = (await ctx.Access!.CheckByMarkingAsync("CONFIDENTIAL", "{}")).Result;
Console.WriteLine($"granted={decision.Granted}");
```

```text
INTERNAL valid=True
granted=False
```

The server replays a finite set of recordings, so use inputs from its
[Cedar Valley catalog](https://github.com/ezclassify/contracts/tree/main/packages/conformance-server#testing-an-application-integration):
markings such as `PUBLIC`, `INTERNAL`, and `CONFIDENTIAL`, plus the recorded access, guide, and
aggregate requests. Anything unrecorded returns a 501 diagnostic rather than a computed answer.

### Swap to your runtime

Change the base URL and supply the credentials your runtime administrator issued: `EZC_ACCESS_TOKEN` (caller identity) and `EZC_CONTEXT_TOKEN` (signed Context credential). The calls stay the same. Use the Context id your runtime publishes, and omit the version to let the server choose: the token's default, or the newest published.

```csharp
using EzClassify;

// Supplied by the runtime administrator; keep tokens out of source control.
var credentials = new RestClientCredentials(
    Environment.GetEnvironmentVariable("EZC_ACCESS_TOKEN")
        ?? throw new InvalidOperationException("Set EZC_ACCESS_TOKEN."),
    Environment.GetEnvironmentVariable("EZC_CONTEXT_TOKEN")
        ?? throw new InvalidOperationException("Set EZC_CONTEXT_TOKEN."));
var baseUrl = Environment.GetEnvironmentVariable("EZC_API_URL")
    ?? throw new InvalidOperationException("Set EZC_API_URL.");

// Root client: health and Context discovery.
using var root = new EzClassifyClient(baseUrl, credentials);
Console.WriteLine($"server: {(await root.PingAsync()).EngineVersion}");
var contexts = await root.ListContextsAsync();

// Bound client: the Context composes scheme, guides, access, and crosswalks.
using var ctx = await EzClassifyClient.ConnectAsync(
    baseUrl, "your-context-id", version: null, credentials);
var result = (await ctx.Scheme!.ProcessAsync("your marking")).Result;
Console.WriteLine($"{result.Rendered} {result.Valid}");
Console.WriteLine($"resolved: {ctx.ResolvedVersion}");
```

Pass a concrete version to pin one, or omit the version to let the server choose: the token's default, or the newest published. An omitted version binds the same way as `latest`, and the concrete immutable version the server resolved is on `ResolvedVersion`. The bound groups are `Scheme`, `Guide`, `Access`, `Crosswalk`, and `Document`; they are null on the root discovery client, hence the `!`.

The bound client caches repeated scheme `Process`, `Validate`, `Explain`, `Suggest`, `SuggestForCategories`, `Aggregate`, `AggregateAssertions`, `Scan`, `Render`, `Normalize`, `DescribeConcept`, `Diff`, and `ApplyPreset` results, plus `Access.Rules`. Everything else reaches the server on every call: the guide operations, `CheckCoverage`, `Notices`, `FilterConcepts`, the scheme views and reads (`SummaryAsync`, `ConceptsAsync`, and the rest of the scheme resources), `DescribeRule`, `DescribeRequirements`, crosswalk translation, and document audit and assessment.

Every field of a request is part of its cache key, so two `ProcessAsync` calls that name different `outputs` or `suggestFor` are two entries. The operation is part of the key too: `ExplainAsync` and `ProcessAsync` never share an entry, and neither do `Access.CheckAsync` and `Access.CheckByMarkingAsync`. Identical concurrent calls share one request. Canceling one caller's `CancellationToken` settles only that caller; the request itself is canceled when its last caller has gone, and a cancellation after the answer arrived evicts nothing. A failed call is not cached, and neither is a 200 that is missing a member the contract requires: the next call retries. Results are read-only, so changing the collections in one caller's result cannot change what another caller, or a later cached read, receives.

Each client holds up to 500 results. A concrete Context version keeps results until eviction. An omitted version or `latest` bounds their lifetime instead: five minutes generally, two minutes for scan, and one hour for concept descriptions and access rules. A 401, the 404 with exactly `{"error":"not_found"}` that a deployment running authentication sends an unauthenticated caller, or a new resolved version on a `latest` binding clears the cache. A 404 that names a missing resource leaves it alone. Call `ctx.ClearCache()` for a fresh read, or pass `new ContextCacheOptions(Enabled: false)` when you connect to disable caching for that client.

`await ctx.DescribeContextAsync()` returns what the Context is made of: the composed scheme, every scheme it holds with its selector and roles, the guides, the access policy, and the crosswalks, each with the version the Context declares and the concrete version that answered. Record these when you need to say which artifacts a decision was made against. The description is read on first use and held for the life of the client, so `ClearCache()` does not drop it; on a `latest` binding it is read again after the composition moves.

Access and document decisions reach the server every time by default so each decision has its audit record. On a concrete Context version, connecting with `new ContextCacheOptions(CacheAccessDecisions: true)` explicitly caches the two single-target access checks. Cache hits then have no server audit record. Connecting with the option and `latest` (or no version) throws `ArgumentException`. Document audit and assessment, access requirements, and crosswalk translation are never cached, with or without the option.

```csharp
using var ctx = await EzClassifyClient.ConnectAsync(
    baseUrl, "your-context-id", "1.0.0", RestClientCredentials.None,
    new ContextCacheOptions(CacheAccessDecisions: true));
```

Cache options are fixed when the client connects. Scope each client to one caller identity, including when an HTTP handler supplies credentials from request context.

## API surface

| Operation | API |
|---|---|
| health / Context discovery | root `PingAsync`, `ListContextsAsync`, `ListContextVersionsAsync` |
| validate / process / explain / suggest / aggregate / scan | `Scheme.*` |
| render / normalize / diff / coverage / concept describe | `Scheme.*` |
| scheme reads: summary, concepts, categories, formats, grammar, scopes, label presets | `Scheme.SummaryAsync`, `ConceptsAsync`, `CategoriesAsync`, `FormatsAsync`, `GrammarAsync`, `ScopesAsync`, `LabelPresetsAsync` |
| access checks / requirements / rule discovery | `Access.*` |
| guide scan / evaluate / rule describe | `Guide.*` |
| crosswalk translation | `Crosswalk.TranslateAsync` |
| Context composition and component versions | `DescribeContextAsync` |
| document audit and access assessment | `Document.*` |

Results that describe a marking carry everything cheap to compute alongside it. `ValidateResult`, `ProcessResult` and `AggregateResult` each expose a `Notices` list, always present and empty when the marking carries none, so reading the notices for a marking does not take a second round trip. `Scheme.NoticesAsync` remains for the case where you hold assertions without a result.

Scheme operations that read a marking take their format choices as named options, under the same names the wire uses. `inputFormat` says what the input is read as: one of the scheme's formats, or for a scheme that declares surfaces, a view, a `surface/view` address, or one of a view's forms. Leave it out and recognition works it out, then reports what it read the text as in `Recognition.InputFormat` and, for a scheme that declares surfaces, whether the text is spelled as a whole or a part in `Recognition.Role`. `outputFormat` picks the output the result is written in. A scheme without surfaces defaults to its default format. A scheme that declares surfaces names its outputs `surface/view`, such as `sensitivity/short`, and leaving `outputFormat` out writes each surface in its default for the marking's role, so a part and a whole can come back in different views. `surface` names which of the scheme's scopes the input is, for a fragment such as a portion; leave it out and the input is treated as a whole marking. `RenderAsync` and `ApplyPresetAsync` only produce output, so they take `outputFormat` alone.

The defaults are in the scheme's metadata, so a picker does not have to guess them. For a scheme that declares surfaces, `SummaryAsync()` reports `DefaultPartRenderFormat` and `DefaultWholeRenderFormat`, and each entry `FormatsAsync()` returns lists in `DefaultFor` the roles it is the default for and in `Role` the role it is a view of. A scheme without surfaces has one default, `DefaultRenderFormat`, and its formats carry neither.

The rest of each request's optional fields are named options too, under their wire names: `assertions`, `outputs`, and `suggestFor` on `ProcessAsync`; `assertions` on `ExplainAsync`; and `includeGated` on both `SuggestAsync` overloads. Fields that carry a JSON object take it as a JSON string, the way `userAttributesJson` does, so `contextJson` on validate, process, explain, aggregate, and the access checks is forwarded as written. `ValidateAsync`, `ProcessAsync`, and `ExplainAsync` also take `parts`, the part markings the whole must cover: a part the whole does not cover is an error, and every proposed fix and rank floor is worked out against the whole and those parts together. An option you leave null stays off the wire and the server applies its default; `false` is sent.

The scheme reads answer with the view types a local engine's scheme view carries: `ConceptsAsync` returns `ConceptView`, `CategoriesAsync` returns `CategoryView`, `FormatsAsync` returns `FormatView`, `LabelPresetsAsync` returns `LabelPresetView`, and `ScopesAsync` returns `SchemeScope` entries in declaration order, empty when the scheme declares none. All but `LabelPresetsAsync` take an optional `scheme` key that selects one of the Context's schemes, such as a crosswalk's source or target. Label presets are the primary scheme's, the one `ApplyPresetAsync` expands them in, and the client sends no scheme key for either.

Requests are closed. The server answers 400 with code `INVALID_INPUT` to a query parameter or body field the operation does not declare, and `Detail` names it, as in `query: scheme`. The client sends only declared ones.

Errors surface as `EzClassifyClientException` with the HTTP status code when one was received. A 200 whose body the contract does not allow, such as one missing a required member at any depth, throws `JsonException` instead, and its `Path` names the member, such as `$.contexts` or `$.result.assessment`. That holds for every call, health and discovery included. Any operation can answer 429 when the rate limit is spent, and the wait the server asks for is on `RetryAfter`, a `TimeSpan?` read from the `Retry-After` header and, when that is absent, the body's `retryAfter`. It is null when the server named no wait. The client never waits or retries by itself, so how long to back off, and whether to retry at all, is your decision.

```csharp
try
{
    var result = (await ctx.Scheme!.ProcessAsync("INTERNAL")).Result;
}
catch (EzClassifyClientException error) when (error.StatusCode == 429)
{
    await Task.Delay(error.RetryAfter ?? TimeSpan.FromSeconds(1));
    // retry within your own limit
}
```

When you pass a base URL, the client creates and owns its `HttpClient`. On `net8.0` that client recycles pooled connections every two minutes so a long-lived process picks up a DNS change when the rest-api address moves. Pass your own `HttpClient`, or one from `IHttpClientFactory`, and that setting is yours.

Every call finishes within the request timeout, which is the `HttpClient.Timeout` of the transport: 100 seconds on the client it creates for a base URL, and whatever your own `HttpClient` is set to when you hand it over. The client reads it once, at construction. If your `HttpClient` has no timeout (`Timeout.InfiniteTimeSpan`), as in a common `IHttpClientFactory` setup where Polly or another resilience handler owns the per-try timeouts, the whole request still gets the same 100 seconds. The deadline covers the whole request: sending it, waiting for the response, and reading the full body, so a server that sends its headers and then stalls fails on time, and so does a handler that ignores cancellation. A call that runs past it throws `EzClassifyClientException` with a `TimeoutException` as its `InnerException`. Your own `CancellationToken` still wins when it fires first, and that surfaces as `OperationCanceledException`. A handler that gives up sooner wins the same way. When a resilience pipeline stops trying before the deadline, the call ends then, with the handler's own exception (Polly's `TimeoutRejectedException`, for example) or, if the handler lets its cancellation through, an `EzClassifyClientException` that says the transport canceled the request.

## Reading a marking and writing one

Two operations answer about one marking, and they are split by who is asking. The first two examples below run against the Cedar Valley replay server from the [quick start](#quick-start), and their output is what it answers. The third shows a composer's request, which the replay server has no recording for; run it against a live runtime.

`ValidateAsync` is the reader's call. Code that displays a marking, gates on it, stores it, or hands its facts to an access check calls it, with the text or with the facts a document states:

```csharp
using var ctx = await EzClassifyClient.ConnectAsync("http://127.0.0.1:4010", "cedar-valley", "1.0.0");

var read = (await ctx.Scheme!.ValidateAsync("INTERNAL")).Result;
Console.WriteLine($"{read.Rendered} valid={read.Valid} asWritten={read.InputWasValid}");
foreach (var change in read.AppliedFixes)
    Console.WriteLine($"  {change.Message}");
```

```
INTERNAL valid=True asWritten=True
  Rule "nonpublic-is-internal-by-default": added "CEDAR_VALLEY_DISTRICT" to category "allow_sharing_with"
```

`Valid` says the marking stands once the engine has settled it; `InputWasValid` says it was right as written; `AppliedFixes` lists every change settling made, here the sharing default the policy adds. The client reports both exactly as the server judged them. `ValidateAsync` also takes an `IEnumerable<Assertion>` for a marking given as facts. Pass facts only as a document or an author stated them: the `Assertions` of an earlier result are already settled, and validating those can call a marking valid whose text was not.

`ProcessAsync` is the author's call. It answers everything `ValidateAsync` does, settled the same way, plus what someone writing the marking needs: the corrections to the text in `Recognition.Corrections`, the fix paths the server has proven, a rendering for each output you name, and picker options for the categories you name.

```csharp
var draft = (await ctx.Scheme.ProcessAsync("INTERNAL//FINANCIAL INFORMATION")).Result;
Console.WriteLine($"{draft.Rendered} valid={draft.Valid}");
foreach (var error in draft.Errors)
    Console.WriteLine($"  {error.Message}");
foreach (var fix in draft.ProposedFixes)
    Console.WriteLine($"  fix: {fix.Rendered}");
```

```
INTERNAL//FINANCIAL INFORMATION valid=False
  Financial information is confidential at minimum.
  fix: CONFIDENTIAL//FINANCIAL INFORMATION
  fix: INTERNAL
```

A composer sends what was typed together with the facts already picked, names the outputs it previews, and asks for the options of the pickers it shows, all in one request:

```csharp
var typed = "FINANCIAL INFORMATION";
var pickedSoFar = new[] { new Assertion("level", "CONFIDENTIAL", Confidence.Certain, "user") };

var keystroke = (await ctx.Scheme.ProcessAsync(
    typed,
    assertions: pickedSoFar,
    outputs: new[] { "long" },
    suggestFor: new[] { "level" })).Result;

var preview = keystroke.Renders["long"].Rendered;
var options = keystroke.SuggestionsFor!["level"].Suggestions;
```

`Renders` holds only the outputs you name, and `SuggestionsFor` is null unless you name categories. Outputs are spelled the way the scheme declares them: a format id here, a `surface/view` address for a scheme that declares surfaces. `SuggestForCategoriesAsync` answers the same map on its own, for a picker that needs options without processing the marking.

An error about text the engine could not read may carry `DidYouMean`, the ids of the concepts the text was likely meant as, for a program to act on. The message says the same in words. It is a hint, not a fix: only `ProposedFixes` are proven.

`ExplainAsync` takes what `ProcessAsync` takes, apart from `outputs` and `suggestFor`, and returns the trace instead: what was read, repaired, inferred and found wrong, and what was written. It is for the moment someone asks why.

`SuggestAsync` takes either text or an `IEnumerable<Assertion>`, as separate overloads. An empty string and an empty sequence are valid supplied inputs; null is refused before transport. Only the text overload accepts `inputFormat`. The facts overload sends `assertions` without synthesizing `input` or `inputFormat`. Both forward `outputFormat`, `filter`, `surface`, and `includeGated` unchanged.

`Assertion.Source` is an open string. Labels such as `stated` and private host labels are preserved alongside the values and provenance supplied by the caller. The engine assigns `infer`, `default`, `context`, and `repair` when appropriate; a `sourceRule` records rule derivation. The client forwards these fields without applying local classification policy.

A failed Context binding and a missing concept both preserve the server's HTTP status and full `ResponseBody` in `EzClassifyClientException`. `Code` and `Detail` retain the operation-specific refusal, so a `conceptId: NOPE` detail is not treated as a missing Context. A resource refusal retains cached answers; a 401 or a denial-shaped 404 clears them.

## Versioning

SDK `major.minor` tracks the rest-api `major.minor`; patch versions may drift. `client.EngineSpecVersion` is the spec version this SDK was built against, `await client.PingAsync()` returns the live server version, and the first mismatch writes a one-time notice to `System.Diagnostics.Trace`, which any trace listener can route into your own logging. The shared conformance corpus checks request serialization and response decoding across clients. Acceptance against a live deployment is a separate lane.

## Demo

[`examples/server-tour`](examples/server-tour/README.md) walks the main client calls and runs as written against the Cedar Valley replay server from the [quick start](#quick-start). It requires the .NET 10 SDK. Its README shows the expected output and how to point it at your runtime.

## Integration testing

The [quick start](#quick-start) server also serves what your application needs to test connectivity, response handling, and access-decision logging. See the
[replay server guide](https://github.com/ezclassify/contracts/tree/main/packages/conformance-server#testing-an-application-integration)
for the full request catalog, CORS options for browser UIs, and credential testing with `--auth simulated`.

A denied access decision is a successful response with `granted: false`; handle transport or authentication failures separately. The access example below logs the outcome and rule explanations without copying subject attributes or credentials. In your backend, derive attributes from trusted identity data and add your application's correlation ID and resource reference. Apply your log retention and redaction policy to returned explanations too.

```csharp
using EzClassify;

try
{
    using var ctx = await EzClassifyClient.ConnectAsync(
        "http://127.0.0.1:4010", "cedar-valley", "1.0.0");
    var decision = (await ctx.Access!.CheckByMarkingAsync("CONFIDENTIAL", "{}")).Result;
    Console.WriteLine($"granted={decision.Granted}");
    foreach (var rule in decision.Rules)
        Console.WriteLine($"{rule.Rule.RuleId} {rule.Granted} {rule.Reason}");
}
catch (EzClassifyClientException error)
{
    Console.Error.WriteLine($"Integration request failed: {error.StatusCode} {error.Message}");
}
```

Restart with `--scenario transient-error`, `rate-limit`, `unavailable`,
`slow-response`, `disconnected`, `malformed-json`, or `malformed-envelope` to
exercise failures on the recorded process request. Clear the client response
cache or create a new client between runs so an earlier answer does not hide
the scenario. The guide lists authentication, validation, empty-result, and
complex access cases. These recordings test your integration, not the
correctness of a production authorization policy.

## Reporting issues

Issues are tracked centrally for the whole EzClassify family. File bugs and feature requests, or ask a question, at [github.com/ezclassify/ezclassify](https://github.com/ezclassify/ezclassify) (Issues and Discussions). For vulnerabilities, follow the [security policy](https://github.com/ezclassify/ezclassify/blob/main/SECURITY.md) instead.

## Links

The [API reference overview](docs/index.md) describes the two shipped libraries
and generated wire models. Their XML documentation also supports IntelliSense.

Website and runtime access: [ezclassify.com](https://ezclassify.com) · Contributing: [CONTRIBUTING.md](CONTRIBUTING.md) · Security: [policy](https://github.com/ezclassify/ezclassify/blob/main/SECURITY.md) · Engineering standards: [STANDARDS.md](./STANDARDS.md)

## License

[Apache License 2.0](LICENSE) © EzClassify, LLC
