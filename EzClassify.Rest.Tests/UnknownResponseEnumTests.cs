using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Xunit;

namespace EzClassify.Tests;

/// <summary>
/// Responses are open for enum values too: a value a newer server adds decodes
/// instead of failing the call, and keeps its exact token.
/// </summary>
/// <remarks>
/// The contract is open-response and closed-request. Every wire enum is an
/// extensible struct over its raw token, so the rest of the response still
/// reaches the caller and the value reports <c>IsKnown == false</c>. The cases
/// cover each kind of place an enum sits: a field of the result, elements of a
/// list of models, a model nested in the result, and the source-reference
/// discriminator in every envelope's resolution. Closed requests are the
/// server's job: the client sends a decoded token back unchanged and lets the
/// server that sent it decide.
/// </remarks>
public class UnknownResponseEnumTests
{
    private const string Resolution =
        """{"contextId":"acme-marking","resolvedVersion":"1.0.0","artifacts":[],"sources":[]}""";

    private static string Envelope(string result, string resolution = Resolution) =>
        $$"""{"resolution":{{resolution}},"operationLimitations":[],"result":{{result}}}""";

    private static async Task<(EzClassifyClient Client, CtxMockHandler Handler)> Connect(string route, string body) =>
        await CtxClientFactory.ConnectAsync(new Dictionary<string, (HttpStatusCode, string)>
        {
            [route] = (HttpStatusCode.OK, body),
        });

    [Fact]
    public async Task AnUnknownValueInATopLevelFieldKeepsItsToken()
    {
        var (client, _) = await Connect("/document/audit", """
            {"resolution":{"contextId":"acme-marking","resolvedVersion":"1.0.0","artifacts":[],"sources":[]},
             "operationLimitations":[],
             "responseProjection":{"profile":"compact","detailsComplete":false,"omittedFields":[],"counts":{"nominations":0,"evidenceSpans":0,"guideUnits":0,"accessRuleResults":0,"contentRequirements":0,"contextRequirements":0}},
             "result":{"whole":null,"wholes":[],"wholeValidation":null,"parts":[],"wholeCoversParts":null,"unmarkedSpans":[],
               "proceduralFlags":[],"overallVerdict":"needs-review","vocabulary":null,"documentDigest":"sha256:doc",
               "guideAssessment":null,"markingRequirements":{"whole":false,"part":false},"limitations":[]}}
            """);
        using (client)
        {
            var audit = await client.Document!.AuditAsync("d");

            Assert.Equal("needs-review", audit.Result.OverallVerdict.ToString());
            Assert.False(audit.Result.OverallVerdict.IsKnown);
            Assert.Equal("compact", audit.ResponseProjection!.Profile.ToString());
            Assert.False(audit.ResponseProjection.Profile.IsKnown);
            Assert.Equal("sha256:doc", audit.Result.DocumentDigest);
        }
    }

    [Fact]
    public async Task UnknownValuesInListElementsKeepTheirTokens()
    {
        var (client, _) = await Connect("/scheme/process", Envelope("""
            {"valid":false,"rendered":"H","inputWasValid":true,"renders":{},"assessment":{"emitted":[],"omitted":[],"collapsed":[],"conflicts":[],"unrepresentable":[]},"notices":[],
             "assertions":[{"conceptId":"L_H","categoryId":"level","confidence":"probable","source":"parse"}],
             "errors":[{"constraintId":"c1","message":"m","severity":"fatal"}],
             "warnings":[{"kind":"advisory","message":"w"}],
             "appliedFixes":[{"ruleId":"r","action":"merge","message":"m"}],"proposedFixes":[]}
            """));
        using (client)
        {
            var result = (await client.Scheme!.ProcessAsync("H")).Result;

            Assert.Equal(new Confidence("probable"), result.Assertions[0].Confidence);
            Assert.False(result.Assertions[0].Confidence.IsKnown);
            Assert.Equal("L_H", result.Assertions[0].ConceptId);
            Assert.Equal("fatal", result.Errors[0].Severity.ToString());
            Assert.False(result.Errors[0].Severity!.Value.IsKnown);
            Assert.Equal("advisory", result.Warnings[0].Kind.ToString());
            Assert.Equal("merge", result.AppliedFixes[0].Action.ToString());
            Assert.False(result.AppliedFixes[0].Action.IsKnown);
        }
    }

    [Fact]
    public async Task UnknownValuesInANestedModelKeepTheirTokens()
    {
        var (client, _) = await Connect("/scheme/scan", Envelope("""
            {"results":[{"marking":"H","start":0,"end":1,"role":"region","type":"t","wrapped":false,
                "result":{"valid":true,"rendered":"H","inputWasValid":true,"assertions":[],"errors":[],"warnings":[],"appliedFixes":[],"notices":[],
                          "assessment":{"emitted":[],"omitted":[],"collapsed":[],"conflicts":[],"unrepresentable":[{"output":"marking/xml","reason":"unwritable-here"}]},
                          "recognition":{"status":"settled","role":"region","unmatched":[]}}}],
             "limits":{"status":"paused","limitHit":"memory","regionsFound":1}}
            """));
        using (client)
        {
            var limits = (await client.Scheme!.ScanAsync("H")).Result.Limits;
            var scanned = (await client.Scheme.ScanAsync("H")).Result.Results[0];

            Assert.Equal("paused", limits.Status.ToString());
            Assert.False(limits.Status.IsKnown);
            Assert.Equal("memory", limits.LimitHit.ToString());
            Assert.Equal(1, limits.RegionsFound);
            Assert.Equal("region", scanned.Role.ToString());
            Assert.False(scanned.Role.IsKnown);
            Assert.Equal("settled", scanned.Result.Recognition!.Status.ToString());
            Assert.False(scanned.Result.Recognition.Status.IsKnown);
            Assert.False(scanned.Result.Recognition.Role!.Value.IsKnown);
            var refused = Assert.IsType<UnrepresentableOutput>(Assert.Single(scanned.Result.Assessment.Unrepresentable));
            Assert.Equal("unwritable-here", refused.Reason.ToString());
            Assert.False(refused.Reason.IsKnown);
        }
    }

    [Fact]
    public async Task AnUnknownDiscriminatorAndArtifactKindKeepTheirTokens()
    {
        var (client, _) = await Connect("/scheme/process", Envelope(
            """{"valid":true,"rendered":"H","inputWasValid":true,"renders":{},"assessment":{"emitted":[],"omitted":[],"collapsed":[],"conflicts":[],"unrepresentable":[]},"notices":[],"assertions":[],"errors":[],"warnings":[],"appliedFixes":[],"proposedFixes":[]}""",
            """
            {"contextId":"acme-marking","resolvedVersion":"1.0.0",
             "artifacts":[{"kind":"policy","id":"p","version":"1.0.0"}],
             "sources":[{"providerId":"idp","reproducibility":"pinned","snapshotId":"snap-1","attribution":{"name":"IdP","license":"internal"}}]}
            """));
        using (client)
        {
            var resolution = (await client.Scheme!.ProcessAsync("H")).Resolution;

            Assert.Equal("policy", resolution.Artifacts[0].Kind.ToString());
            Assert.False(resolution.Artifacts[0].Kind.IsKnown);
            Assert.Equal("pinned", resolution.Sources[0].Reproducibility.ToString());
            Assert.False(resolution.Sources[0].Reproducibility.IsKnown);
            Assert.Equal("snap-1", resolution.Sources[0].SnapshotId);
        }
    }

    [Fact]
    public async Task ARequestCarriesAnUnknownTokenBackUnchanged()
    {
        // The server that sent a value decides whether it may come back; the client
        // neither refuses nor rewrites it.
        var (client, handler) = await Connect("/document/audit", "{}");
        using (client)
        {
            var determinations = new GuideDeterminationInput(
                new[] { new GuideFindingDetermination("f1", "sha256:doc", new GuideFindingDecision("deferred"), "r", new DecisionProposalOrigin("imported-by-agent")) },
                Array.Empty<ManualGuideSelectionInput>());

            try
            {
                await client.Document!.AuditAsync("d", determinations, new DocumentResponseProfile("compact"));
            }
            catch (Exception e) when (e is not ArgumentException and not InvalidOperationException)
            {
                // The canned body is not an audit report; only the request matters here.
            }

            var sent = JsonNode.Parse(handler.Requests[^1].Body!)!;
            Assert.Equal("compact", sent["responseProfile"]!.GetValue<string>());
            var finding = sent["guideDeterminations"]!["findings"]![0]!;
            Assert.Equal("deferred", finding["decision"]!.GetValue<string>());
            Assert.Equal("imported-by-agent", finding["proposalOrigin"]!.GetValue<string>());
        }
    }

    [Fact]
    public async Task ARequestWithKnownValuesIsSent()
    {
        // Negative control for the round trip above.
        var (client, handler) = await Connect("/scheme/render", """{"rendered":"H"}""");
        using (client)
        {
            try
            {
                await client.Scheme!.RenderAsync(
                    new[] { new Assertion("level", "L_H", Confidence.Inferred, "parse") });
            }
            catch (Exception e) when (e is not ArgumentException and not InvalidOperationException)
            {
                // The canned body may not satisfy the render parser; only the request matters here.
            }

            var (_, path, body) = handler.Requests[^1];
            Assert.EndsWith("/scheme/render", path);
            Assert.Contains("\"confidence\":\"inferred\"", body);
        }
    }

    [Fact]
    public async Task ADefaultValueCarriesNoTokenAndIsRefusedBeforeItIsSent()
    {
        // default(T) is not a value any server sent; it is an unset field.
        var (client, handler) = await Connect("/scheme/render", """{"rendered":"H"}""");
        using (client)
        {
            var before = handler.Requests.Count;

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => client.Scheme!.RenderAsync(new[] { new Assertion("level", "L_H", default, "parse") }));
            Assert.Equal(before, handler.Requests.Count);
        }
    }
}
