using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Xunit;

namespace EzClassify.Tests;

/// <summary>
/// Conformance runner for the EzClassify .NET SDK.
///
/// Reads every engine-slice plan from vendor/spec/plans/v1/, pre-populates a
/// local HTTP replayer with the matching recorded responses, and runs each plan
/// case against the replayer.
///
/// Server-slice plans (ctx* methods) run against a live server through
/// <c>make conformance-server</c>.
///
/// Run with <c>make test</c> from the repo root.
/// </summary>
public class ConformanceTests : IDisposable
{
    // Guide and access operations are reachable only through a bound Context: a
    // guide or access policy is meaningless without a scheme to bind it to, so
    // the REST API serves no standalone guide/access routes. Those cases
    // route through /v1/ctx/{ctxId}/{version}/... with payload-only bodies. The
    // replayer is keyed by (path, body) and never resolves a live context, so
    // this is just the canonical route shape the server serves; ref-ctx-1@1.0.0
    // is the reference context used across the conformance spec.
    private const string CtxBase = "/v1/ctx/ref-ctx-1/1.0.0";

    // Number of engine-slice plan cases the runner must execute against the
    // vendored spec bundle. Ambient registry vectors belong to the embedded
    // engine; every HTTP adapter case must run. The count
    // is a pinned golden: when the vendored bundle deliberately adds or removes
    // plan cases, update it in the same commit. Without the pin, a missing
    // recording or unreadable fixture would silently shrink the suite.
    private const int ExpectedCaseCount = 144;

    private readonly string _fixtureDir;
    private readonly string _plansDir;
    private readonly string _recordingsDir;
    private readonly Dictionary<string, JsonNode> _allRecordings;
    private readonly Replayer _replayer;
    private readonly HttpClient _http;

    public ConformanceTests()
    {
        var repoRoot = FindRepoRoot();
        var specBase = Path.Combine(repoRoot, "vendor", "spec");
        _fixtureDir = Path.Combine(specBase, "fixtures", "v1");
        _plansDir = Path.Combine(specBase, "plans", "v1");
        _recordingsDir = Path.Combine(specBase, "recordings", "v1");

        _allRecordings = LoadAllRecordings(_recordingsDir);

        var plans = LoadEnginePlans(_plansDir);
        var replayerMap = BuildReplayerMap(plans, _allRecordings, _fixtureDir);

        _replayer = Replayer.Start(replayerMap);
        _http = new HttpClient();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _http.Dispose();
        _replayer.Dispose();
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "EzClassify.Rest.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Cannot locate EzClassify.Rest.sln");
    }

    private static Dictionary<string, JsonNode> LoadAllRecordings(string dir)
    {
        var result = new Dictionary<string, JsonNode>();
        foreach (var file in Directory.GetFiles(dir, "*.recordings.json"))
        {
            var doc = JsonNode.Parse(File.ReadAllBytes(file))!;
            var responses = doc["responses"]?.AsObject();
            if (responses == null) continue;
            foreach (var kv in responses)
            {
                var response = kv.Value?["response"];
                if (response != null)
                {
                    if (result.TryGetValue(kv.Key, out var previous) && !SameJson(previous, response))
                        throw new InvalidDataException($"Conflicting recording identity in {Path.GetFileName(file)}: {kv.Key}");
                    result.TryAdd(kv.Key, response.DeepClone());
                }
            }
        }
        return result;
    }

    private sealed record PlanFile(string Name, string Slice, JsonArray Cases);

    private static List<PlanFile> LoadEnginePlans(string dir)
    {
        var plans = new List<PlanFile>();
        foreach (var file in Directory.GetFiles(dir, "*.json")
                     .Where(f => Path.GetFileName(f) != "plan-schema.json"))
        {
            var doc = JsonNode.Parse(File.ReadAllBytes(file))!;
            var slice = doc["slice"]?.GetValue<string>() ?? "";
            var cases = doc["cases"]?.AsArray() ?? new JsonArray();
            // Ambient registry fan-out has no route in the Context-only HTTP API.
            if (Path.GetFileName(file) == "registry.json")
            {
                Assert.Equal("engine", slice);
                Assert.Equal(5, cases.Count);
                Assert.All(cases, tc => Assert.Contains(tc?["method"]?.GetValue<string>(),
                    new[] { "registryProcessAll", "registrySuggestAcross" }));
                continue;
            }
            plans.Add(new PlanFile(Path.GetFileName(file), slice, cases));
        }
        return plans;
    }

    private Dictionary<string, ReplayerRecording> BuildReplayerMap(
        List<PlanFile> plans,
        Dictionary<string, JsonNode> recordings,
        string fixtureDir)
    {
        var map = new Dictionary<string, ReplayerRecording>();
        foreach (var plan in plans.Where(p => p.Slice == "engine"))
        {
            foreach (var tc in plan.Cases)
            {
                if (tc is null) continue;
                var method = tc["method"]?.GetValue<string>() ?? "";
                var args = tc["args"]?.AsArray() ?? new JsonArray();
                var fixture = tc["fixture"];
                var mode = tc["mode"]?.GetValue<string>() ?? "fromJSON";
                var ctxId = tc["ctxId"]?.GetValue<string>() ?? "";
                var ctxVersion = tc["ctxVersion"]?.GetValue<string>() ?? "";
                var guideFixture = tc["guideFixture"]?.GetValue<string>() ?? "";

                var recKey = ComputeRecordingKey(method, args, fixture, mode, ctxId, ctxVersion);
                if (!recordings.TryGetValue(recKey, out var responseBody)) continue;

                var result = BuildPlanBody(method, args, fixture, guideFixture, fixtureDir);
                if (result.Skip) continue;

                var rKey = Replayer.RecordingKey(result.Path, result.Body);
                PutRecording(map, rKey, new ReplayerRecording(200, responseBody.DeepClone()),
                    $"{plan.Name}/{tc["name"]}");
            }
        }
        return map;
    }

    /// <summary>
    /// Reproduces the canonical recording hash algorithm.
    /// Hash input: method | stableJSON(args) | stableJSON(fixture) | mode | ctxId | ctxVersion
    /// </summary>
    private static string ComputeRecordingKey(
        string method,
        JsonArray args,
        JsonNode? fixture,
        string mode,
        string ctxId,
        string ctxVersion)
    {
        var fixtureJson = Replayer.StableJson(fixture);
        var argsJson = Replayer.StableJson(args);
        var raw = $"{method}|{argsJson}|{fixtureJson}|{mode}|{ctxId}|{ctxVersion}";
        return Replayer.Sha256Hex(raw);
    }

    private sealed record PlanBodyResult(string Path, JsonNode Body, bool Skip, string? DropReason = null)
    {
        public static PlanBodyResult Dropped(string reason) =>
            new("", JsonNode.Parse("{}")!, true, reason);
    }

    /// <summary>
    /// Constructs the HTTP request path and body for a plan case.
    /// A non-null DropReason marks a corpus defect (missing or unreadable
    /// fixture) that must fail the suite by name. Unknown methods fail loudly.
    /// </summary>
    private PlanBodyResult BuildPlanBody(
        string method,
        JsonArray args,
        JsonNode? fixture,
        string guideFixture,
        string fixtureDir)
    {
        // Safe string extraction from args; returns "" when index out of range or node is not a scalar.
        string StrArg(int i)
        {
            if (i < 0 || i >= args.Count) return "";
            var n = args[i];
            if (n is JsonValue v && v.TryGetValue<string>(out var s)) return s ?? "";
            return "";
        }

        string[] StrSliceArg(int i)
        {
            if (i < 0 || i >= args.Count) return Array.Empty<string>();
            var arr = args[i]?.AsArray();
            if (arr == null) return Array.Empty<string>();
            return arr
                .Select(v => (v is JsonValue jv && jv.TryGetValue<string>(out var s)) ? s ?? "" : "")
                .ToArray();
        }

        // All other methods require a scheme ID from the fixture. A missing or
        // unreadable fixture is a corpus defect, not a skip.
        if (!TryLoadFixtureSchemeId(fixtureDir, fixture, out var schemeId, out var layerIds))
            return PlanBodyResult.Dropped(
                $"unreadable scheme fixture: {fixture?.ToJsonString() ?? "null"}");

        JsonObject Payload(params (string Key, JsonNode? Value)[] fields)
        {
            var obj = new JsonObject();
            foreach (var (k, v) in fields)
                obj[k] = v?.DeepClone();
            return obj;
        }

        JsonObject ExclusiveInput() => args.Count > 0 && args[0] is JsonArray facts
            ? Payload(("assertions", facts))
            : Payload(("input", JsonValue.Create(StrArg(0))));

        // Keep canonical options intact without overwriting an operand.
        JsonObject WithOptions(JsonObject body, int index)
        {
            if (index >= args.Count || args[index] is not JsonObject options) return body;
            foreach (var (key, value) in options)
            {
                if (body.TryGetPropertyValue(key, out var existing) && !SameJson(existing, value))
                    throw new InvalidDataException($"Conflicting plan request field: {key}");
                body[key] = value?.DeepClone();
            }
            return body;
        }

        // The replayer needs a distinct immutable Context address for every
        // fixture composition. Encoding the fixture's base-plus-layer ids in
        // this test-only Context id preserves that distinction without putting
        // a scheme selector into request bodies.
        var contextParts = new List<string> { schemeId };
        contextParts.AddRange(layerIds);
        var contextId = Uri.EscapeDataString(string.Join("+", contextParts));
        var schemeBase = $"/v1/ctx/{contextId}/1.0.0/scheme";

        switch (method)
        {
            case "process":
                {
                    // Process carries chosen facts alongside an empty text input, unlike
                    // validate and suggest, whose text and facts fields are exclusive.
                    var body = Payload(("input", JsonValue.Create(StrArg(0))));
                    if (args.Count > 0 && args[0] is JsonArray facts)
                        body["assertions"] = facts.DeepClone();
                    return new($"{schemeBase}/process", WithOptions(body, 1), false);
                }

            case "validate":
                return new($"{schemeBase}/validate", WithOptions(ExclusiveInput(), 1), false);

            case "suggest":
                return new($"{schemeBase}/suggest", WithOptions(ExclusiveInput(), 1), false);

            case "normalize":
                return new($"{schemeBase}/normalize",
                    WithOptions(Payload(("input", JsonValue.Create(StrArg(0)))), 1), false);

            case "render":
                {
                    var assertionsNode = args.Count > 0 ? args[0]?.DeepClone() : new JsonArray();
                    var body = Payload(("assertions", assertionsNode));
                    if (args.Count > 1 && args[1] is JsonValue scalar && scalar.TryGetValue<string>(out var format))
                        body["outputFormat"] = format;
                    return new($"{schemeBase}/render", body, false);
                }

            case "scan":
                return new($"{schemeBase}/scan",
                    WithOptions(Payload(("text", JsonValue.Create(StrArg(0)))), 1), false);

            case "aggregate":
                {
                    var arr = new JsonArray();
                    foreach (var value in StrSliceArg(0)) arr.Add(JsonValue.Create(value));
                    return new($"{schemeBase}/aggregate", WithOptions(Payload(("inputs", arr)), 1), false);
                }

            case "aggregateAssertions":
                {
                    var setsNode = args.Count > 0 ? args[0]?.DeepClone() ?? new JsonArray() : new JsonArray();
                    return new($"{schemeBase}/aggregate",
                        WithOptions(Payload(("assertionSets", setsNode)), 1), false);
                }

            case "diff":
                return new($"{schemeBase}/diff",
                    WithOptions(Payload(("a", JsonValue.Create(StrArg(0))), ("b", JsonValue.Create(StrArg(1)))), 2), false);

            case "checkAccess":
                {
                    // The access policy is bound to the context; the body carries
                    // only the data assertions and the subject's user attributes.
                    var body = MakeObj(
                        ("assertions", args.Count > 0 ? args[0]?.DeepClone() : new JsonArray()),
                        ("userAttributes", args.Count > 1 ? args[1]?.DeepClone() : new JsonObject()));
                    return new($"{CtxBase}/access/check", body, false);
                }

            case "evaluateGuide":
            case "evaluateMany":
                {
                    // evaluateMany over the plan's single-guide fixtures is
                    // equivalent to a single-guide evaluate, so both fold onto the
                    // ctx guide/evaluate route (the same fold the client runner
                    // uses). The guide is bound to the context; the body carries
                    // only the guide id and the selected rules.
                    var gf = !string.IsNullOrEmpty(guideFixture) ? guideFixture : StrArg(0);
                    if (string.IsNullOrEmpty(gf) && fixture is JsonValue fv)
                        gf = fv.GetValue<string>();
                    // A guide case without its guide fixture is a corpus defect.
                    if (!TryLoadGuideFixture(fixtureDir, gf, out _))
                        return PlanBodyResult.Dropped($"unreadable guide fixture: '{gf}'");
                    var guideId = GuideIdFromFixture(fixtureDir, gf);
                    var selArr = new JsonArray();
                    foreach (var s in StrSliceArg(0)) selArr.Add(JsonValue.Create(s));
                    return new($"{CtxBase}/guide/evaluate", MakeObj(
                        ("guideId", JsonValue.Create(guideId)),
                        ("selectedRuleIds", selArr)), false);
                }

            case "guideScan":
                {
                    var gf = !string.IsNullOrEmpty(guideFixture) ? guideFixture : StrArg(0);
                    if (string.IsNullOrEmpty(gf) && fixture is JsonValue fv2)
                        gf = fv2.GetValue<string>();
                    if (!TryLoadGuideFixture(fixtureDir, gf, out _))
                        return PlanBodyResult.Dropped($"unreadable guide fixture: '{gf}'");
                    var guideId = GuideIdFromFixture(fixtureDir, gf);
                    return new($"{CtxBase}/guide/scan", MakeObj(
                        ("text", JsonValue.Create(StrArg(0))),
                        ("guideId", JsonValue.Create(guideId))), false);
                }

            default:
                throw new InvalidDataException($"Unsupported HTTP conformance method: {method}");
        }
    }

    private static JsonObject MakeObj(params (string Key, JsonNode? Value)[] fields)
    {
        var obj = new JsonObject();
        foreach (var (k, v) in fields) obj[k] = v;
        return obj;
    }

    // A repeated request key must describe one complete answer before any HTTP server starts.
    private static void PutRecording(
        Dictionary<string, ReplayerRecording> recordings, string key,
        ReplayerRecording response, string caseName)
    {
        if (recordings.TryGetValue(key, out var previous) &&
            (previous.StatusCode != response.StatusCode || !SameJson(previous.Body, response.Body)))
            throw new InvalidDataException($"Conflicting recording identity for {caseName}: {key}");
        recordings.TryAdd(key, response);
    }

    private static bool SameJson(JsonNode? left, JsonNode? right)
    {
        if (left is null || right is null) return left is null && right is null;
        if (left is JsonObject a && right is JsonObject b)
            return a.Count == b.Count && a.All(kv =>
                b.TryGetPropertyValue(kv.Key, out var value) && SameJson(kv.Value, value));
        if (left is JsonArray x && right is JsonArray y)
            return x.Count == y.Count && x.Zip(y).All(pair => SameJson(pair.First, pair.Second));
        if (left.GetValueKind() == JsonValueKind.Number && right.GetValueKind() == JsonValueKind.Number)
        {
            // The owner parses JSON numbers as IEEE doubles; spelling alone carries no meaning.
            using var l = JsonDocument.Parse(left.ToJsonString());
            using var r = JsonDocument.Parse(right.ToJsonString());
            return l.RootElement.GetDouble() == r.RootElement.GetDouble();
        }
        return JsonNode.DeepEquals(left, right);
    }

    private static bool TryLoadFixtureSchemeId(
        string fixtureDir,
        JsonNode? fixture,
        out string schemeId,
        out string[] layerIds)
    {
        schemeId = "";
        layerIds = Array.Empty<string>();

        if (fixture is JsonValue sv)
        {
            var name = sv.GetValue<string>();
            return TryReadSchemeIdFromFile(fixtureDir, name, out schemeId, out layerIds);
        }

        if (fixture is JsonArray arr)
        {
            var names = arr
                .Select(v => (v is JsonValue jv && jv.TryGetValue<string>(out var s)) ? s : "")
                .Where(s => !string.IsNullOrEmpty(s))
                .ToList();
            if (names.Count == 0) return false;

            var ids = new List<string>();
            foreach (var name in names)
            {
                if (!TryReadSchemeIdFromFile(fixtureDir, name, out var id, out _)) return false;
                ids.Add(id);
            }
            schemeId = ids[0];
            layerIds = ids.Skip(1).ToArray();
            return true;
        }

        return false;
    }

    private static bool TryReadSchemeIdFromFile(
        string fixtureDir, string name, out string schemeId, out string[] layerIds)
    {
        schemeId = "";
        layerIds = Array.Empty<string>();
        var path = Path.Combine(fixtureDir, name + ".json");
        if (!File.Exists(path)) return false;

        var node = JsonNode.Parse(File.ReadAllBytes(path));
        if (node is null) return false;

        if (node is JsonArray arr)
        {
            var ids = arr
                .Select(item => item?["scheme"]?["id"]?.GetValue<string>())
                .Where(id => id != null)
                .Select(id => id!)
                .ToList();
            if (ids.Count == 0) return false;
            schemeId = ids[0];
            layerIds = ids.Skip(1).ToArray();
            return true;
        }

        var sid = node["scheme"]?["id"]?.GetValue<string>();
        if (sid == null) return false;
        schemeId = sid;
        return true;
    }

    private static bool TryLoadGuideFixture(string fixtureDir, string name, out JsonNode guide)
    {
        guide = new JsonObject();
        if (string.IsNullOrEmpty(name)) return false;
        var path = Path.Combine(fixtureDir, name + ".json");
        if (!File.Exists(path)) return false;
        guide = JsonNode.Parse(File.ReadAllBytes(path))!;
        return true;
    }

    private static string GuideIdFromFixture(string fixtureDir, string name)
    {
        var path = Path.Combine(fixtureDir, name + ".json");
        if (!File.Exists(path)) return name;
        var node = JsonNode.Parse(File.ReadAllBytes(path));
        if (node is null) return name;
        return node["id"]?.GetValue<string>()
            ?? node["guide"]?["id"]?.GetValue<string>()
            ?? name;
    }

    /// <summary>
    /// Pins the per-route wire names for the plans' canonical output, surface and input
    /// format options. Process and aggregate carry the first two as
    /// <c>outputFormat</c> and <c>surface</c>; render sends its format as
    /// <c>outputFormat</c> too; validate and process send the input format as
    /// <c>inputFormat</c>.
    /// The replayer is keyed by the body this runner
    /// builds, so a stale field name would otherwise replay cleanly while the
    /// real server rejects the request.
    /// </summary>
    [Theory]
    [InlineData("process", """[[{"categoryId":"level","conceptId":"HIGH"}],{"outputFormat":"full","surface":"alt"}]""", "outputFormat", "surface")]
    [InlineData("aggregateAssertions", """[[[{"categoryId":"level","conceptId":"HIGH"}]],{"outputFormat":"full","surface":"alt"}]""", "outputFormat", "surface")]
    [InlineData("render", """[[{"categoryId":"level","conceptId":"HIGH"}],"full"]""", "outputFormat", null)]
    [InlineData("validate", """["CONF",{"inputFormat":"full"}]""", "inputFormat", null)]
    [InlineData("process", """["CONF/PARTNERS",{"inputFormat":"full"}]""", "inputFormat", null)]
    public void BuildPlanBody_UsesEachRoutesWireNames(
        string method, string argsJson, string formatKey, string? surfaceKey)
    {
        var args = JsonNode.Parse(argsJson)!.AsArray();
        var result = BuildPlanBody(method, args, JsonValue.Create("ref-scope-1"), "", _fixtureDir);

        Assert.Null(result.DropReason);
        Assert.False(result.Skip);
        var body = result.Body.AsObject();
        Assert.Equal("full", body[formatKey]?.GetValue<string>());
        if (surfaceKey is not null)
            Assert.Equal("alt", body[surfaceKey]?.GetValue<string>());
        var operand = method == "aggregateAssertions" ? "assertionSets"
            : args[0] is JsonArray ? "assertions" : "input";
        var expected = new JsonObject { [operand] = args[0]?.DeepClone(), [formatKey] = "full" };
        if (method == "process" && args[0] is JsonArray) expected["input"] = "";
        if (surfaceKey is not null) expected[surfaceKey] = "alt";
        Assert.True(JsonNode.DeepEquals(expected, body));
    }

    [Fact]
    public void UnknownConformanceMethodFails()
    {
        var error = Assert.Throws<InvalidDataException>(() => BuildPlanBody(
            "futureOperation", new JsonArray(), JsonValue.Create("ref-scheme-1"), "", _fixtureDir));
        Assert.Contains("futureOperation", error.Message);
    }

    /// <summary>
    /// Reads engine-slice plans from vendor/spec/plans/v1/ and runs each case
    /// against a local HTTP replayer seeded with responses from
    /// vendor/spec/recordings/v1/.
    /// </summary>
    [Fact]
    public async Task Conformance_EnginePlans_AllCasesPass()
    {
        var plans = LoadEnginePlans(_plansDir);
        var ran = 0;
        var failures = new List<string>();
        var dropped = new List<string>();

        foreach (var plan in plans)
        {
            if (plan.Slice != "engine") continue;

            foreach (var tc in plan.Cases)
            {
                if (tc is null) continue;
                var name = $"{plan.Name}/{tc["name"]?.GetValue<string>() ?? "?"}";
                var method = tc["method"]?.GetValue<string>() ?? "";
                var fixture = tc["fixture"];
                var guideFixture = tc["guideFixture"]?.GetValue<string>() ?? "";
                var args = tc["args"]?.AsArray() ?? new JsonArray();
                var expect = tc["expect"];

                var result = BuildPlanBody(method, args, fixture, guideFixture, _fixtureDir);
                if (result.DropReason != null)
                {
                    // A case that lost its fixture is corpus corruption; record
                    // it by name so the suite fails instead of shrinking.
                    dropped.Add($"{name} ({result.DropReason})");
                    continue;
                }

                var bodyJson = Replayer.StableJson(result.Body);
                var content = new StringContent(bodyJson, Encoding.UTF8, "application/json");
                HttpResponseMessage resp;
                try
                {
                    resp = await _http.PostAsync(_replayer.Url + result.Path, content);
                }
                catch (Exception ex)
                {
                    failures.Add($"{name}: request failed: {ex.Message}");
                    continue;
                }

                var respBody = await resp.Content.ReadAsStringAsync();

                if ((int)resp.StatusCode == 404)
                {
                    var miss = JsonNode.Parse(respBody);
                    if (miss?["error"]?.GetValue<string>() == "no_recording")
                    {
                        // A missing recording is corpus corruption, not an
                        // expected skip: the case must fail by name.
                        dropped.Add($"{name} (no recording for path={result.Path})");
                        continue;
                    }
                    failures.Add($"{name}: unexpected 404: {respBody}");
                    continue;
                }

                if ((int)resp.StatusCode != 200)
                {
                    failures.Add($"{name}: expected 200, got {(int)resp.StatusCode}: {respBody}");
                    continue;
                }

                var actual = JsonNode.Parse(respBody);
                if (expect != null && !JsonSubset.Matches(actual, expect))
                {
                    failures.Add($"{name}: subset mismatch\n  expect: {expect}\n  actual: {respBody}");
                    continue;
                }

                ran++;
            }
        }

        Assert.True(
            dropped.Count == 0,
            $"Plan cases dropped instead of executed:\n  {string.Join("\n  ", dropped)}");

        Assert.True(
            failures.Count == 0,
            $"{failures.Count} conformance failure(s):\n{string.Join("\n", failures)}");

        Assert.True(
            ran == ExpectedCaseCount,
            $"Executed conformance case count ({ran}) " +
            $"drifted from the pinned corpus count ({ExpectedCaseCount}); if the vendored spec " +
            "bundle changed deliberately, update ExpectedCaseCount in the same commit");
    }
}
