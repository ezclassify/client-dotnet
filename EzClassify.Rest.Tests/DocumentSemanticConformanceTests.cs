using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace EzClassify.Tests;

/// <summary>
/// Replays every frozen document-assessment case through the public .NET Context
/// surface. The test intentionally treats the vendored corpus as an immutable
/// wire contract: both request construction and response parsing must preserve
/// every canonical field that the contract references.
/// </summary>
public class DocumentSemanticConformanceTests
{

    // Provenance of the vendored bundle. The spec and its conformance corpus live in
    // ezclassify/contracts; the spec bot is what writes vendor/.
    //
    // These assertions are about SHAPE and INTERNAL CONSISTENCY, never about one
    // blessed commit. `specDigest` is computed over the spec bot's own bytes,
    // so a change to the bot re-stales every mirror without a single spec byte
    // moving. A literal SHA pinned here would go red on a legitimate re-vendor, and
    // the only thing that could update it is a human, while the bot that invalidated
    // it writes vendor/ alone. That is a gate guaranteed to fail for the wrong reason.
    // Everything below survives a re-vendor and goes red only when the provenance is
    // actually wrong: a hash that does not match the bytes it claims to describe, a
    // MANIFEST and a SPEC.md that disagree, or a tree that cannot name its source.
    private static readonly Regex Sha1Hex = new("^[0-9a-f]{40}$", RegexOptions.Compiled);
    private static readonly Regex Sha256Hex = new("^[0-9a-f]{64}$", RegexOptions.Compiled);
    private static readonly Regex SourcePackageName = new(@"^@ezclassify/[a-z0-9-]+$", RegexOptions.Compiled);
    private static readonly Regex SourcePackageVersion = new(@"^\d+\.\d+\.\S*$", RegexOptions.Compiled);
    private static readonly Regex OpenApiInfoVersion = new(@"^ {2}version:\s*(\S+)\s*$", RegexOptions.Multiline);
    private static readonly string[] ExpectedSemanticFiles =
    {
        "spec/semantic/v1/document-assessment.contract.json",
        "spec/semantic/v1/document-assessment.schema.json",
        "spec/semantic/v1/fixtures/clause-coverage.json",
        "spec/semantic/v1/fixtures/clause-coverage.schema.json",
        "spec/semantic/v1/fixtures/context.json",
        "spec/semantic/v1/fixtures/context.schema.json",
        "spec/semantic/v1/fixtures/recordings.json",
        "spec/semantic/v1/fixtures/recordings.schema.json",
        "spec/semantic/v1/fixtures/requests.json",
        "spec/semantic/v1/fixtures/requests.schema.json",
    };
    private static readonly Regex PathSegment = new(@"^(?<name>[^\[]+)(?:\[(?<index>\*|\d+)\])?$", RegexOptions.Compiled);

    [Fact]
    public async Task CanonicalDocumentAssessmentContract_ReplaysThroughPublicDtosWithoutFieldLoss()
    {
        var root = FindRepoRoot();
        var semanticRoot = Path.Combine(root, "vendor", "spec", "semantic", "v1");
        VerifySemanticMirrorIntegrity(root, semanticRoot);

        var contract = ReadObject(Path.Combine(semanticRoot, "document-assessment.contract.json"));
        var requests = ReadObject(Path.Combine(semanticRoot, "fixtures", "requests.json"));
        var recordings = ReadObject(Path.Combine(semanticRoot, "fixtures", "recordings.json"))["cases"]!.AsObject();
        var coverage = ReadObject(Path.Combine(semanticRoot, "fixtures", "clause-coverage.json"));
        var fixtureContext = ReadObject(Path.Combine(semanticRoot, "fixtures", "context.json"));
        var cases = contract["cases"]!.AsArray();

        // The corpus is the count. Cases run 1..N below and every inventory has to
        // name exactly those N, so a dropped or added case fails without a pinned
        // total to move.
        Assert.NotEmpty(cases);
        Assert.Equal(cases.Count, requests.Count);
        Assert.Equal(cases.Count, recordings.Count);
        Assert.Equal(cases.Count, coverage.Count);
        Assert.Equal("recorded-operation-output", ReadObject(Path.Combine(semanticRoot, "fixtures", "recordings.json"))["status"]!.GetValue<string>());

        var semanticIds = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < cases.Count; index++)
        {
            var contractCase = cases[index]!.AsObject();
            var requestId = contractCase["requestId"]!.GetValue<string>();
            Assert.Equal(index + 1, contractCase["number"]!.GetValue<int>());
            Assert.Equal($"case-{index + 1:D2}", requestId);
            Assert.True(contractCase["required"]!.GetValue<bool>());
            Assert.Equal("recorded", contractCase["implementation"]!.GetValue<string>());
            Assert.True(semanticIds.Add(contractCase["semanticId"]!.GetValue<string>()));
            Assert.True(requests.ContainsKey(requestId), $"{requestId} has no canonical request.");
            Assert.True(recordings.ContainsKey(requestId), $"{requestId} has no canonical recording.");
            Assert.True(coverage.ContainsKey(requestId), $"{requestId} has no clause coverage.");

            var recipe = requests[requestId]!.AsObject();
            var canonicalOutput = recordings[requestId]!["output"]!.AsObject();
            var context = SemanticContextFor(semanticRoot, fixtureContext, recipe, requestId);
            AssertVariantMatchesRecording(recipe, canonicalOutput, context, requestId);
            var replay = await ReplayThroughPublicSurfaceAsync(
                context, contractCase["operation"]!.GetValue<string>(), recipe, canonicalOutput);

            AssertCanonicalSubset(NormalizeCanonicalOutput(canonicalOutput), replay.Output, requestId + ".output");
            AssertPublicRequestPreservesCanonicalPayload(contractCase["operation"]!.GetValue<string>(), recipe, replay.Request, requestId);
            foreach (var assertion in contractCase["assertions"]!.AsArray())
                AssertSemanticAssertion(replay.Output, assertion!.AsObject(), requestId, canonicalOutput.ContainsKey("error"));
        }
    }

    /// <summary>
    /// Resolves the Context a semantic request runs against from the published
    /// variant map. The base fixtures apply unless the request names a
    /// <c>fixtureVariant</c>, whose scheme fixture then replaces the base one. The
    /// Context's identity is the one that scheme fixture declares.
    /// </summary>
    private static SemanticContext SemanticContextFor(string semanticRoot, JsonObject fixtureContext, JsonObject recipe, string requestId)
    {
        var schemeFixture = RequiredString(fixtureContext, "schemeFixture");
        if (OptionalString(recipe, "fixtureVariant") is { } variantName)
        {
            var variant = fixtureContext["variants"]?[variantName] as JsonObject;
            Assert.True(variant is not null, $"{requestId} names undeclared fixture variant {variantName}.");
            if (OptionalString(variant!, "schemeFixture") is { } replacement) schemeFixture = replacement;
        }
        var path = Path.GetFullPath(Path.Combine(semanticRoot, "fixtures", schemeFixture));
        var scheme = ReadObject(path)["scheme"] as JsonObject;
        Assert.True(scheme is not null, $"{path} declares no scheme.");
        return new SemanticContext(RequiredString(scheme!, "id"), RequiredString(scheme!, "version"));
    }

    /// <summary>
    /// Holds a variant case to the Context its recording names, which proves the
    /// runner read the variant map the way the recorder did.
    /// </summary>
    private static void AssertVariantMatchesRecording(JsonObject recipe, JsonObject canonicalOutput, SemanticContext context, string requestId)
    {
        if (OptionalString(recipe, "fixtureVariant") is null) return;
        if (canonicalOutput["resolution"] is not JsonObject resolution || OptionalString(resolution, "contextId") is not { } recorded) return;
        Assert.True(recorded == context.Id, $"{requestId} ran against Context {context.Id}, recorded against {recorded}.");
    }

    private static async Task<SemanticReplay> ReplayThroughPublicSurfaceAsync(
        SemanticContext context,
        string operation,
        JsonObject recipe,
        JsonObject canonicalOutput)
    {
        var operationPath = operation switch
        {
            "auditDocument" => context.Path + "/document/audit",
            "assessDocumentAccess" => context.Path + "/document/assess-access",
            "documentAccessRequirements" => context.Path + "/access/describe-requirements",
            // The .NET SDK has no CLI, but this case records a document-audit
            // envelope. Replaying it through AuditAsync still proves the shared
            // request and DTO contract instead of blessing a fixture clone.
            "cliDocument" => context.Path + "/document/audit",
            _ => throw new Xunit.Sdk.XunitException($"Unsupported semantic operation {operation}."),
        };

        using var handler = new SemanticContextHandler(context, operationPath, CanonicalHttpResponse(canonicalOutput));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:8080") };
        using var client = await EzClassifyClient.ConnectAsync(http, context.Id, context.Version);

        JsonNode output;
        try
        {
            object result = operation switch
            {
                "auditDocument" => await client.Document!.AuditAsync(
                    RecipeText(recipe), DecodeGuideDeterminations(recipe), RecipeProfile(recipe)),
                "assessDocumentAccess" => await client.Document!.AssessAccessAsync(
                    RecipeText(recipe), Json(recipe, "userAttributes", "{}"), Json(recipe, "context", "{}"),
                    DecodeGuideDeterminations(recipe), RecipeProfile(recipe)),
                "documentAccessRequirements" => await ReplayRequirementsAsync(client.Access!, recipe),
                "cliDocument" => await client.Document!.AuditAsync(
                    RecipeText(recipe), DecodeGuideDeterminations(recipe), RecipeProfile(recipe)),
                _ => throw new Xunit.Sdk.XunitException($"Unsupported semantic operation {operation}."),
            };
            output = ToWireNode(result)!;
        }
        catch (EzClassifyClientException exception)
        {
            output = PublicErrorFrom(exception);
        }
        handler.AssertExpectedRoutes();
        return new SemanticReplay(handler.OperationRequest!, output);
    }

    private static async Task<ContextAccessRequirementsResult> ReplayRequirementsAsync(ContextAccessOps access, JsonObject recipe)
    {
        var assertions = DecodeAssertions(recipe["assertions"] as JsonArray);
        var marking = OptionalString(recipe, "marking");
        if (marking is not null && assertions.Count > 0) return await access.DescribeRequirementsAsync(marking, assertions);
        if (marking is not null) return await access.DescribeRequirementsAsync(marking);
        return await access.DescribeRequirementsAsync(assertions);
    }

    private static HttpResponseSpec CanonicalHttpResponse(JsonObject canonicalOutput)
    {
        if (canonicalOutput["error"] is not JsonObject error)
            return new HttpResponseSpec(HttpStatusCode.OK, canonicalOutput.ToJsonString());

        var detail = error["detail"]!.GetValue<string>();
        var code = error["code"]!.GetValue<string>();
        var status = detail.StartsWith("stale-", StringComparison.Ordinal) ? HttpStatusCode.Conflict
            : code == "NO_ACCESS_POLICY" ? HttpStatusCode.UnprocessableEntity
            : HttpStatusCode.BadRequest;
        // The semantic recorder stores a local error DTO. REST uses the frozen
        // flat error body that EzClassifyClientException exposes to .NET callers.
        return new HttpResponseSpec(status, new JsonObject
        {
            ["error"] = error["message"]!.GetValue<string>(),
            ["code"] = code,
            ["detail"] = detail,
        }.ToJsonString());
    }

    private static JsonObject PublicErrorFrom(EzClassifyClientException exception)
    {
        var body = JsonNode.Parse(exception.ResponseBody ?? "{}") as JsonObject;
        return new JsonObject
        {
            ["error"] = body?["error"]?.GetValue<string>() ?? exception.Message,
            ["code"] = exception.Code,
            ["detail"] = exception.Detail,
        };
    }

    private static JsonObject NormalizeCanonicalOutput(JsonObject output)
    {
        if (output["error"] is not JsonObject error) return output;
        return new JsonObject
        {
            ["error"] = error["message"]!.GetValue<string>(),
            ["code"] = error["code"]!.GetValue<string>(),
            ["detail"] = error["detail"]!.GetValue<string>(),
        };
    }

    private static void AssertPublicRequestPreservesCanonicalPayload(string operation, JsonObject recipe, JsonNode request, string requestId)
    {
        var operationKeys = operation switch
        {
            "auditDocument" or "cliDocument" => new[] { "text", "guideDeterminations", "responseProfile" },
            "assessDocumentAccess" => new[] { "text", "userAttributes", "context", "guideDeterminations", "responseProfile" },
            "documentAccessRequirements" => new[] { "marking", "assertions" },
            _ => Array.Empty<string>(),
        };
        var expected = new JsonObject();
        foreach (var key in operationKeys)
            if (recipe[key] is not null) expected[key] = recipe[key]!.DeepClone();
        AssertCanonicalSubset(expected, request, requestId + ".request");
    }

    private static void AssertSemanticAssertion(JsonNode output, JsonObject assertion, string requestId, bool isPublicError)
    {
        var path = assertion["path"]!.GetValue<string>();
        if (isPublicError && path.StartsWith("error.", StringComparison.Ordinal)) path = path[6..];
        var matches = ResolvePath(output, path).ToList();
        switch (assertion["operator"]!.GetValue<string>())
        {
            case "equals":
                Assert.True(matches.Count == 1, $"{requestId}: expected one match for {path}, found {matches.Count}.");
                AssertJsonEqual(assertion["value"], matches[0], requestId + ": " + path);
                break;
            case "present":
                Assert.NotEmpty(matches);
                break;
            case "absent":
                // Optional DTO members normalize an omitted wire property to
                // null. Both representations satisfy the contract's absent
                // operator; explicit equals-null assertions remain distinct.
                Assert.True(matches.Count == 0 || matches.All(match => match is null), $"{requestId}: expected no value for {path}.");
                break;
            case "minItems":
                Assert.True(matches.Count == 1, $"{requestId}: expected one match for {path}, found {matches.Count}.");
                Assert.True(matches[0] is JsonArray array && array.Count >= assertion["value"]!.GetValue<int>(), requestId + ": " + path);
                break;
            case "contains":
                Assert.True(matches.Count == 1, $"{requestId}: expected one match for {path}, found {matches.Count}.");
                Assert.True(matches[0] is JsonArray values && values.Any(value => JsonNode.DeepEquals(value, assertion["value"])), requestId + ": " + path);
                break;
            default:
                throw new Xunit.Sdk.XunitException($"{requestId}: unsupported semantic operator {assertion["operator"]}.");
        }
    }

    private static IEnumerable<JsonNode?> ResolvePath(JsonNode root, string path)
    {
        IEnumerable<JsonNode?> current = new JsonNode?[] { root };
        foreach (var rawSegment in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            var match = PathSegment.Match(rawSegment);
            if (!match.Success) yield break;
            var next = new List<JsonNode?>();
            foreach (var node in current)
            {
                if (node is not JsonObject obj || !obj.TryGetPropertyValue(match.Groups["name"].Value, out var property)) continue;
                if (!match.Groups["index"].Success) { next.Add(property); continue; }
                if (property is not JsonArray array) continue;
                var index = match.Groups["index"].Value;
                if (index == "*") next.AddRange(array.Where(value => value is not null).Select(value => value!));
                else if (int.TryParse(index, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) && parsed < array.Count && array[parsed] is JsonNode item) next.Add(item);
            }
            current = next;
        }
        foreach (var node in current) yield return node;
    }

    private static void AssertCanonicalSubset(JsonNode? expected, JsonNode? actual, string path)
    {
        if (expected is null) { Assert.True(actual is null, path + $": expected null, received {actual}"); return; }
        Assert.True(actual is not null, path + $": expected {expected.ToJsonString()}, received null");
        if (expected is JsonObject expectedObject)
        {
            var actualObject = Assert.IsType<JsonObject>(actual);
            foreach (var property in expectedObject)
            {
                Assert.True(actualObject.ContainsKey(property.Key), path + ": missing " + property.Key);
                AssertCanonicalSubset(property.Value, actualObject[property.Key], path + "." + property.Key);
            }
            return;
        }
        if (expected is JsonArray expectedArray)
        {
            var actualArray = Assert.IsType<JsonArray>(actual);
            Assert.Equal(expectedArray.Count, actualArray.Count);
            for (var index = 0; index < expectedArray.Count; index++)
                AssertCanonicalSubset(expectedArray[index], actualArray[index], path + "[" + index + "]");
            return;
        }
        AssertJsonEqual(expected, actual, path);
    }

    private static void AssertJsonEqual(JsonNode? expected, JsonNode? actual, string path)
    {
        Assert.True(JsonNode.DeepEquals(expected, actual), path + $": expected {expected}, received {actual}");
    }

    private static JsonNode? ToWireNode(object? value)
    {
        if (value is null) return null;
        if (value is JsonNode node) return node.DeepClone();
        if (value is JsonElement element) return JsonNode.Parse(element.GetRawText());
        if (value is string or bool or int or long or double or decimal) return JsonValue.Create(value);
        // Wire enums are extensible structs that carry their own converter; going
        // through it proves the public DTOs serialize their raw tokens.
        if (value.GetType().GetCustomAttribute<JsonConverterAttribute>() is not null)
            return JsonSerializer.SerializeToNode(value, value.GetType());
        if (value is IEnumerable enumerable)
        {
            var array = new JsonArray();
            foreach (var item in enumerable) array.Add(ToWireNode(item));
            return array;
        }

        var result = new JsonObject();
        foreach (var property in value.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public).Where(property => property.CanRead))
            result[ToCamelCase(property.Name)] = ToWireNode(property.GetValue(value));
        return result;
    }

    private static string ToCamelCase(string value) => char.ToLowerInvariant(value[0]) + value[1..];

    private static GuideDeterminationInput? DecodeGuideDeterminations(JsonObject recipe)
    {
        if (recipe["guideDeterminations"] is not JsonObject raw) return null;
        var findings = raw["findings"]!.AsArray().Select(item =>
        {
            var finding = item!.AsObject();
            return new GuideFindingDetermination(
                finding["findingId"]!.GetValue<string>(), finding["documentDigest"]!.GetValue<string>(),
                GuideFindingDecision.Parse(finding["decision"]!.GetValue<string>()), finding["reason"]!.GetValue<string>(),
                finding["proposalOrigin"] is JsonValue origin ? DecisionProposalOrigin.Parse(origin.GetValue<string>()) : null);
        }).ToList();
        var manual = raw["manualSelections"]!.AsArray().Select(item =>
        {
            var selection = item!.AsObject();
            return new ManualGuideSelectionInput(
                selection["documentDigest"]!.GetValue<string>(), DecodeRule(selection["rule"]!.AsObject()),
                DecodeEvidence(selection["evidence"]!.AsArray()), selection["reason"]!.GetValue<string>(),
                selection["proposalOrigin"] is JsonValue origin ? DecisionProposalOrigin.Parse(origin.GetValue<string>()) : null);
        }).ToList();
        return new GuideDeterminationInput(findings, manual);
    }

    private static List<Assertion> DecodeAssertions(JsonArray? raw) => raw?.Select(item =>
    {
        var assertion = item!.AsObject();
        return new Assertion(
            assertion["categoryId"]!.GetValue<string>(), assertion["conceptId"]!.GetValue<string>(),
            assertion["confidence"] is JsonValue confidence ? Confidence.Parse(confidence.GetValue<string>()) : Confidence.Certain,
            assertion["source"] is JsonValue source ? source.GetValue<string>() : "semantic-conformance",
            Value: OptionalString(assertion, "value"));
    }).ToList() ?? new List<Assertion>();

    private static RuleReference DecodeRule(JsonObject raw)
    {
        var artifact = raw["artifact"]!.AsObject();
        var source = raw["source"] as JsonObject;
        return new RuleReference(
            new ArtifactReference(ArtifactKind.Parse(artifact["kind"]!.GetValue<string>()), artifact["id"]!.GetValue<string>(), artifact["version"]!.GetValue<string>(), OptionalString(artifact, "digest")),
            raw["ruleId"]!.GetValue<string>(), raw["ruleDigest"]!.GetValue<string>(),
            source is null ? null : new RuleSourceReference(source["providerId"]!.GetValue<string>(), SourceReproducibility.Parse(source["reproducibility"]!.GetValue<string>()), OptionalString(source, "snapshotId")));
    }

    private static List<ContentEvidenceSpan> DecodeEvidence(JsonArray raw) => raw.Select(item =>
    {
        var evidence = item!.AsObject();
        return new ContentEvidenceSpan(evidence["start"]!.GetValue<int>(), evidence["end"]!.GetValue<int>(), evidence["offsetEncoding"]!.GetValue<string>(), DecodeRule(evidence["rule"]!.AsObject()), OptionalString(evidence, "matched"));
    }).ToList();

    private static string RecipeText(JsonObject recipe)
    {
        if (recipe["text"] is JsonValue text) return text.GetValue<string>();
        if (recipe.ContainsKey("textRecipes")) return " \t\n";
        if (recipe.ContainsKey("invalidInputRecipes")) return "\uD800";
        throw new Xunit.Sdk.XunitException("Canonical request has no public text payload.");
    }

    private static DocumentResponseProfile? RecipeProfile(JsonObject recipe)
    {
        if (recipe["responseProfile"] is JsonValue profile) return DocumentResponseProfile.Parse(profile.GetValue<string>());
        if (recipe["responseProfiles"] is JsonArray profiles && profiles.Any(item => item?.GetValue<string>() == "standard")) return DocumentResponseProfile.Standard;
        return null;
    }

    private static string Json(JsonObject objectValue, string key, string fallback) => objectValue[key]?.ToJsonString() ?? fallback;
    private static string? OptionalString(JsonObject objectValue, string key) => objectValue[key] is JsonValue value ? value.GetValue<string>() : null;
    private static JsonObject ReadObject(string path) => JsonNode.Parse(File.ReadAllBytes(path))!.AsObject();

    private static string RequiredString(JsonObject objectValue, string key)
    {
        if (objectValue[key] is JsonValue value && value.TryGetValue<string>(out var text)) return text;
        throw new Xunit.Sdk.XunitException($"MANIFEST.json is missing the required string field '{key}'.");
    }

    private static void VerifySemanticMirrorIntegrity(string root, string semanticRoot)
    {
        var manifest = ReadObject(Path.Combine(root, "vendor", "spec", "MANIFEST.json"));
        var specMd = File.ReadAllText(Path.Combine(root, "vendor", "SPEC.md"));
        // Only the spec bot can compute these: the upstream contracts
        // commit and the digests over the whole bundle. Anything re-vendored by
        // hand has to leave a placeholder, so the hex-shape assertions below are
        // what make such a tree fail loudly rather than ship a mirror claiming
        // provenance nobody can trace.
        var contractsSha = RequiredString(manifest, "contractsSha");
        AssertMatches(Sha1Hex, contractsSha, "MANIFEST.json 'contractsSha'");
        AssertMatches(Sha256Hex, RequiredString(manifest, "specDigest"), "MANIFEST.json 'specDigest'");
        AssertMatches(Sha256Hex, RequiredString(manifest, "openapiSha256"), "MANIFEST.json 'openapiSha256'");
        AssertMatches(Sha256Hex, RequiredString(manifest, "specBundleSha256"), "MANIFEST.json 'specBundleSha256'");

        // The recorded openapi hash and version must describe the openapi.yaml that
        // is actually committed next to them. This is the half of the manifest that
        // can be checked against bytes in the tree; specDigest cannot, because it is
        // computed over the spec bot's inputs, which do not live here.
        var openapiBytes = File.ReadAllBytes(Path.Combine(root, "vendor", "spec", "openapi.yaml"));
        Assert.Equal(
            Convert.ToHexString(SHA256.HashData(openapiBytes)).ToLowerInvariant(),
            RequiredString(manifest, "openapiSha256"));
        var declaredVersion = OpenApiInfoVersion.Match(Encoding.UTF8.GetString(openapiBytes));
        Assert.True(declaredVersion.Success, "vendor/spec/openapi.yaml declares no info.version.");
        Assert.Equal(declaredVersion.Groups[1].Value, RequiredString(manifest, "openapiVersion"));

        var sourcePackages = manifest["sourcePackages"] as JsonObject;
        Assert.True(sourcePackages is not null, "MANIFEST.json is missing the 'sourcePackages' provenance block.");
        Assert.True(
            sourcePackages!.Count > 0,
            "MANIFEST.json 'sourcePackages' is empty; the bundle names no source package.");
        foreach (var entry in sourcePackages)
        {
            AssertMatches(SourcePackageName, entry.Key, "sourcePackages key");
            AssertMatches(SourcePackageVersion, RequiredString(sourcePackages, entry.Key), $"sourcePackages['{entry.Key}']");
        }

        // Two files, one fact: SPEC.md's provenance table and MANIFEST.json are
        // written in the same pass, so a disagreement means the writer half-applied
        // a change. Matched loosely on whitespace so column padding in the generated
        // table cannot break the gate.
        Assert.Equal(contractsSha, ProvenanceRow(specMd, "contracts@"));
        var specSourceUrl = ProvenanceRow(specMd, "specSourceUrl");
        Assert.True(
            specSourceUrl is not null
                && specSourceUrl.Contains("github.com/ezclassify/contracts", StringComparison.Ordinal)
                && specSourceUrl.Contains(contractsSha, StringComparison.Ordinal),
            $"vendor/SPEC.md 'specSourceUrl' must point at ezclassify/contracts at {contractsSha}; found: {specSourceUrl ?? "(no row)"}");

        var files = Directory.EnumerateFiles(Path.Combine(root, "vendor", "spec"), "*", SearchOption.AllDirectories)
            .Where(path => !string.Equals(Path.GetFileName(path), "MANIFEST.json", StringComparison.Ordinal))
            // The spec bot sorts with JavaScript localeCompare;
            // invariant-culture ordering matches that contract for the
            // ASCII spec paths (notably corpus.json before README.md).
            .OrderBy(path => Path.GetRelativePath(Path.Combine(root, "vendor"), path).Replace(Path.DirectorySeparatorChar, '/'), StringComparer.InvariantCulture)
            .ToList();
        Assert.True(files.Count > 0, "vendor/spec holds no files to hash.");
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var semanticFiles = new List<string>();
        foreach (var file in files)
        {
            var relative = Path.GetRelativePath(Path.Combine(root, "vendor"), file).Replace(Path.DirectorySeparatorChar, '/');
            if (relative.StartsWith("spec/semantic/v1/", StringComparison.Ordinal)) semanticFiles.Add(relative);
            hash.AppendData(Encoding.UTF8.GetBytes(relative));
            hash.AppendData(new byte[] { 0 });
            // The spec bot appends the lowercase hexadecimal content hash,
            // not its raw 32-byte digest. Keep this byte-for-byte compatible
            // with the canonical publisher so this gate rejects a fabricated
            // manifest as well as a missing semantic fixture.
            hash.AppendData(Encoding.UTF8.GetBytes(
                Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))).ToLowerInvariant()));
            hash.AppendData(new byte[] { (byte)'\n' });
        }
        Assert.Equal(ExpectedSemanticFiles, semanticFiles.ToArray());
        // Recomputes the publisher's bundle hash over the committed tree and holds it
        // against what the manifest recorded. This is the assertion that makes the
        // whole vendored file set tamper-evident without pinning a blessed value: any
        // added, removed or edited file under vendor/spec moves the computed hash, and
        // a re-vendor moves both sides together.
        var actual = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        Assert.Equal(RequiredString(manifest, "specBundleSha256"), actual);
    }

    private static void AssertMatches(Regex pattern, string value, string what)
    {
        Assert.True(pattern.IsMatch(value), $"{what} does not match {pattern}: '{value}'");
    }

    /// <summary>
    /// Reads one value out of the two-column provenance table in vendor/SPEC.md.
    /// </summary>
    /// <param name="specMd">The contents of vendor/SPEC.md.</param>
    /// <param name="field">Row label, without the surrounding backticks.</param>
    /// <returns>The row's value cell, trimmed of whitespace and backticks, or null when there is no such row.</returns>
    private static string? ProvenanceRow(string specMd, string field)
    {
        var match = Regex.Match(
            specMd,
            @"^\|\s*`" + Regex.Escape(field) + @"`\s*\|([^|]*)\|",
            RegexOptions.Multiline);
        return match.Success ? match.Groups[1].Value.Replace("`", string.Empty).Trim() : null;
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "EzClassify.Rest.sln"))) return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Cannot locate EzClassify.Rest.sln");
    }

    private sealed record SemanticReplay(JsonNode Request, JsonNode Output);

    /// <summary>The Context a replayed request is sent to.</summary>
    private sealed record SemanticContext(string Id, string Version)
    {
        public string Path => "/v1/ctx/" + Id + "/" + Version;
    }
    private sealed record HttpResponseSpec(HttpStatusCode Status, string Body);

    private sealed class SemanticContextHandler : HttpMessageHandler
    {
        private readonly SemanticContext _context;
        private readonly string _operationPath;
        private readonly HttpResponseSpec _response;
        private readonly List<string> _requestPaths = new();
        public JsonNode? OperationRequest { get; private set; }

        public SemanticContextHandler(SemanticContext context, string operationPath, HttpResponseSpec response)
        {
            _context = context;
            _operationPath = operationPath;
            _response = response;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.PathAndQuery ?? "";
            _requestPaths.Add(path);
            if (path == _context.Path)
            {
                return WithVersion(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(ContextProbe.Body, Encoding.UTF8, "application/json"),
                });
            }
            if (path == _operationPath)
            {
                OperationRequest = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!;
                return WithVersion(new HttpResponseMessage(_response.Status)
                {
                    Content = new StringContent(_response.Body, Encoding.UTF8, "application/json"),
                });
            }
            throw new Xunit.Sdk.XunitException($"Unexpected semantic replay route: {path}");
        }

        public void AssertExpectedRoutes()
        {
            Assert.Equal(new[] { _context.Path, _operationPath }, _requestPaths);
        }

        private HttpResponseMessage WithVersion(HttpResponseMessage response)
        {
            response.Headers.TryAddWithoutValidation("X-EzClassify-Resolved-Version", _context.Version);
            return response;
        }
    }
}
