using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace EzClassify.Tests;

/// <summary>What one call settled with: a result, or the failure it raised.</summary>
internal sealed record CachePlanOutcome(string Operation, string Case, object? Value, Exception? Error);

/// <summary>
/// Runs one plan from the vendored cache plans (<c>vendor/spec/cache/v1/plans.json</c>)
/// against a counting replay transport.
///
/// <para>One cache-enabled client serves every step of the plan. It is built the
/// way an integrator builds it, with the cache options given at connect, holds a
/// static Context carrier and is connected before the first step; connect traffic
/// is not counted. The cache reads a test clock the plan advances. Each caller of
/// a concurrent step is a real call through the public method, and the runner
/// observes that every one has joined the in-flight entry before the step ends.</para>
/// </summary>
internal sealed class CachePlanRunner : IAsyncDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);
    private static readonly RestClientCredentials Carrier = new(null, "fixture-context-carrier");
    private const string PinnedVersion = "1.0.0";

    private readonly JsonNode _plan;
    private readonly WireCorpus _corpus;
    private readonly string _denialCase;
    private readonly CachePlanTransport _transport;
    private readonly HttpClient _http;
    private readonly CachePlanClock _clock = new();
    private readonly EzClassifyClient _client;
    private readonly Dictionary<string, Task<CachePlanOutcome>> _callers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CancellationTokenSource> _cancels = new(StringComparer.Ordinal);

    private CachePlanRunner(JsonNode plan, WireCorpus corpus, string denialCase, CachePlanTransport transport, HttpClient http, EzClassifyClient client)
    {
        _plan = plan;
        _corpus = corpus;
        _denialCase = denialCase;
        _transport = transport;
        _http = http;
        _client = client;
        _client.CacheClock = _clock.Now;
    }

    /// <summary>
    /// Build the plan's client. A plan whose client block says <c>construction: refused</c>
    /// has no steps; building must fail with the transport untouched, and the
    /// returned runner is null.
    /// </summary>
    public static async Task<CachePlanRunner?> StartAsync(JsonNode plan, WireCorpus corpus, string denialCase)
    {
        var block = plan["client"]!;
        var binding = block["binding"]!.GetValue<string>();
        // A client with no version asks for latest by name, which is also how a
        // token-defaulted binding reaches the server from this SDK: it always
        // sends the version segment, and the carrier lets the server settle it.
        string? version = binding == "pinned" ? PinnedVersion : null;
        var context = block["context"]?.GetValue<string>() ?? "ez-full";
        var options = new ContextCacheOptions(Enabled: true, CacheAccessDecisions: block["cacheAccessDecisions"]?.GetValue<bool>() ?? false);

        var transport = new CachePlanTransport(corpus, version ?? "latest");
        var http = new HttpClient(transport) { BaseAddress = new Uri("http://plan.invalid/") };
        if (block["construction"]?.GetValue<string>() == "refused")
        {
            using (http)
            {
                await Assert.ThrowsAsync<ArgumentException>(
                    () => EzClassifyClient.ConnectAsync(http, context, version, Carrier, options));
                Assert.True(transport.Total == 0, "a refused client must fail at construction, before any request");
            }
            return null;
        }

        var client = await EzClassifyClient.ConnectAsync(http, context, version, Carrier, options);
        transport.ResetCounts();
        return new CachePlanRunner(plan, corpus, denialCase, transport, http, client);
    }

    /// <summary>Execute every step, in order.</summary>
    public async Task RunAsync()
    {
        foreach (var step in _plan["steps"]!.AsArray())
            await ExecuteAsync(step!);
        Assert.True(_transport.Misses.Count == 0, "the client sent requests no recorded case matches:\n" + string.Join("\n", _transport.Misses));
    }

    private async Task ExecuteAsync(JsonNode step)
    {
        switch (step["step"]!.GetValue<string>())
        {
            case "call": await CallAsync(step); break;
            case "concurrent": await ConcurrentAsync(step); break;
            case "gate": _transport.Gate(Case(step)); _gatedCases.Add(Case(step)); break;
            case "release": await ReleaseAsync(Case(step)); break;
            case "cancel": await CancelAsync(step["caller"]!.GetValue<string>()); break;
            case "advanceClock": _clock.Advance(step["ms"]!.GetValue<long>()); break;
            case "respond": Respond(step); break;
            case "quiesce": await WaitUntilAsync(() => _client.PendingCacheCallers == 0, "background work to finish"); break;
            case "mutateResult": await MutateAsync(step["caller"]!.GetValue<string>()); break;
            case "expectOk": await ExpectOkAsync(step); break;
            case "expectError": await ExpectErrorAsync(step); break;
            case "expectWireCount": ExpectWireCount(step); break;
            case "expectWireAborted": ExpectWireAborted(step); break;
            case "expectIndependentResults": await ExpectIndependentAsync(step); break;
            default: throw new InvalidOperationException($"unknown step {step["step"]}");
        }
    }

    private static string Case(JsonNode step) => step["case"]!.GetValue<string>();

    private Task<CachePlanOutcome> Invoke(JsonNode step, CancellationToken token)
    {
        var variant = step["variant"]?.GetValue<string>();
        if (variant == "explicitNullMember")
            throw new InvalidOperationException("explicitNullMember is not applicable to this SDK; the plan must not be run");
        return CachePlanCalls.InvokeAsync(
            _client, _corpus, step["operation"]!.GetValue<string>(), Case(step),
            reorder: variant == "reorderedMembers", token);
    }

    private async Task CallAsync(JsonNode step)
    {
        CachePlanOutcome? last = null;
        var token = step["preCanceled"] is not null ? new CancellationToken(canceled: true) : CancellationToken.None;
        for (var i = 0; i < (step["times"]?.GetValue<int>() ?? 1); i++)
        {
            last = await Settle(Invoke(step, token));
            if (step["expect"] is JsonNode expect) AssertError(last, expect["error"]!);
            else AssertOk(last, "call");
        }
        if (step["caller"] is JsonNode name) _callers[name.GetValue<string>()] = Task.FromResult(last!);
    }

    private async Task ConcurrentAsync(JsonNode step)
    {
        var callers = step["callers"]!.AsArray().Select(n => n!.GetValue<string>()).ToList();
        var joined = _client.PendingCacheCallers;
        var cancelable = step["cancelable"]?.AsArray().Select(n => n!.GetValue<string>()).ToHashSet() ?? new HashSet<string>();
        foreach (var caller in callers)
        {
            var token = CancellationToken.None;
            if (cancelable.Contains(caller))
            {
                var source = new CancellationTokenSource();
                _cancels[caller] = source;
                token = source.Token;
            }
            // The call runs on this thread up to its first real wait, which is
            // after it has joined or created the cache entry.
            _callers[caller] = Settle(Invoke(step, token));
        }

        var recorded = Case(step);
        if (_gatedCases.Contains(recorded))
        {
            // The held request has reached the transport and every caller is
            // waiting on the one entry, observed rather than assumed.
            await WaitUntilAsync(() => _transport.HeldCount(recorded) >= 1, "the held request to reach the transport");
            await WaitUntilAsync(() => _client.PendingCacheCallers >= joined + callers.Count, "every caller to join the in-flight entry");
        }
        else
        {
            await WithinPatience(Task.WhenAll(callers.Select(c => _callers[c])), "the callers to settle");
        }
    }

    private readonly HashSet<string> _gatedCases = new(StringComparer.Ordinal);

    private async Task ReleaseAsync(string caseId)
    {
        _transport.Release(caseId);
        _gatedCases.Remove(caseId);
        await WithinPatience(Task.WhenAll(_callers.Values), "the callers to settle after the release");
    }

    private async Task CancelAsync(string caller)
    {
        _cancels[caller].Cancel();
        // The caller settles only after it has walked away from the entry, and the
        // last one out cancels the wire request before it does.
        await WithinPatience(_callers[caller], $"caller {caller} to settle");
    }

    private void Respond(JsonNode step)
    {
        switch (step["synthetic"]!.GetValue<string>())
        {
            case "malformed-success":
                _transport.ScriptMalformedSuccess(Case(step), step["removeMembers"]!.AsArray().Select(n => n!.GetValue<string>()).ToList());
                break;
            case "denial-404":
                _transport.ScriptDenial(Case(step), _denialCase);
                break;
            case "resolved-version":
                _transport.MoveResolvedVersion(step["version"]!.GetValue<string>());
                break;
            default:
                throw new InvalidOperationException($"unknown synthetic response {step["synthetic"]}");
        }
    }

    private async Task MutateAsync(string caller)
    {
        var outcome = await _callers[caller];
        AssertOk(outcome, $"caller {caller}");
        CachePlanMutator.Mutate(outcome.Value!);
    }

    private async Task ExpectOkAsync(JsonNode step)
    {
        foreach (var caller in step["callers"]!.AsArray().Select(n => n!.GetValue<string>()))
            AssertOk(await WithinPatience(_callers[caller], $"caller {caller} to settle"), $"caller {caller}");
    }

    private async Task ExpectErrorAsync(JsonNode step)
    {
        foreach (var caller in step["callers"]!.AsArray().Select(n => n!.GetValue<string>()))
            AssertError(await WithinPatience(_callers[caller], $"caller {caller} to settle"), step["error"]!);
    }

    private void ExpectWireCount(JsonNode step) =>
        Assert.Equal(step["count"]!.GetValue<int>(), _transport.Count(Case(step)));

    private void ExpectWireAborted(JsonNode step) =>
        Assert.Equal(step["aborted"]!.GetValue<bool>(), _transport.Aborted(Case(step)));

    /// <summary>
    /// Each caller's result must equal a fresh decode of the response it was
    /// answered from. The fresh decode is the same public call on a client with
    /// the cache off, over its own transport, so it reads the recorded bytes
    /// without anything the plan did to the shared client.
    /// </summary>
    private async Task ExpectIndependentAsync(JsonNode step)
    {
        foreach (var caller in step["callers"]!.AsArray().Select(n => n!.GetValue<string>()))
        {
            var outcome = await _callers[caller];
            AssertOk(outcome, $"caller {caller}");
            var expected = await FreshDecodeAsync(outcome);
            Assert.Equal(expected, CachePlanMutator.Snapshot(outcome.Value!));
        }
    }

    private async Task<string> FreshDecodeAsync(CachePlanOutcome outcome)
    {
        var transport = new CachePlanTransport(_corpus, PinnedVersion);
        using var http = new HttpClient(transport) { BaseAddress = new Uri("http://plan.invalid/") };
        var context = _plan["client"]!["context"]?.GetValue<string>() ?? "ez-full";
        using var client = await EzClassifyClient.ConnectAsync(
            http, context, PinnedVersion, Carrier, new ContextCacheOptions(Enabled: false));
        var fresh = await CachePlanCalls.InvokeAsync(client, _corpus, outcome.Operation, outcome.Case, reorder: false, CancellationToken.None);
        AssertOk(fresh, "fresh decode");
        return CachePlanMutator.Snapshot(fresh.Value!);
    }

    private static async Task<CachePlanOutcome> Settle(Task<CachePlanOutcome> call) => await call.ConfigureAwait(false);

    private static void AssertOk(CachePlanOutcome outcome, string who) =>
        Assert.True(outcome.Error is null, $"{who} ({outcome.Operation} {outcome.Case}) failed: {outcome.Error}");

    private static void AssertError(CachePlanOutcome outcome, JsonNode error)
    {
        Assert.True(outcome.Error is not null, $"{outcome.Operation} {outcome.Case} was expected to fail but returned a result");
        var kind = error["kind"]!.GetValue<string>();
        switch (kind)
        {
            case "http":
                var refusal = Assert.IsType<EzClassifyClientException>(outcome.Error);
                Assert.Equal(error["status"]!.GetValue<int>(), refusal.StatusCode);
                break;
            case "aborted":
                Assert.IsAssignableFrom<OperationCanceledException>(outcome.Error);
                break;
            case "malformed-response":
                Assert.IsType<JsonException>(outcome.Error);
                break;
            default:
                throw new InvalidOperationException($"unknown error kind {kind}");
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition, string what)
    {
        var deadline = DateTime.UtcNow + Patience;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException($"timed out waiting for {what}");
            await Task.Delay(1);
        }
    }

    private static async Task<T> WithinPatience<T>(Task<T> task, string what)
    {
        if (await Task.WhenAny(task, Task.Delay(Patience)) != task) throw new TimeoutException($"timed out waiting for {what}");
        return await task;
    }

    private static async Task WithinPatience(Task task, string what)
    {
        if (await Task.WhenAny(task, Task.Delay(Patience)) != task) throw new TimeoutException($"timed out waiting for {what}");
        await task;
    }

    public ValueTask DisposeAsync()
    {
        foreach (var source in _cancels.Values) source.Dispose();
        _client.Dispose();
        _http.Dispose();
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// Changes a result in place as far as the language allows, including a nested
/// collection: a list is cleared, an array's elements are overwritten, a map is
/// emptied. A collection that refuses the change, as a read-only wrapper does,
/// satisfies the step.
/// </summary>
internal static class CachePlanMutator
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = false };

    /// <summary>The result's wire form, which is what independence is measured on.</summary>
    public static string Snapshot(object value) => JsonSerializer.Serialize(value, value.GetType(), Options);

    /// <summary>Mutate every collection reachable from <paramref name="root"/>; returns how many accepted the change.</summary>
    public static int Mutate(object root)
    {
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var changed = 0;
        Walk(root, seen, ref changed);
        return changed;
    }

    private static void Walk(object? value, HashSet<object> seen, ref int changed)
    {
        if (value is null || value is string || value is JsonElement) return;
        var type = value.GetType();
        if (type.IsPrimitive || type.IsEnum) return;
        // Framework value types (dates, spans) hold nothing a caller could share.
        if (value is not IEnumerable && type.Namespace is { } ns && ns.StartsWith("System", StringComparison.Ordinal)) return;
        if (!type.IsValueType && !seen.Add(value)) return;

        if (value is IDictionary map)
        {
            foreach (DictionaryEntry entry in map) Walk(entry.Value, seen, ref changed);
            if (!map.IsReadOnly) { map.Clear(); changed++; }
            return;
        }
        if (value is IList list)
        {
            foreach (var item in list) Walk(item, seen, ref changed);
            if (list.IsFixedSize && !list.IsReadOnly)
            {
                // An array cannot shrink, so overwrite what it holds.
                for (var i = 0; i < list.Count; i++)
                    if (!type.GetElementType()!.IsValueType) { list[i] = null; changed++; }
            }
            else if (!list.IsReadOnly)
            {
                list.Clear();
                changed++;
            }
            return;
        }
        if (value is IEnumerable sequence)
        {
            foreach (var item in sequence) Walk(item, seen, ref changed);
            changed += ClearGeneric(value);
            return;
        }
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            if (property.GetIndexParameters().Length == 0 && property.CanRead)
                Walk(property.GetValue(value), seen, ref changed);
    }

    // Sets that are neither IList nor IDictionary still implement ICollection<T>.
    private static int ClearGeneric(object value)
    {
        foreach (var contract in value.GetType().GetInterfaces())
        {
            if (!contract.IsGenericType || contract.GetGenericTypeDefinition() != typeof(ICollection<>)) continue;
            if ((bool)contract.GetProperty("IsReadOnly")!.GetValue(value)!) return 0;
            contract.GetMethod("Clear")!.Invoke(value, null);
            return 1;
        }
        return 0;
    }
}
