using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace EzClassify.Tests;

public class ContextResponseCacheTests
{
    private const string ProcessResponse = """{"resolution":{"contextId":"c","resolvedVersion":"1.0.0","artifacts":[],"sources":[]},"operationLimitations":[],"result":{"valid":true,"rendered":"H","inputWasValid":true,"renders":{},"assessment":{"emitted":[],"omitted":[],"collapsed":[],"conflicts":[],"unrepresentable":[]},"assertions":[],"errors":[],"warnings":[],"appliedFixes":[],"proposedFixes":[],"notices":[]}}""";

    private sealed class SequenceHandler : HttpMessageHandler
    {
        private readonly Func<string, HttpResponseMessage> _reply;
        internal SequenceHandler(Func<string, HttpResponseMessage> reply) => _reply = reply;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken _) =>
            Task.FromResult(_reply(request.RequestUri?.AbsolutePath ?? ""));
    }

    private static HttpResponseMessage Response(string body, HttpStatusCode status = HttpStatusCode.OK, string version = "1.0.0")
    {
        var response = new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        response.Headers.TryAddWithoutValidation("X-EzClassify-Resolved-Version", version);
        return response;
    }

    [Fact]
    public async Task MalformedSuccessIsNotCachedAfterParserRejectsIt()
    {
        var processCalls = 0;
        using var http = new HttpClient(new SequenceHandler(path =>
            path.EndsWith("/scheme/process", StringComparison.Ordinal)
                ? Response(Interlocked.Increment(ref processCalls) == 1 ? "{}" : ProcessResponse)
                : Response(ContextProbe.Body)))
        { BaseAddress = new Uri("http://localhost:8080") };
        using var client = await EzClassifyClient.ConnectAsync(http, "c", "1.0.0");

        await Assert.ThrowsAsync<JsonException>(() => client.Scheme!.ProcessAsync("H"));
        Assert.Equal("H", (await client.Scheme!.ProcessAsync("H")).Result.Rendered);
        Assert.Equal("H", (await client.Scheme.ProcessAsync("H")).Result.Rendered);
        Assert.Equal(2, Volatile.Read(ref processCalls));
    }

    [Fact]
    public async Task UnauthorizedResponseClearsEarlierRead()
    {
        var processCalls = 0;
        using var http = new HttpClient(new SequenceHandler(path =>
        {
            if (path.EndsWith("/scheme/process", StringComparison.Ordinal))
            {
                Interlocked.Increment(ref processCalls);
                return Response(ProcessResponse);
            }
            return path.EndsWith("/scheme/validate", StringComparison.Ordinal)
                ? Response("{\"error\":\"expired\"}", HttpStatusCode.Unauthorized)
                : Response(ContextProbe.Body);
        }))
        { BaseAddress = new Uri("http://localhost:8080") };
        using var client = await EzClassifyClient.ConnectAsync(http, "c", "1.0.0");

        await client.Scheme!.ProcessAsync("H");
        await Assert.ThrowsAsync<EzClassifyClientException>(() => client.Scheme.ValidateAsync("H"));
        await client.Scheme.ProcessAsync("H");
        Assert.Equal(2, Volatile.Read(ref processCalls));
    }

    // A deployment running authentication answers an unauthenticated request with
    // this constant 404, so it refuses the caller as surely as a 401 does.
    [Theory]
    [InlineData("""{"error":"not_found"}""", 2)]
    [InlineData("""{ "error" : "not_found" }""", 2)]
    // A not-found that names what is missing is about the resource, not the caller.
    [InlineData("""{"error":"not_found","detail":"unknown preset"}""", 1)]
    [InlineData("""{"detail":"unknown preset","error":"not_found"}""", 1)]
    [InlineData("""{"error":"no_recording"}""", 1)]
    [InlineData("""{"error":["not_found"]}""", 1)]
    [InlineData("""["not_found"]""", 1)]
    [InlineData("not json", 1)]
    [InlineData("", 1)]
    public async Task NotFoundClearsEarlierReadOnlyWhenDenialShaped(string notFoundBody, int expectedProcessCalls)
    {
        var processCalls = 0;
        using var http = new HttpClient(new SequenceHandler(path =>
        {
            if (path.EndsWith("/scheme/process", StringComparison.Ordinal))
            {
                Interlocked.Increment(ref processCalls);
                return Response(ProcessResponse);
            }
            return path.EndsWith("/scheme/validate", StringComparison.Ordinal)
                ? Response(notFoundBody, HttpStatusCode.NotFound)
                : Response(ContextProbe.Body);
        }))
        { BaseAddress = new Uri("http://localhost:8080") };
        using var client = await EzClassifyClient.ConnectAsync(http, "c", "1.0.0");

        await client.Scheme!.ProcessAsync("H");
        var refusal = await Assert.ThrowsAsync<EzClassifyClientException>(() => client.Scheme.ValidateAsync("H"));
        Assert.Equal(404, refusal.StatusCode);
        await client.Scheme.ProcessAsync("H");
        Assert.Equal(expectedProcessCalls, Volatile.Read(ref processCalls));
    }

    [Fact]
    public async Task ResolvedVersionChangeClearsEarlierRead()
    {
        var processCalls = 0;
        using var http = new HttpClient(new SequenceHandler(path =>
        {
            if (!path.EndsWith("/scheme/process", StringComparison.Ordinal)) return Response(ContextProbe.Body);
            var count = Interlocked.Increment(ref processCalls);
            return Response(ProcessResponse, version: count == 1 ? "1.0.0" : "2.0.0");
        }))
        { BaseAddress = new Uri("http://localhost:8080") };
        using var client = await EzClassifyClient.ConnectAsync(http, "c", "latest");

        await client.Scheme!.ProcessAsync("H");
        await client.Scheme.ProcessAsync("L");
        await client.Scheme.ProcessAsync("H");
        Assert.Equal(3, Volatile.Read(ref processCalls));
        Assert.Equal("2.0.0", client.ResolvedVersion);
    }

    private const string Envelope = """{"resolution":{"contextId":"c","resolvedVersion":"1.0.0","artifacts":[],"sources":[]},"operationLimitations":[],"result":""";

    private const string Assessment = """{"emitted":[],"omitted":[],"collapsed":[],"conflicts":[],"unrepresentable":[]}""";

    // Answers every marking route with a well-formed result of its own shape and
    // counts the requests each route receives.
    private static (HttpClient Http, Dictionary<string, int> Calls) CountingTransport()
    {
        var calls = new Dictionary<string, int>(StringComparer.Ordinal);
        var http = new HttpClient(new SequenceHandler(path =>
        {
            var route = path.Substring(path.LastIndexOf('/') + 1);
            lock (calls) calls[route] = calls.TryGetValue(route, out var n) ? n + 1 : 1;
            var result = route switch
            {
                "process" => $$"""{"valid":true,"rendered":"H","inputWasValid":true,"renders":{},"assessment":{{Assessment}},"assertions":[],"errors":[],"warnings":[],"appliedFixes":[],"proposedFixes":[],"notices":[]}""",
                "validate" => $$"""{"valid":true,"rendered":"H","inputWasValid":true,"assessment":{{Assessment}},"assertions":[],"errors":[],"warnings":[],"appliedFixes":[],"notices":[]}""",
                "explain" => """{"input":"H","schemeId":"s","rendered":"H","entries":[],"repairs":[],"validationErrors":[],"inferences":[],"finalAssertions":[]}""",
                _ => null,
            };
            return result is null ? Response(ContextProbe.Body) : Response(Envelope + result + "}");
        }))
        { BaseAddress = new Uri("http://localhost:8080") };
        return (http, calls);
    }

    private static readonly Assertion Stated = new("level", "HIGH", Confidence.Certain, "stated");

    /// <summary>
    /// One variation per request field of the three marking calls. Each changes
    /// exactly that field from the base call, so a key that left the field out
    /// would hand the base call's answer back for it.
    /// </summary>
    public static TheoryData<string, string> FieldVariations() => new()
    {
        { "process", "outputs" }, { "process", "suggestFor" }, { "process", "outputFormat" },
        { "process", "inputFormat" }, { "process", "assertions" }, { "process", "surface" },
        { "process", "parts" }, { "process", "context" },
        { "validate", "outputFormat" }, { "validate", "inputFormat" }, { "validate", "surface" },
        { "validate", "parts" }, { "validate", "context" },
        { "explain", "outputFormat" }, { "explain", "inputFormat" }, { "explain", "assertions" },
        { "explain", "surface" }, { "explain", "parts" }, { "explain", "context" },
    };

    private static Task Call(EzClassifyClient client, string operation, string? field) => operation switch
    {
        "process" => client.Scheme!.ProcessAsync(
            "H",
            outputFormat: field == "outputFormat" ? "marking/long" : null,
            surface: field == "surface" ? "marking" : null,
            inputFormat: field == "inputFormat" ? "long" : null,
            assertions: field == "assertions" ? new[] { Stated } : null,
            contextJson: field == "context" ? """{"role":"part"}""" : null,
            parts: field == "parts" ? new[] { "(H)" } : null,
            outputs: field == "outputs" ? new[] { "marking/short" } : null,
            suggestFor: field == "suggestFor" ? new[] { "audience" } : null),
        "validate" => client.Scheme!.ValidateAsync(
            "H",
            surface: field == "surface" ? "marking" : null,
            outputFormat: field == "outputFormat" ? "marking/long" : null,
            inputFormat: field == "inputFormat" ? "long" : null,
            parts: field == "parts" ? new[] { "(H)" } : null,
            contextJson: field == "context" ? """{"role":"part"}""" : null),
        "explain" => client.Scheme!.ExplainAsync(
            "H",
            outputFormat: field == "outputFormat" ? "marking/long" : null,
            surface: field == "surface" ? "marking" : null,
            inputFormat: field == "inputFormat" ? "long" : null,
            assertions: field == "assertions" ? new[] { Stated } : null,
            contextJson: field == "context" ? """{"role":"part"}""" : null,
            parts: field == "parts" ? new[] { "(H)" } : null),
        _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null),
    };

    [Theory]
    [MemberData(nameof(FieldVariations))]
    public async Task EveryRequestFieldIsPartOfTheKey(string operation, string field)
    {
        var (http, calls) = CountingTransport();
        using (http)
        using (var client = await EzClassifyClient.ConnectAsync(http, "c", "1.0.0"))
        {
            await Call(client, operation, null);
            await Call(client, operation, null);
            Assert.Equal(1, calls[operation]);

            await Call(client, operation, field);
            Assert.Equal(2, calls[operation]);
            await Call(client, operation, field);
            Assert.Equal(2, calls[operation]);
        }
    }

    [Fact]
    public async Task ExplainAndProcessKeepSeparateEntriesForTheSameBody()
    {
        var (http, calls) = CountingTransport();
        using (http)
        using (var client = await EzClassifyClient.ConnectAsync(http, "c", "1.0.0"))
        {
            var processed = await client.Scheme!.ProcessAsync("H");
            var explained = await client.Scheme.ExplainAsync("H");
            await client.Scheme.ExplainAsync("H");

            Assert.Equal("H", processed.Result.Rendered);
            Assert.Equal("H", explained.Result.Input);
            Assert.Equal(1, calls["process"]);
            Assert.Equal(1, calls["explain"]);
        }
    }

    [Fact]
    public async Task ValidateGivenTextAndGivenFactsAreDifferentRequests()
    {
        var (http, calls) = CountingTransport();
        using (http)
        using (var client = await EzClassifyClient.ConnectAsync(http, "c", "1.0.0"))
        {
            await client.Scheme!.ValidateAsync("H");
            await client.Scheme.ValidateAsync(new[] { Stated });
            await client.Scheme.ValidateAsync(new[] { Stated });

            Assert.Equal(2, calls["validate"]);
        }
    }

    [Fact]
    public void KeyCanonicalizationSortsNestedObjectsButKeepsArrayOrder()
    {
        var first = ContextResponseCache.Canonicalize("""{"b":{"z":2,"a":1},"a":[2,1]}""");
        var reordered = ContextResponseCache.Canonicalize("""{"a":[2,1],"b":{"a":1,"z":2}}""");
        var different = ContextResponseCache.Canonicalize("""{"a":[1,2],"b":{"a":1,"z":2}}""");
        Assert.Equal(first, reordered);
        Assert.NotEqual(first, different);
    }

    [Fact]
    public async Task PendingRequestRemainsSharedPastItsTtl()
    {
        var cache = new ContextResponseCache();
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        async Task<string> Call(CancellationToken _)
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                entered.SetResult(true);
                await release.Task;
            }
            return "answer";
        }

        var first = cache.GetAsync("same", TimeSpan.FromMilliseconds(100), false, Call, CancellationToken.None);
        await entered.Task;
        await Task.Delay(120);
        var second = cache.GetAsync("same", TimeSpan.FromMilliseconds(100), false, Call, CancellationToken.None);
        Assert.Equal(1, Volatile.Read(ref calls));
        release.SetResult(true);
        Assert.Equal("answer", await first);
        Assert.Equal("answer", await second);
        Assert.Equal("answer", await cache.GetAsync("same", TimeSpan.FromMilliseconds(100), false, Call, CancellationToken.None));
        Assert.Equal(1, Volatile.Read(ref calls));
        await Task.Delay(120);
        Assert.Equal("answer", await cache.GetAsync("same", TimeSpan.FromMilliseconds(100), false, Call, CancellationToken.None));
        Assert.Equal(2, Volatile.Read(ref calls));
    }

    [Fact]
    public async Task OneCallerCancellationLeavesSharedWireRequestRunning()
    {
        var cache = new ContextResponseCache();
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        async Task<string> Call(CancellationToken token)
        {
            Interlocked.Increment(ref calls);
            entered.SetResult(true);
            await release.Task.WaitAsync(token);
            return "answer";
        }

        var first = cache.GetAsync("same", TimeSpan.FromMinutes(1), true, Call, CancellationToken.None);
        await entered.Task;
        using var source = new CancellationTokenSource();
        var second = cache.GetAsync("same", TimeSpan.FromMinutes(1), true, Call, source.Token);
        source.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second);
        release.SetResult(true);
        Assert.Equal("answer", await first);
        Assert.Equal(1, Volatile.Read(ref calls));
    }

    [Fact]
    public async Task LastCallerCancellationAbortsWireRequest()
    {
        var cache = new ContextResponseCache();
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var wireCanceled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var source = new CancellationTokenSource();
        var call = cache.GetAsync("same", TimeSpan.FromMinutes(1), true,
            async (CancellationToken token) =>
            {
                entered.SetResult(true);
                try { await Task.Delay(Timeout.Infinite, token); }
                catch (OperationCanceledException) { wireCanceled.SetResult(true); throw; }
                return "unreachable";
            }, source.Token);
        await entered.Task;
        source.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call);
        await wireCanceled.Task.WaitAsync(TimeSpan.FromSeconds(1));
    }
}
