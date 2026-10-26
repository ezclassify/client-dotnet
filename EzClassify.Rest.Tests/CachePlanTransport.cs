using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace EzClassify.Tests;

/// <summary>
/// The clock a plan's client reads for entry lifetimes. It starts at a fixed
/// instant and moves only when a plan advances it, so a thirty-day lifetime is
/// measured without waiting and a real-time pause can never age an entry.
/// </summary>
internal sealed class CachePlanClock
{
    private long _elapsedMilliseconds;

    public DateTimeOffset Now() =>
        new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddMilliseconds(Interlocked.Read(ref _elapsedMilliseconds));

    public void Advance(long milliseconds) => Interlocked.Add(ref _elapsedMilliseconds, milliseconds);
}

/// <summary>
/// The counting replay transport the cache plans run against. It answers each
/// request with the recorded response of the corpus case the request matches and
/// counts requests per case.
///
/// <para>Matching takes the method, the path, the query as a set and the
/// canonical body as recorded, with one allowance: the Context version path
/// segment may carry the binding's version or the version the recording
/// resolved to. Anything else is a miss, answered 404 and kept in
/// <see cref="Misses"/> so a plan fails with the request that went out.</para>
///
/// <para>It also holds a matching request in flight until a plan releases it,
/// replays scripted responses derived from recorded bytes, and reports whether a
/// held request was canceled at the transport.</para>
/// </summary>
internal sealed class CachePlanTransport : HttpMessageHandler
{
    private const string VersionHeader = "x-ezclassify-resolved-version";
    private static readonly Regex VersionSegment = new(@"^(/v1/ctx/[^/?]+/)([^/?]+)(?=/|\?|$)", RegexOptions.Compiled);

    private sealed class Held
    {
        public readonly TaskCompletionSource<HttpResponseMessage> Answer = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Aborted;
    }

    private readonly WireCorpus _corpus;
    private readonly IReadOnlySet<string> _versions;
    private readonly Dictionary<string, WireCase> _byKey = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _counts = new(StringComparer.Ordinal);
    private readonly HashSet<string> _gated = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<Held>> _held = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Queue<Func<WireCase, (int Status, string Body, IReadOnlyDictionary<string, string> Headers)>>> _scripted = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private string? _movedVersion;

    /// <param name="corpus">The wire corpus every case id refers to.</param>
    /// <param name="bindingVersion">The version segment the client puts in its paths.</param>
    public CachePlanTransport(WireCorpus corpus, string bindingVersion)
    {
        _corpus = corpus;
        var recorded = new HashSet<string>(StringComparer.Ordinal) { bindingVersion };
        foreach (var recordedCase in corpus.Cases)
        {
            var key = KeyOf(recordedCase);
            if (!_byKey.ContainsKey(key)) _byKey[key] = recordedCase;
            var match = VersionSegment.Match(recordedCase.Target);
            if (match.Success) recorded.Add(match.Groups[2].Value);
        }
        _versions = recorded;
    }

    /// <summary>Requests that matched no recorded case.</summary>
    public List<string> Misses { get; } = new();

    /// <summary>Every request received since the last <see cref="ResetCounts"/>, matched or not.</summary>
    public int Total
    {
        get { lock (_gate) return _counts.Values.Sum() + Misses.Count; }
    }

    /// <summary>Counts start once the client is built and connected; the connect probe is not part of any plan.</summary>
    public void ResetCounts()
    {
        lock (_gate)
        {
            _counts.Clear();
            Misses.Clear();
        }
    }

    /// <summary>Requests received for the exchange <paramref name="caseId"/> records.</summary>
    public int Count(string caseId)
    {
        var key = KeyOf(_corpus.ById[caseId]);
        lock (_gate) return _counts.TryGetValue(key, out var count) ? count : 0;
    }

    /// <summary>Hold every later request matching the case in flight until <see cref="Release"/>.</summary>
    public void Gate(string caseId)
    {
        lock (_gate) _gated.Add(KeyOf(_corpus.ById[caseId]));
    }

    /// <summary>Requests for the case currently held in flight.</summary>
    public int HeldCount(string caseId)
    {
        var key = KeyOf(_corpus.ById[caseId]);
        lock (_gate) return _held.TryGetValue(key, out var held) ? held.Count(h => !h.Aborted && !h.Answer.Task.IsCompleted) : 0;
    }

    /// <summary>Whether any request held for the case has been canceled at the transport.</summary>
    public bool Aborted(string caseId)
    {
        var key = KeyOf(_corpus.ById[caseId]);
        lock (_gate) return _held.TryGetValue(key, out var held) && held.Any(h => h.Aborted);
    }

    /// <summary>
    /// Answer the held requests for the case with its recorded response and stop
    /// holding. A request already canceled is not answered.
    /// </summary>
    public void Release(string caseId)
    {
        var recorded = _corpus.ById[caseId];
        var key = KeyOf(recorded);
        List<Held> release;
        lock (_gate)
        {
            _gated.Remove(key);
            release = _held.TryGetValue(key, out var held) ? held.Where(h => !h.Aborted).ToList() : new List<Held>();
        }
        foreach (var held in release) held.Answer.TrySetResult(Replay(recorded));
    }

    /// <summary>Answer the next request matching the case with its recorded response minus the JSON pointers named.</summary>
    public void ScriptMalformedSuccess(string caseId, IReadOnlyList<string> removeMembers) =>
        Script(caseId, recorded =>
        {
            var body = JsonNode.Parse(recorded.ResponseText) ?? throw new InvalidOperationException($"{caseId} records no body");
            foreach (var pointer in removeMembers) Remove(body, pointer);
            return (recorded.Status, body.ToJsonString(), recorded.ResponseHeaders);
        });

    /// <summary>Answer the next request matching the case with the denial-shaped 404 the corpus records.</summary>
    public void ScriptDenial(string caseId, string denialCaseId)
    {
        var denial = _corpus.ById[denialCaseId];
        Script(caseId, _ => (denial.Status, denial.ResponseText, denial.ResponseHeaders));
    }

    /// <summary>
    /// Make every later response carry the version wherever the recorded bytes
    /// name the resolved Context version: the header, the resolution's resolved
    /// version and the version of a Context description.
    /// </summary>
    public void MoveResolvedVersion(string version)
    {
        lock (_gate) _movedVersion = version;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var target = request.RequestUri!.PathAndQuery;
        var bodyText = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var key = WireCorpus.MatchKey(
            request.Method.Method, Normalize(target), bodyText is null ? null : JsonNode.Parse(bodyText), request.Content is not null);

        WireCase? recorded;
        Held? held = null;
        Func<WireCase, (int Status, string Body, IReadOnlyDictionary<string, string> Headers)>? script = null;
        lock (_gate)
        {
            if (!_byKey.TryGetValue(key, out recorded))
            {
                Misses.Add($"{request.Method} {target} {bodyText ?? "(no body)"}");
                return WireReplayHandler.Respond(404, """{"error":"no_recording"}""", new Dictionary<string, string>());
            }
            _counts[key] = _counts.GetValueOrDefault(key) + 1;
            if (_scripted.TryGetValue(key, out var queue) && queue.Count > 0) script = queue.Dequeue();
            if (_gated.Contains(key))
            {
                held = new Held();
                if (!_held.TryGetValue(key, out var list)) _held[key] = list = new List<Held>();
                list.Add(held);
            }
        }

        if (held is not null)
        {
            // Registered outside the lock: Cancel runs the callback inline.
            using var registration = cancellationToken.Register(() =>
            {
                lock (_gate) held.Aborted = true;
                held.Answer.TrySetCanceled(cancellationToken);
            });
            return await held.Answer.Task.ConfigureAwait(false);
        }
        return script is null ? Replay(recorded) : Build(script(recorded));
    }

    private HttpResponseMessage Replay(WireCase recorded) =>
        Build((recorded.Status, recorded.ResponseText, recorded.ResponseHeaders));

    private HttpResponseMessage Build((int Status, string Body, IReadOnlyDictionary<string, string> Headers) answer)
    {
        string? moved;
        lock (_gate) moved = _movedVersion;
        if (moved is null) return WireReplayHandler.Respond(answer.Status, answer.Body, answer.Headers);

        var headers = new Dictionary<string, string>(answer.Headers, StringComparer.OrdinalIgnoreCase);
        if (headers.ContainsKey(VersionHeader)) headers[VersionHeader] = moved;
        var body = answer.Body;
        if (JsonNode.Parse(body) is JsonObject root)
        {
            if (root["resolution"] is JsonObject resolution && resolution.ContainsKey("resolvedVersion"))
                resolution["resolvedVersion"] = moved;
            if (root.ContainsKey("scheme") && root.ContainsKey("schemes") && root.ContainsKey("version"))
                root["version"] = moved;
            body = root.ToJsonString();
        }
        return WireReplayHandler.Respond(answer.Status, body, headers);
    }

    private void Script(string caseId, Func<WireCase, (int Status, string Body, IReadOnlyDictionary<string, string> Headers)> answer)
    {
        var key = KeyOf(_corpus.ById[caseId]);
        lock (_gate)
        {
            if (!_scripted.TryGetValue(key, out var queue)) _scripted[key] = queue = new();
            queue.Enqueue(answer);
        }
    }

    private static void Remove(JsonNode root, string pointer)
    {
        var steps = pointer.TrimStart('/').Split('/');
        var node = root;
        foreach (var step in steps.Take(steps.Length - 1))
            node = node?[step] ?? throw new InvalidOperationException($"{pointer} names a member the recording does not carry");
        if (node is not JsonObject parent || !parent.Remove(steps[^1]))
            throw new InvalidOperationException($"{pointer} names a member the recording does not carry");
    }

    private static string KeyOf(WireCase recorded) =>
        WireCorpus.MatchKey(
            recorded.Method, VersionSegment.Replace(recorded.Target, "$1{version}"), recorded.RequestBody, recorded.HasRequestBody);

    // Replaces the Context version segment with a placeholder, so a request that
    // carries the binding's version and a recording that carries the resolved
    // one share a key. A request naming any other version stays unmatched.
    private string Normalize(string target) =>
        VersionSegment.Replace(target, match => _versions.Contains(match.Groups[2].Value) ? match.Groups[1].Value + "{version}" : match.Value);
}
