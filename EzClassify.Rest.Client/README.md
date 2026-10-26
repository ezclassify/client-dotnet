# EzClassify.Rest.Client

HTTP client for the EzClassify REST API, using `System.Net.Http` and
`System.Text.Json`. It depends on `EzClassify.Rest.Contract` for wire models
and uses no third-party HTTP stack.

## Install

```
dotnet add package EzClassify.Rest.Client
```

## Quick start

Start the Cedar Valley replay server, which serves recorded responses so you can prove your integration works before you have a runtime. Node.js 22 or 24 is needed only for this server.

```sh
npx @ezclassify/conformance-server --scenario cedar-valley --port 4010
```

```csharp
using EzClassify;

using var ctx = await EzClassifyClient.ConnectAsync(
    "http://127.0.0.1:4010", "cedar-valley", "1.0.0");

var result = (await ctx.Scheme!.ProcessAsync("INTERNAL")).Result;
Console.WriteLine($"{result.Rendered} valid={result.Valid}");
```

```text
INTERNAL valid=True
```

The server replays a finite set of recorded requests, such as the markings `PUBLIC`, `INTERNAL`, and `CONFIDENTIAL`. Anything unrecorded returns a 501 diagnostic.

## Swap to your runtime

Change the base URL and supply the tokens your runtime administrator issued. The calls stay the same.

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
var ping = await root.PingAsync();

// Bound client: operations run against one immutable Context version.
using var ctx = await EzClassifyClient.ConnectAsync(
    baseUrl, "your-context-id", "1.0.0", credentials);
var result = (await ctx.Scheme!.ProcessAsync("your marking")).Result;
Console.WriteLine($"{result.Rendered} {result.Valid}");
```

Failures surface as `EzClassifyClientException` with the HTTP status. When the server rate limits a request (429, which any operation can answer), `RetryAfter` holds the wait it asks for, read from the `Retry-After` header or the body's `retryAfter`. The client never retries by itself.

Every call finishes within the request timeout, the `HttpClient.Timeout` of the transport (100 seconds unless you supply an `HttpClient` set otherwise), including the full response body. An `HttpClient` with an infinite timeout, a common setup when Polly owns per-try timeouts, gets the 100 seconds too, and a resilience pipeline that gives up sooner still ends the call then. A call past the deadline throws `EzClassifyClientException` with a `TimeoutException` inside; canceling your own token throws `OperationCanceledException`.

Repeated scheme reads and access-rule lists share a bounded response cache on the bound client. Identical concurrent calls share one request, and canceling one caller's token leaves the others running. Failed calls and malformed 200 responses are never cached. A 200 that lacks a member the contract requires, at any depth, throws `JsonException` whose `Path` names the member (such as `$.result.assessment`), and the next call asks the server again. A concrete Context version keeps answers until eviction; an omitted version or `latest` bounds their lifetime to at most an hour and clears them when the resolved version changes. A 401 or the denial-shaped 404 `{"error":"not_found"}` clears them too. Use `ctx.ClearCache()` for a fresh read or pass `new ContextCacheOptions(Enabled: false)` when you connect to disable caching for that client. Access checks, crosswalk translation and document decisions call the server every time by default to preserve their audit records. `new ContextCacheOptions(CacheAccessDecisions: true)`, passed to `ConnectAsync` together with credentials, explicitly caches single-target checks under a concrete version and omits audit records for cache hits; connecting with it and `latest` or an omitted version throws `ArgumentException`. Translation and document decisions are never cached. Cache options are fixed when the client connects. Use one client instance for one caller identity.

`await ctx.DescribeContextAsync()` returns the Context's composition: the scheme, every scheme it holds, the guides, the access policy and the crosswalks, each with its declared and resolved version. It is read once and held, and `ClearCache()` does not drop it.

## License

Apache-2.0
