using System.Collections.Generic;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Xunit;

namespace EzClassify.Tests;

/// <summary>
/// Assertions are the engine's state transport: a caller feeds the assertions one
/// response returns into the next request. These tests pin that the transport is
/// lossless, including for an enum value this SDK does not know and for every
/// optional field the contract's <c>Assertion</c> declares.
/// </summary>
public class AssertionRoundTripTests
{
    private const string Resolution =
        """{"contextId":"acme-marking","resolvedVersion":"1.0.0","artifacts":[],"sources":[]}""";

    private static string Envelope(string result) =>
        $$"""{"resolution":{{Resolution}},"operationLimitations":[],"result":{{result}}}""";

    private static JsonNode SentBody(CtxMockHandler handler, string route)
    {
        var (_, path, body) = handler.Requests.FindLast(r => r.Path.EndsWith(route, System.StringComparison.Ordinal));
        Assert.NotNull(path);
        return JsonNode.Parse(body!)!;
    }

    [Fact]
    public async Task ProcessThenAggregateSendsAnUnknownConfidenceBackByteForByte()
    {
        // The banner rollup path: portion assertions from process go straight into
        // aggregate. A newer server's confidence value must go back exactly as it
        // came, in its original case, so the server that sent it can accept it.
        var routes = new Dictionary<string, (HttpStatusCode, string)>
        {
            ["/scheme/process"] = (HttpStatusCode.OK, Envelope("""
                {"valid":true,"rendered":"H","inputWasValid":true,"renders":{},"assessment":{"emitted":[],"omitted":[],"collapsed":[],"conflicts":[],"unrepresentable":[]},"notices":[],"errors":[],"warnings":[],"appliedFixes":[],"proposedFixes":[],
                 "assertions":[
                   {"conceptId":"L_H","categoryId":"level","confidence":"probable","source":"parse"},
                   {"conceptId":"A_X","categoryId":"audience","confidence":"Needs-Review","source":"parse"},
                   {"conceptId":"A_Y","categoryId":"audience","confidence":"inferred","source":"inference"}]}
                """)),
            ["/scheme/aggregate"] = (HttpStatusCode.OK, Envelope(
                """{"valid":true,"rendered":"H","assertions":[],"errors":[],"warnings":[],"appliedFixes":[],"proposedFixes":[],"notices":[]}""")),
        };
        var (client, handler) = await CtxClientFactory.ConnectAsync(routes);
        using (client)
        {
            var portion = (await client.Scheme!.ProcessAsync("H")).Result.Assertions;

            var rollup = await client.Scheme.AggregateAssertionsAsync(new[] { portion, portion });

            Assert.Equal("H", rollup.Result.Rendered);
            var sets = SentBody(handler, "/scheme/aggregate")["assertionSets"]!.AsArray();
            Assert.Equal(2, sets.Count);
            foreach (var set in sets)
            {
                Assert.Equal("probable", set![0]!["confidence"]!.GetValue<string>());
                Assert.Equal("Needs-Review", set[1]!["confidence"]!.GetValue<string>());
                // Negative control: a value this SDK knows goes out unchanged too.
                Assert.Equal("inferred", set[2]!["confidence"]!.GetValue<string>());
            }
        }
    }

    [Fact]
    public async Task AnAssertionRequestCarriesEveryFieldTheContractDeclares()
    {
        // Access check reads expiresAt to downgrade a time-bounded marking, so an
        // assertion projection that drops it silently changes the decision input.
        var routes = new Dictionary<string, (HttpStatusCode, string)>
        {
            ["/access/check"] = (HttpStatusCode.OK, """{ "granted": false, "rules": [] }"""),
        };
        using var metadata = JsonDocument.Parse("""{"origin":"import","weight":2}""");
        var provenance = new SourceReference(
            ProviderId: "idp",
            Attribution: new Attribution("IdP", "internal", LicenseUrl: "https://example.test/license", Citation: "IdP directory"),
            Reproducibility: SourceReproducibility.Reproducible,
            SnapshotId: "snap-1",
            Locator: "idp://levels/high");
        var assertion = new Assertion(
            CategoryId: "level",
            ConceptId: "LEVEL_HIGH",
            Confidence: Confidence.Repaired,
            Source: "repair",
            ExpiresAt: "2030-01-01T00:00:00Z",
            Metadata: metadata.RootElement.Clone(),
            Provenance: provenance,
            ReplacedConceptId: "LEVEL_OLD",
            SourceRule: "rule-7",
            Value: "H");

        var (client, handler) = await CtxClientFactory.ConnectAsync(routes);
        using (client)
        {
            await client.Access!.CheckAsync(new[] { assertion }, """{"clearance":"high"}""");

            var sent = SentBody(handler, "/access/check")["assertions"]![0]!;
            var expected = JsonNode.Parse("""
                {"conceptId":"LEVEL_HIGH","categoryId":"level","confidence":"repaired","source":"repair",
                 "sourceRule":"rule-7","expiresAt":"2030-01-01T00:00:00Z","value":"H",
                 "metadata":{"origin":"import","weight":2},"replacedConceptId":"LEVEL_OLD",
                 "provenance":{"providerId":"idp",
                   "attribution":{"name":"IdP","license":"internal","licenseUrl":"https://example.test/license","citation":"IdP directory"},
                   "reproducibility":"reproducible","snapshotId":"snap-1","locator":"idp://levels/high"}}
                """);
            Assert.True(JsonNode.DeepEquals(expected, sent), $"Sent {sent.ToJsonString()}");
        }
    }

    [Fact]
    public async Task AnAssertionWithOnlyRequiredFieldsSendsOnlyThose()
    {
        // Negative control: absent optional fields stay off the wire rather than going out as null.
        var routes = new Dictionary<string, (HttpStatusCode, string)>
        {
            ["/access/check"] = (HttpStatusCode.OK, """{ "granted": false, "rules": [] }"""),
        };
        var (client, handler) = await CtxClientFactory.ConnectAsync(routes);
        using (client)
        {
            await client.Access!.CheckAsync(
                new[] { new Assertion("level", "LEVEL_HIGH", Confidence.Certain, "parse") }, """{"clearance":"high"}""");

            var sent = SentBody(handler, "/access/check")["assertions"]![0]!;
            var expected = JsonNode.Parse(
                """{"conceptId":"LEVEL_HIGH","categoryId":"level","confidence":"certain","source":"parse"}""");
            Assert.True(JsonNode.DeepEquals(expected, sent), $"Sent {sent.ToJsonString()}");
        }
    }

    [Fact]
    public async Task ProcessReadsReplacedConceptIdSoItSurvivesTheNextRequest()
    {
        var routes = new Dictionary<string, (HttpStatusCode, string)>
        {
            ["/scheme/process"] = (HttpStatusCode.OK, Envelope("""
                {"valid":true,"rendered":"H","inputWasValid":true,"renders":{},"assessment":{"emitted":[],"omitted":[],"collapsed":[],"conflicts":[],"unrepresentable":[]},"notices":[],"errors":[],"warnings":[],"appliedFixes":[],"proposedFixes":[],
                 "assertions":[{"conceptId":"L_NEW","categoryId":"level","confidence":"repaired","source":"modernize",
                                "replacedConceptId":"L_OLD"}]}
                """)),
        };
        var (client, _) = await CtxClientFactory.ConnectAsync(routes);
        using (client)
        {
            var assertion = (await client.Scheme!.ProcessAsync("H")).Result.Assertions[0];

            Assert.Equal("L_OLD", assertion.ReplacedConceptId);
            Assert.Equal("L_NEW", assertion.ConceptId);
        }
    }
}
