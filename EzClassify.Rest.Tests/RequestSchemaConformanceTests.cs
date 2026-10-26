using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Xunit;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace EzClassify.Tests;

/// <summary>
/// Request-shape gate.
///
/// Every operation that sends a body is driven through the real client with
/// every optional argument supplied, and the body it puts on the wire is
/// validated against that operation's request schema in the vendored contract
/// (vendor/spec/openapi.yaml). The request schemas close their objects
/// (<c>additionalProperties: false</c>), so a field the contract renamed or
/// removed fails here instead of as a 400 from a live server.
///
/// This exists because the failure it catches is invisible to the rest of the
/// suite: when a request field is renamed in the contract, a client that keeps
/// sending the former name still passes every body-substring test that asserts
/// that same name.
/// </summary>
public class RequestSchemaConformanceTests
{
    private static readonly Lazy<OpenApiRequestSchemas> Spec = new(OpenApiRequestSchemas.Load);

    // Every optional Assertion field is populated so each operation that takes
    // assertions validates the full field set against the contract.
    private static readonly Assertion Level = new(
        CategoryId: "level",
        ConceptId: "HIGH",
        Confidence: Confidence.Certain,
        Source: "parse",
        ExpiresAt: "2030-01-01T00:00:00Z",
        Metadata: JsonDocument.Parse("""{ "origin": "import" }""").RootElement.Clone(),
        Provenance: new SourceReference(
            "idp", new Attribution("IdP", "internal", "https://example.test/license", "IdP directory"),
            SourceReproducibility.Reproducible, SnapshotId: "snap-1", Locator: "idp://levels/high"),
        ReplacedConceptId: "LEGACY_HIGH",
        SourceRule: "rule-7",
        Value: "H");

    private static readonly GuideDeterminationInput Determinations = new(
        new[]
        {
            new GuideFindingDetermination("finding-1", "sha256:abc", GuideFindingDecision.Accepted, "Confirmed by analyst"),
        },
        Array.Empty<ManualGuideSelectionInput>());

    private const string PartOf = """{ "role": "part", "partOf": { "rendered": "H//A" } }""";

    /// <summary>
    /// One case per client call that sends a body, keyed by a readable name.
    /// Each supplies every optional argument the call takes, so the whole
    /// option surface reaches the wire. Overloads that build a different body
    /// get their own case.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, (string Route, Func<EzClassifyClient, Task> Invoke)> Cases =
        new Dictionary<string, (string, Func<EzClassifyClient, Task>)>(StringComparer.Ordinal)
        {
            ["scheme.process"] = ("scheme/process", c => c.Scheme!.ProcessAsync(
                "H//A", outputFormat: "banner", surface: "portion", inputFormat: "portion",
                assertions: new[] { Level }, contextJson: PartOf, parts: new[] { "H//B" },
                outputs: new[] { "marking/short", "marking/long" }, suggestFor: new[] { "audience" })),
            ["scheme.explain"] = ("scheme/explain", c => c.Scheme!.ExplainAsync(
                "H//A", outputFormat: "banner", surface: "portion", inputFormat: "portion",
                assertions: new[] { Level }, contextJson: PartOf, parts: new[] { "H//B" })),
            ["scheme.validate"] = ("scheme/validate", c => c.Scheme!.ValidateAsync(
                "H//A", surface: "portion", outputFormat: "banner", inputFormat: "portion",
                parts: new[] { "H//B" }, contextJson: PartOf)),
            // Validate takes the marking as text or as facts, and each overload
            // builds its own body.
            ["scheme.validate-facts"] = ("scheme/validate", c => c.Scheme!.ValidateAsync(
                new[] { Level }, surface: "portion", outputFormat: "banner",
                parts: new[] { "H//B" }, contextJson: PartOf)),
            ["scheme.suggest"] = ("scheme/suggest", c => c.Scheme!.SuggestAsync(
                "H//", outputFormat: "banner", filter: "aud", surface: "portion", inputFormat: "portion",
                includeGated: true)),
            ["scheme.suggest-facts"] = ("scheme/suggest", c => c.Scheme!.SuggestAsync(
                new[] { Level }, outputFormat: "banner", filter: "aud", surface: "portion", includeGated: true)),
            ["scheme.suggest-for-categories"] = ("scheme/suggest-for-categories", c => c.Scheme!.SuggestForCategoriesAsync(
                new[] { Level }, new[] { "level", "audience" }, outputFormat: "banner", surface: "portion")),
            ["scheme.aggregate"] = ("scheme/aggregate", c => c.Scheme!.AggregateAsync(
                new[] { "H", "L" }, outputFormat: "banner", surface: "portion", inputFormat: "portion", contextJson: PartOf)),
            ["scheme.aggregate-assertions"] = ("scheme/aggregate", c => c.Scheme!.AggregateAssertionsAsync(
                new[] { new[] { Level } }, outputFormat: "banner", surface: "portion", contextJson: PartOf)),
            ["scheme.scan"] = ("scheme/scan", c => c.Scheme!.ScanAsync(
                "text H text", outputFormat: "banner", inputFormat: "portion")),
            ["scheme.render"] = ("scheme/render", c => c.Scheme!.RenderAsync(
                new[] { Level }, outputFormat: "banner", preservation: RenderPreservation.Full)),
            ["scheme.normalize"] = ("scheme/normalize", c => c.Scheme!.NormalizeAsync(
                "h", outputFormat: "banner", inputFormat: "portion")),
            ["scheme.check-coverage"] = ("scheme/check-coverage", c => c.Scheme!.CheckCoverageAsync(
                new[] { "L" }, "H", outputFormat: "banner", inputFormat: "portion")),
            ["scheme.diff"] = ("scheme/diff", c => c.Scheme!.DiffAsync("H", "L", inputFormat: "portion")),
            ["scheme.filter-concepts"] = ("scheme/filter-concepts", c => c.Scheme!.FilterConceptsAsync("hi")),
            ["scheme.notices"] = ("scheme/notices", c => c.Scheme!.NoticesAsync(new[] { Level })),
            ["scheme.view"] = ("scheme/view", c => c.Scheme!.ViewAsync()),
            ["scheme.apply-preset"] = ("scheme/apply-preset", c => c.Scheme!.ApplyPresetAsync(
                "default-label", outputFormat: "banner")),
            ["guide.scan"] = ("guide/scan", c => c.Guide!.ScanAsync("secret text", guideId: "g1")),
            ["guide.evaluate"] = ("guide/evaluate", c => c.Guide!.EvaluateAsync("g1", new[] { "r1" })),
            ["guide.evaluateMany"] = ("guide/evaluate", c => c.Guide!.EvaluateManyAsync(new[] { "r1" })),
            ["guide.filter-rules"] = ("guide/filter-rules", c => c.Guide!.FilterRulesAsync("sec", guideId: "g1")),
            ["guide.describe-rule"] = ("guide/describe-rule", c => c.Guide!.DescribeRuleAsync("r1", guideId: "g1")),
            ["guide.view"] = ("guide/view", c => c.Guide!.ViewAsync(guideId: "g1")),
            ["access.check"] = ("access/check", c => c.Access!.CheckAsync(
                new[] { Level }, """{ "clearance": "high" }""", contextJson: """{ "orgs": ["org-1"] }""")),
            ["access.check-by-marking"] = ("access/check", c => c.Access!.CheckByMarkingAsync(
                "H", """{ "clearance": "high" }""", contextJson: """{ "orgs": ["org-1"] }""")),
            ["access.describe-requirements"] = ("access/describe-requirements", c => c.Access!.DescribeRequirementsAsync(
                "H", new[] { Level })),
            ["access.describe-rule"] = ("access/describe-rule", c => c.Access!.DescribeRuleAsync("rule-1")),
            ["crosswalk.translate-marking"] = ("crosswalk/translate", c => c.Crosswalk!.TranslateAsync("cw-1", "H")),
            ["crosswalk.translate-assertions"] = ("crosswalk/translate", c => c.Crosswalk!.TranslateAsync("cw-1", new[] { Level })),
            ["document.audit"] = ("document/audit", c => c.Document!.AuditAsync(
                "document text", Determinations, DocumentResponseProfile.Full)),
            ["document.assess-access"] = ("document/assess-access", c => c.Document!.AssessAccessAsync(
                "document text", """{ "clearance": "high" }""", """{ "project": "p1" }""",
                Determinations, DocumentResponseProfile.Full)),
        };

    public static IEnumerable<object[]> CaseNames() => Cases.Keys.Select(name => new object[] { name });

    [Theory]
    [MemberData(nameof(CaseNames))]
    public async Task RequestBody_ValidatesAgainstTheContractRequestSchema(string caseName)
    {
        var (route, _) = Cases[caseName];
        var (method, path, body) = await SendAsync(caseName);
        Assert.Equal("POST", method);
        var suffix = "/v1/ctx/acme-marking/1.0.0/" + route;
        Assert.True(
            path == suffix || path.StartsWith(suffix + "?", StringComparison.Ordinal),
            $"{caseName} posted to {path}, not {suffix}.");

        var errors = Spec.Value.Validate("post", RoutePrefix + route, JsonNode.Parse(body!));
        Assert.True(
            errors.Count == 0,
            $"{caseName} sent a body the contract rejects:\n  {body}\n  " + string.Join("\n  ", errors));
    }

    /// <summary>
    /// Every field a request schema declares is sent by at least one case.
    ///
    /// Each case supplies every optional argument its call takes, so a declared
    /// field no case sends is a field the client has no argument for. Without
    /// this, the contract can add an option and the client can ship with no way
    /// to set it while every body it does send still validates.
    /// </summary>
    [Fact]
    public async Task EveryContractRequestProperty_ReachesTheWire()
    {
        var sent = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var (caseName, (route, _)) in Cases)
        {
            var (_, _, body) = await SendAsync(caseName);
            if (!sent.TryGetValue(route, out var keys)) sent[route] = keys = new HashSet<string>(StringComparer.Ordinal);
            keys.UnionWith(JsonNode.Parse(body!)!.AsObject().Select(kv => kv.Key));
        }

        var unsent = UnsentFields(sent);
        Assert.True(
            unsent.Count == 0,
            "The client never sends these contract fields: " + string.Join("; ", unsent));
    }

    private const string RoutePrefix = "/v1/ctx/{ctxId}/{version}/";

    /// <summary>
    /// The calls that send a query string, plus the root calls, which must send
    /// none. Keyed by the contract's path template, each supplies every optional
    /// argument it takes so the whole query surface reaches the wire.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, Func<EzClassifyClient, Task>> GetCases =
        new Dictionary<string, Func<EzClassifyClient, Task>>(StringComparer.Ordinal)
        {
            ["/v1/health"] = c => c.PingAsync(),
            ["/v1/ctx"] = c => c.ListContextsAsync(),
            ["/v1/ctx/{ctxId}"] = c => c.ListContextVersionsAsync("acme-marking"),
            [RoutePrefix + "scheme"] = c => c.Scheme!.SummaryAsync("source"),
            [RoutePrefix + "scheme/concepts"] = c => c.Scheme!.ConceptsAsync("source"),
            [RoutePrefix + "scheme/categories"] = c => c.Scheme!.CategoriesAsync("source"),
            [RoutePrefix + "scheme/formats"] = c => c.Scheme!.FormatsAsync("source"),
            [RoutePrefix + "scheme/grammar"] = c => c.Scheme!.GrammarAsync("source"),
            [RoutePrefix + "scheme/scopes"] = c => c.Scheme!.ScopesAsync("source"),
            [RoutePrefix + "scheme/label-presets"] = c => c.Scheme!.LabelPresetsAsync(),
            [RoutePrefix + "scheme/describe-concept"] = c => c.Scheme!.DescribeConceptAsync("HIGH", "level", view: "short"),
            [RoutePrefix + "access/rules"] = c => c.Access!.RulesAsync(),
        };

    /// <summary>
    /// The server answers 400 to a query parameter the operation does not
    /// declare, so a call that sent one would fail against any live server
    /// while every stubbed test passed. Every call, body-carrying or not, may
    /// send only the query parameters its operation declares.
    /// </summary>
    [Fact]
    public async Task EveryQueryParameterSent_IsDeclaredByItsOperation()
    {
        var problems = new List<string>();
        foreach (var (path, invoke, method) in QueryChecked())
        {
            var (_, sentPath, _) = await SendCallAsync(path, invoke);
            var declared = Spec.Value.DeclaredQueryParameters(method, path);
            var extras = QueryNames(sentPath).Where(name => !declared.Contains(name)).ToList();
            if (extras.Count > 0) problems.Add($"{method.ToUpperInvariant()} {path} sent {string.Join(", ", extras)}");
        }

        Assert.True(problems.Count == 0, "The client sends query parameters the contract does not declare: " + string.Join("; ", problems));
    }

    /// <summary>
    /// A query parameter the contract declares is one the client can set. The
    /// operations that declare one all have a call here, and each call sends
    /// every one of them.
    /// </summary>
    [Fact]
    public async Task EveryDeclaredQueryParameter_ReachesTheWire()
    {
        var checkedPaths = new HashSet<string>(QueryChecked().Select(entry => entry.Path), StringComparer.Ordinal);
        var uncovered = Spec.Value.OperationsWithQueryParameters
            .Where(op => !checkedPaths.Contains(op.Path))
            .Select(op => $"{op.Method.ToUpperInvariant()} {op.Path}")
            .ToList();
        Assert.True(uncovered.Count == 0, "No call covers the query parameters of: " + string.Join(", ", uncovered));

        var unsent = new List<string>();
        foreach (var (path, invoke, method) in QueryChecked())
        {
            var (_, sentPath, _) = await SendCallAsync(path, invoke);
            var sent = QueryNames(sentPath).ToHashSet(StringComparer.Ordinal);
            var missing = Spec.Value.DeclaredQueryParameters(method, path).Where(name => !sent.Contains(name)).OrderBy(n => n, StringComparer.Ordinal).ToList();
            if (missing.Count > 0) unsent.Add($"{method.ToUpperInvariant()} {path}: {string.Join(", ", missing)}");
        }
        Assert.True(unsent.Count == 0, "The client never sends these declared query parameters: " + string.Join("; ", unsent));
    }

    // Every call the gate drives: the GET calls above, and each body-carrying
    // case under its POST path.
    private static IEnumerable<(string Path, Func<EzClassifyClient, Task> Invoke, string Method)> QueryChecked() =>
        GetCases.Select(entry => (entry.Key, entry.Value, "get"))
            .Concat(Cases.Values.Select(entry => (RoutePrefix + entry.Route, entry.Invoke, "post")));

    private static IEnumerable<string> QueryNames(string pathAndQuery)
    {
        var at = pathAndQuery.IndexOf('?');
        return at < 0
            ? Array.Empty<string>()
            : pathAndQuery[(at + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Select(pair => Uri.UnescapeDataString(pair.Split('=')[0]));
    }

    /// <summary>
    /// One "route: field, field" entry per route whose request schema declares
    /// a field that is missing from the keys actually sent to that route.
    /// </summary>
    private static List<string> UnsentFields(IReadOnlyDictionary<string, HashSet<string>> sent) =>
        sent.OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => (Route: kv.Key, Fields: Spec.Value.DeclaredProperties("post", RoutePrefix + kv.Key)
                .Where(name => !kv.Value.Contains(name))
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList()))
            .Where(entry => entry.Fields.Count > 0)
            .Select(entry => $"{entry.Route}: {string.Join(", ", entry.Fields)}")
            .ToList();

    /// <summary>Drive one case through the real client and return the single request it sent.</summary>
    private static Task<(string Method, string Path, string? Body)> SendAsync(string caseName) =>
        SendCallAsync(caseName, Cases[caseName].Invoke);

    private static async Task<(string Method, string Path, string? Body)> SendCallAsync(
        string caseName, Func<EzClassifyClient, Task> invoke)
    {
        var (client, handler) = await CtxClientFactory.ConnectAsync();
        using (client)
        {
            var before = handler.Requests.Count;
            try
            {
                await invoke(client);
            }
            catch (Exception)
            {
                // The stub answers every operation with an empty result, which
                // most parsers reject. Only the request matters here, and the
                // assertion below proves one was sent.
            }

            var sent = handler.Requests.Skip(before).ToList();
            Assert.True(sent.Count == 1, $"{caseName} sent {sent.Count} requests; expected exactly one.");
            return sent[0];
        }
    }

    /// <summary>
    /// Every operation the contract gives a request body has at least one case
    /// above, so a new operation cannot ship without its body being checked.
    /// </summary>
    [Fact]
    public void EveryContractRequestBody_HasACase()
    {
        var covered = Cases.Values.Select(c => "/v1/ctx/{ctxId}/{version}/" + c.Route).ToHashSet(StringComparer.Ordinal);
        var missing = Spec.Value.OperationsWithBodies
            .Where(op => op.Method == "post" && !covered.Contains(op.Path))
            .Select(op => $"{op.Method.ToUpperInvariant()} {op.Path}")
            .ToList();
        var nonPost = Spec.Value.OperationsWithBodies.Where(op => op.Method != "post").ToList();

        Assert.True(missing.Count == 0, "No request-schema case for: " + string.Join(", ", missing));
        Assert.True(
            nonPost.Count == 0,
            "The contract now has request bodies on non-POST operations; extend the cases to cover them: "
                + string.Join(", ", nonPost.Select(op => $"{op.Method} {op.Path}")));
    }

    // Negative controls. A gate that cannot fail proves nothing, so pin the two
    // failure modes it exists for: a removed field, and a missing required one.

    [Theory]
    [InlineData("scheme/process", """{ "input": "H", "format": "banner" }""", "format")]
    [InlineData("scheme/process", """{ "input": "H", "scope": "portion" }""", "scope")]
    [InlineData("scheme/process", """{ "input": "H", "tolerant": true }""", "tolerant")]
    [InlineData("scheme/process", """{ "input": "H", "normalization": "loose" }""", "normalization")]
    [InlineData("scheme/explain", """{ "input": "H", "outputs": ["marking/short"] }""", "outputs")]
    [InlineData("scheme/explain", """{ "input": "H", "suggestFor": ["audience"] }""", "suggestFor")]
    [InlineData("scheme/validate", """{ "input": "H", "scope": "portion" }""", "scope")]
    [InlineData("scheme/suggest", """{ "input": "H", "format": "banner" }""", "format")]
    [InlineData("scheme/aggregate", """{ "inputs": ["H"], "format": "banner", "scope": "portion" }""", "format")]
    [InlineData("scheme/diff", """{ "a": "H", "b": "L", "format": "banner" }""", "format")]
    [InlineData("scheme/scan", """{ "text": "H", "stripHtml": true }""", "stripHtml")]
    [InlineData("scheme/render", """{ "assertions": [], "format": "banner" }""", "format")]
    [InlineData("scheme/apply-preset", """{ "presetId": "p", "format": "banner" }""", "format")]
    public void Validator_RejectsRemovedFields(string route, string body, string field)
    {
        var errors = Spec.Value.Validate("post", RoutePrefix + route, JsonNode.Parse(body));
        Assert.Contains(errors, e => e.Contains($"'{field}'", StringComparison.Ordinal));
    }

    [Fact]
    public void Validator_RejectsMissingRequiredFieldAndWrongType()
    {
        var missing = Spec.Value.Validate("post", "/v1/ctx/{ctxId}/{version}/scheme/process", JsonNode.Parse("""{ "surface": "portion" }"""));
        Assert.Contains(missing, e => e.Contains("'input'", StringComparison.Ordinal));

        var wrongType = Spec.Value.Validate("post", "/v1/ctx/{ctxId}/{version}/scheme/render",
            JsonNode.Parse("""{ "assertions": [ { "categoryId": "level", "conceptId": "HIGH", "confidence": "sure", "source": "parse" } ] }"""));
        Assert.NotEmpty(wrongType);
    }

    [Fact]
    public async Task EveryPropertyCheck_ReportsAFieldNoCaseSends()
    {
        // Drop one field from what a real case put on the wire, as a client
        // that lost the option would, and the check must name it by route.
        var (_, _, body) = await SendAsync("scheme.scan");
        var keys = JsonNode.Parse(body!)!.AsObject().Select(kv => kv.Key).ToHashSet(StringComparer.Ordinal);
        Assert.True(keys.Remove("inputFormat"), "The scheme.scan case no longer sends inputFormat.");

        var unsent = UnsentFields(new Dictionary<string, HashSet<string>> { ["scheme/scan"] = keys });
        Assert.Equal(new[] { "scheme/scan: inputFormat" }, unsent);
    }

    [Fact]
    public void DeclaredProperties_CoverEveryShapeARouteAccepts()
    {
        // aggregate takes rendered inputs or assertion sets; both shapes'
        // fields must count as declared, or a missing overload goes unnoticed.
        var declared = Spec.Value.DeclaredProperties("post", RoutePrefix + "scheme/aggregate");
        Assert.Superset(
            new HashSet<string>(StringComparer.Ordinal) { "inputs", "assertionSets", "context", "surface" },
            declared);
    }

    [Fact]
    public void Validator_AcceptsTheRenamedFields()
    {
        var errors = Spec.Value.Validate("post", "/v1/ctx/{ctxId}/{version}/scheme/process",
            JsonNode.Parse("""{ "input": "H", "inputFormat": "portion", "outputFormat": "banner", "surface": "portion" }"""));
        Assert.Empty(errors);
    }
}

/// <summary>
/// The request schemas of the vendored OpenAPI document, and a validator for
/// the JSON Schema subset they use. The validator fails closed: a schema
/// keyword it does not implement throws rather than being skipped, so a
/// contract that starts using one forces this harness to learn it instead of
/// silently passing everything that keyword would have rejected.
/// </summary>
internal sealed class OpenApiRequestSchemas
{
    public sealed record Operation(string Method, string Path);

    private static readonly HashSet<string> Annotations = new(StringComparer.Ordinal)
    {
        "description", "title", "default", "example", "examples", "deprecated", "readOnly", "writeOnly", "$comment",
    };

    private readonly JsonObject _components;
    private readonly Dictionary<Operation, JsonObject> _requestSchemas;
    private readonly Dictionary<Operation, HashSet<string>> _queryParameters;
    private readonly Dictionary<Operation, HashSet<string>> _responseStatuses;
    private readonly JsonObject _responses;

    private OpenApiRequestSchemas(
        JsonObject components,
        Dictionary<Operation, JsonObject> requestSchemas,
        Dictionary<Operation, HashSet<string>> queryParameters,
        Dictionary<Operation, HashSet<string>> responseStatuses,
        JsonObject responses)
    {
        _components = components;
        _requestSchemas = requestSchemas;
        _queryParameters = queryParameters;
        _responseStatuses = responseStatuses;
        _responses = responses;
    }

    /// <summary>The operations whose declared responses do not include <paramref name="status"/>.</summary>
    public IReadOnlyList<Operation> OperationsWithoutResponse(string status) =>
        _responseStatuses.Where(entry => !entry.Value.Contains(status)).Select(entry => entry.Key).ToList();

    /// <summary>A reusable response under <c>components/responses</c>.</summary>
    public JsonObject ComponentResponse(string name) =>
        _responses[name] as JsonObject ?? throw new KeyNotFoundException($"The contract declares no response {name}.");

    /// <summary>A schema component.</summary>
    public JsonObject ComponentSchema(string name) =>
        _components[name] as JsonObject ?? throw new KeyNotFoundException($"The contract declares no schema {name}.");

    public IReadOnlyCollection<Operation> OperationsWithBodies => _requestSchemas.Keys;

    /// <summary>The operations that declare at least one query parameter.</summary>
    public IReadOnlyCollection<Operation> OperationsWithQueryParameters =>
        _queryParameters.Where(entry => entry.Value.Count > 0).Select(entry => entry.Key).ToList();

    /// <summary>Query parameter names an operation declares; empty for an operation that declares none.</summary>
    public HashSet<string> DeclaredQueryParameters(string method, string path)
    {
        Assert.True(
            _queryParameters.TryGetValue(new Operation(method, path), out var names),
            $"The contract declares no operation {method.ToUpperInvariant()} {path}.");
        return names!;
    }

    public static OpenApiRequestSchemas Load()
    {
        var specPath = Path.Combine(FindRepoRoot(), "vendor", "spec", "openapi.yaml");
        var yaml = new YamlStream();
        using (var reader = new StreamReader(specPath))
            yaml.Load(reader);
        var root = (JsonObject)ToJson(yaml.Documents[0].RootNode)!;

        var components = (JsonObject)root["components"]!["schemas"]!;
        var schemas = new Dictionary<Operation, JsonObject>();
        var queries = new Dictionary<Operation, HashSet<string>>();
        var statuses = new Dictionary<Operation, HashSet<string>>();
        foreach (var (path, item) in (JsonObject)root["paths"]!)
        {
            foreach (var (method, operation) in (JsonObject)item!)
            {
                var key = new Operation(method, path);
                var schema = operation?["requestBody"]?["content"]?["application/json"]?["schema"];
                if (schema is JsonObject obj) schemas[key] = obj;
                statuses[key] = ((JsonObject)operation!["responses"]!).Select(response => response.Key).ToHashSet(StringComparer.Ordinal);
                queries[key] = (operation?["parameters"] as JsonArray ?? new JsonArray())
                    .OfType<JsonObject>()
                    .Select(parameter => parameter["$ref"] is null ? parameter
                        : throw new NotSupportedException($"{path} declares a $ref parameter; teach Load to follow it."))
                    .Where(parameter => parameter["in"]?.GetValue<string>() == "query")
                    .Select(parameter => parameter["name"]!.GetValue<string>())
                    .ToHashSet(StringComparer.Ordinal);
            }
        }
        Assert.True(schemas.Count > 0, "Read no request schemas out of vendor/spec/openapi.yaml.");
        return new OpenApiRequestSchemas(components, schemas, queries, statuses, (JsonObject)root["components"]!["responses"]!);
    }

    /// <summary>
    /// Top-level property names an operation's request schema declares. Follows
    /// <c>$ref</c> and the <c>anyOf</c> and <c>if</c>/<c>then</c>/<c>else</c>
    /// branches, so a field declared on any branch counts.
    /// </summary>
    public HashSet<string> DeclaredProperties(string method, string path)
    {
        Assert.True(
            _requestSchemas.TryGetValue(new Operation(method, path), out var root),
            $"The contract declares no request body for {method.ToUpperInvariant()} {path}.");
        var names = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<JsonNode>();
        pending.Push(root!);
        while (pending.Count > 0)
        {
            if (pending.Pop() is not JsonObject schema) continue; // boolean schemas declare nothing
            if (schema["$ref"] is JsonNode reference) pending.Push(Resolve(reference.GetValue<string>()));
            if (schema["properties"] is JsonObject properties) names.UnionWith(properties.Select(kv => kv.Key));
            if (schema["anyOf"] is JsonArray branches)
                foreach (var branch in branches) pending.Push(branch!);
            if (schema["then"] is JsonNode then) pending.Push(then);
            if (schema["else"] is JsonNode otherwise) pending.Push(otherwise);
        }
        return names;
    }

    /// <summary>Validate a request body; returns one message per violation, empty when valid.</summary>
    public List<string> Validate(string method, string path, JsonNode? body)
    {
        Assert.True(
            _requestSchemas.TryGetValue(new Operation(method, path), out var schema),
            $"The contract declares no request body for {method.ToUpperInvariant()} {path}.");
        var errors = new List<string>();
        Check(body, schema!, "$", errors);
        return errors;
    }

    private void Check(JsonNode? value, JsonNode schemaNode, string at, List<string> errors)
    {
        if (schemaNode is JsonValue boolSchema && boolSchema.TryGetValue<bool>(out var allowed))
        {
            if (!allowed) errors.Add($"{at}: no value is allowed here");
            return;
        }
        var schema = (JsonObject)schemaNode;

        foreach (var (keyword, argument) in schema)
        {
            switch (keyword)
            {
                case "$ref":
                    Check(value, Resolve(argument!.GetValue<string>()), at, errors);
                    break;
                case "type":
                    CheckType(value, argument!, at, errors);
                    break;
                case "enum":
                    if (!argument!.AsArray().Any(option => JsonNode.DeepEquals(option, value)))
                        errors.Add($"{at}: {Show(value)} is not one of {argument.ToJsonString()}");
                    break;
                case "const":
                    if (!JsonNode.DeepEquals(argument, value))
                        errors.Add($"{at}: {Show(value)} is not {argument!.ToJsonString()}");
                    break;
                case "required":
                    if (value is JsonObject required)
                        foreach (var name in argument!.AsArray().Select(n => n!.GetValue<string>()))
                            if (!required.ContainsKey(name))
                                errors.Add($"{at}: required property '{name}' is missing");
                    break;
                case "properties":
                    if (value is JsonObject withProperties)
                        foreach (var (name, propertySchema) in argument!.AsObject())
                            if (withProperties.TryGetPropertyValue(name, out var property))
                                Check(property, propertySchema!, $"{at}.{name}", errors);
                    break;
                case "additionalProperties":
                    if (value is JsonObject open)
                    {
                        var declared = schema["properties"] as JsonObject;
                        foreach (var (name, property) in open)
                        {
                            if (declared is not null && declared.ContainsKey(name)) continue;
                            if (argument is JsonValue flag && flag.TryGetValue<bool>(out var permitted))
                            {
                                if (!permitted) errors.Add($"{at}: property '{name}' is not allowed by the contract");
                            }
                            else
                            {
                                Check(property, argument!, $"{at}.{name}", errors);
                            }
                        }
                    }
                    break;
                case "items":
                    if (value is JsonArray array)
                        for (var i = 0; i < array.Count; i++)
                            Check(array[i], argument!, $"{at}[{i}]", errors);
                    break;
                case "anyOf":
                    var branches = argument!.AsArray();
                    if (!branches.Any(branch => Passes(value, branch!, at)))
                        errors.Add($"{at}: {Show(value)} matches none of the anyOf branches");
                    break;
                case "if":
                    if (Passes(value, argument!, at) && schema["then"] is JsonNode then)
                        Check(value, then, at, errors);
                    else if (!Passes(value, argument!, at) && schema["else"] is JsonNode otherwise)
                        Check(value, otherwise, at, errors);
                    break;
                case "then":
                case "else":
                    break; // applied by "if"
                default:
                    if (!Annotations.Contains(keyword))
                        throw new NotSupportedException(
                            $"The request-schema validator does not implement '{keyword}' (at {at}). "
                                + "Teach OpenApiRequestSchemas.Check the keyword rather than ignoring it.");
                    break;
            }
        }
    }

    private bool Passes(JsonNode? value, JsonNode schema, string at)
    {
        var scratch = new List<string>();
        Check(value, schema, at, scratch);
        return scratch.Count == 0;
    }

    private JsonObject Resolve(string reference)
    {
        const string prefix = "#/components/schemas/";
        if (!reference.StartsWith(prefix, StringComparison.Ordinal))
            throw new NotSupportedException($"Unsupported $ref '{reference}'.");
        return _components[reference[prefix.Length..]] as JsonObject
            ?? throw new KeyNotFoundException($"Unresolved $ref '{reference}'.");
    }

    private static void CheckType(JsonNode? value, JsonNode type, string at, List<string> errors)
    {
        var accepted = type is JsonArray many
            ? many.Select(t => t!.GetValue<string>()).ToList()
            : new List<string> { type.GetValue<string>() };
        if (!accepted.Any(t => IsType(value, t)))
            errors.Add($"{at}: {Show(value)} is not of type {string.Join(" | ", accepted)}");
    }

    private static bool IsType(JsonNode? value, string type) => type switch
    {
        "null" => value is null,
        "object" => value is JsonObject,
        "array" => value is JsonArray,
        "string" => value is JsonValue s && s.TryGetValue<string>(out _),
        "boolean" => value is JsonValue b && b.TryGetValue<bool>(out _),
        "number" => value is JsonValue n && n.TryGetValue<double>(out _),
        "integer" => value is JsonValue i && i.TryGetValue<double>(out var d) && Math.Floor(d) == d,
        _ => throw new NotSupportedException($"Unknown JSON Schema type '{type}'."),
    };

    private static string Show(JsonNode? value) => value?.ToJsonString() ?? "null";

    // YAML to JSON with the YAML 1.2 core-schema reading of plain scalars, so
    // `const: true` and numeric enums compare as their JSON types, while quoted
    // scalars always stay strings.
    internal static JsonNode? ToJson(YamlNode node) => node switch
    {
        YamlMappingNode map => new JsonObject(map.Children.Select(kv =>
            KeyValuePair.Create(((YamlScalarNode)kv.Key).Value!, ToJson(kv.Value)))),
        YamlSequenceNode seq => new JsonArray(seq.Children.Select(ToJson).ToArray()),
        YamlScalarNode scalar => ScalarToJson(scalar),
        _ => throw new NotSupportedException($"Unsupported YAML node {node.NodeType}."),
    };

    private static JsonNode? ScalarToJson(YamlScalarNode scalar)
    {
        var text = scalar.Value ?? "";
        if (scalar.Style != ScalarStyle.Plain) return JsonValue.Create(text);
        switch (text)
        {
            case "null" or "~" or "": return null;
            case "true": return JsonValue.Create(true);
            case "false": return JsonValue.Create(false);
        }
        if (long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var integer))
            return JsonValue.Create(integer);
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var real))
            return JsonValue.Create(real);
        return JsonValue.Create(text);
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "EzClassify.Rest.sln")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Cannot locate EzClassify.Rest.sln");
    }
}
