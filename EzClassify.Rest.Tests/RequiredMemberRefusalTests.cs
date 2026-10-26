using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Xunit;
using YamlDotNet.RepresentationModel;

namespace EzClassify.Tests;

/// <summary>
/// A 200 that lacks a member the contract requires, at any depth, is a failure
/// and never a success to cache (cache contract C07).
///
/// The members come from the vendored OpenAPI document and from nothing else:
/// each recorded success is walked against its operation's response schema, and
/// every member that schema's <c>required</c> list names, wherever it sits in the
/// recorded body, is removed in turn. The public call that decoded the recording
/// has to refuse the body with the response-decoding exception, naming that
/// member. Nothing here is a list of members the client is known to require, so
/// a required member the client forgot to check, or a parser that fills one in
/// with a default, fails the member it forgot.
/// </summary>
public class RequiredMemberRefusalTests
{
    private static readonly Lazy<SpecResponses> Spec = new(SpecResponses.Load);

    /// <summary>
    /// Every driver whose recorded answer is a success the public call reads,
    /// the binding probe, health and discovery included.
    /// </summary>
    public static TheoryData<string> DecodedSuccesses()
    {
        var names = new TheoryData<string>();
        foreach (var name in WireCorpusReplayTests.SuccessDriverNames()) names.Add(name);
        return names;
    }

    [Theory]
    [MemberData(nameof(DecodedSuccesses))]
    public async Task EveryRequiredMemberIsRefusedWhenAbsent(string driver)
    {
        var recorded = WireCorpusReplayTests.CaseOf(driver);
        var members = Spec.Value.RequiredMembersIn(recorded);
        // An answer of an empty list carries no member to take away.
        if (members.Count == 0) return;

        var failures = new List<string>();
        foreach (var member in members)
        {
            var body = recorded.ResponseBody!.DeepClone();
            member.RemoveFrom(body);
            var error = await WireCorpusReplayTests.RunAgainstAsync(
                driver, WireCorpus.Vendored.WithResponseBody(recorded.Id, body));
            if (error is not JsonException refusal)
                failures.Add($"{member.JsonPath}: {(error is null ? "the call returned a result" : error.GetType().Name + ": " + error.Message)}");
            else if (refusal.Path != member.JsonPath)
                failures.Add($"{member.JsonPath}: refused as {refusal.Path} ({refusal.Message})");
        }
        Assert.True(failures.Count == 0,
            $"{driver}: {failures.Count} of {members.Count} required members were not refused naming themselves:\n"
            + string.Join("\n", failures.Take(20)));
    }

    [Fact]
    public void TheCorpusExercisesTheRequiredMembersOfEverySuccessfulOperation()
    {
        var covered = DecodedSuccessCases().Select(c => Spec.Value.OperationOf(c).Id).ToHashSet(StringComparer.Ordinal);
        var all = Spec.Value.OperationIds;
        var missing = all.Where(id => !covered.Contains(id) && id != NoPublicCall).OrderBy(id => id).ToList();
        Assert.True(missing.Count == 0, "No recorded success decodes: " + string.Join(", ", missing));
    }

    // The readiness probe has no public call; SpecOperationCoverageTests records why.
    private const string NoPublicCall = "getReady";

    private static IEnumerable<WireCase> DecodedSuccessCases() =>
        WireCorpusReplayTests.SuccessDriverNames().Select(WireCorpusReplayTests.CaseOf);

    /// <summary>One required member present in a recorded body.</summary>
    private sealed record Member(IReadOnlyList<object> Tokens, string JsonPath)
    {
        public void RemoveFrom(JsonNode root)
        {
            var node = root;
            foreach (var token in Tokens.Take(Tokens.Count - 1))
                node = token is int index ? node![index] : node![(string)token];
            ((JsonObject)node!).Remove((string)Tokens[^1]);
        }
    }

    private sealed record Operation(string Id, JsonNode ResponseSchema);

    private sealed class SpecResponses
    {
        private readonly JsonObject _components;
        private readonly List<(string Method, string[] Segments, Operation Operation)> _operations = new();

        private SpecResponses(JsonObject root)
        {
            _components = (JsonObject)root["components"]!["schemas"]!;
            foreach (var (path, item) in (JsonObject)root["paths"]!)
                foreach (var (method, operation) in (JsonObject)item!)
                {
                    var success = ((JsonObject)operation!["responses"]!).FirstOrDefault(r => r.Key.StartsWith('2'));
                    var schema = success.Value?["content"]?["application/json"]?["schema"];
                    if (schema is null) continue;
                    _operations.Add((method, path.Trim('/').Split('/'),
                        new Operation(operation["operationId"]!.GetValue<string>(), schema)));
                }
        }

        public IEnumerable<string> OperationIds => _operations.Select(o => o.Operation.Id);

        public static SpecResponses Load()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "EzClassify.Rest.sln")))
                directory = directory.Parent;
            var specPath = Path.Combine(directory?.FullName ?? throw new DirectoryNotFoundException("Cannot locate EzClassify.Rest.sln"),
                "vendor", "spec", "openapi.yaml");
            var yaml = new YamlStream();
            using (var reader = new StreamReader(specPath)) yaml.Load(reader);
            return new SpecResponses((JsonObject)OpenApiRequestSchemas.ToJson(yaml.Documents[0].RootNode)!);
        }

        public Operation OperationOf(WireCase recorded)
        {
            var segments = recorded.Target.Split('?')[0].Trim('/').Split('/');
            var matches = _operations.Where(o =>
                    o.Method.Equals(recorded.Method, StringComparison.OrdinalIgnoreCase)
                    && o.Segments.Length == segments.Length
                    && o.Segments.Zip(segments, (template, actual) => template.StartsWith('{') || template == actual).All(x => x))
                .ToList();
            Assert.True(matches.Count == 1, $"{recorded.Id}: {matches.Count} operations match {recorded.Method} {recorded.Target}");
            return matches[0].Operation;
        }

        public IReadOnlyList<Member> RequiredMembersIn(WireCase recorded)
        {
            var found = new List<Member>();
            Collect(OperationOf(recorded).ResponseSchema, recorded.ResponseBody, new List<object>(), "$", found);
            return found;
        }

        private JsonNode? Resolve(JsonNode? schema)
        {
            while (schema is JsonObject obj && obj["$ref"] is JsonNode reference)
                schema = _components[reference.GetValue<string>().Split('/').Last()];
            return schema;
        }

        private void Collect(JsonNode? schemaNode, JsonNode? value, List<object> tokens, string path, List<Member> found)
        {
            if (Resolve(schemaNode) is not JsonObject schema || value is null) return;
            var alternatives = (schema["anyOf"] ?? schema["oneOf"]) as JsonArray;
            if (alternatives is not null)
            {
                // The alternative the value satisfies by name: every member it requires is
                // present and every constant it fixes is the one the value carries.
                var chosen = alternatives.Select(Resolve).OfType<JsonObject>().FirstOrDefault(alternative => Describes(alternative, value));
                if (chosen is not null) Collect(chosen, value, tokens, path, found);
                return;
            }
            switch (value)
            {
                case JsonObject obj:
                    foreach (var name in (schema["required"] as JsonArray ?? new JsonArray()).Select(n => n!.GetValue<string>()))
                        if (obj.ContainsKey(name))
                            found.Add(new Member(tokens.Append(name).ToList(), path + Segment(name)));
                    var declared = schema["properties"] as JsonObject ?? new JsonObject();
                    foreach (var (name, member) in obj)
                    {
                        JsonNode? memberSchema = declared.TryGetPropertyValue(name, out var property)
                            ? property
                            : schema["additionalProperties"] as JsonObject;
                        Collect(memberSchema, member, tokens.Append(name).ToList(), path + Segment(name), found);
                    }
                    break;
                case JsonArray array:
                    for (var i = 0; i < array.Count; i++)
                        Collect(schema["items"], array[i], tokens.Append(i).ToList(), $"{path}[{i}]", found);
                    break;
            }
        }

        private bool Describes(JsonObject alternative, JsonNode value)
        {
            if (value is not JsonObject obj) return alternative["type"]?.GetValue<string>() == "array" && value is JsonArray;
            var required = (alternative["required"] as JsonArray ?? new JsonArray()).Select(n => n!.GetValue<string>());
            if (!required.All(obj.ContainsKey)) return false;
            foreach (var (name, property) in alternative["properties"] as JsonObject ?? new JsonObject())
                if (Resolve(property) is JsonObject propertySchema && propertySchema["const"] is JsonNode constant
                    && obj[name]?.ToJsonString() != constant.ToJsonString())
                    return false;
            return true;
        }

        private static string Segment(string name) =>
            name.All(c => char.IsLetterOrDigit(c) || c == '_') ? "." + name : $"['{name.Replace("'", "\\'")}']";
    }
}
