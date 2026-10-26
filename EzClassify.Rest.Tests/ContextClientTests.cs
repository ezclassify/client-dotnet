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

/// <summary>
/// Mock handler for context-bound routing tests. Matches a response by the first
/// registered key the request path contains; otherwise serves a describe stub
/// (the connect-time probe). Stamps the resolved-version header on every reply
/// and records each request for path/body assertions.
/// </summary>
internal sealed class CtxMockHandler : HttpMessageHandler
{
    private readonly Dictionary<string, (HttpStatusCode Status, string Body)> _byContains;
    private readonly string _describeBody;

    // The scheme sub-resources are reads of the composed scheme, not operations
    // over it, so they answer flat rather than inside the Context envelope. Only
    // the operation routes get an envelope wrapped around a bare result body
    // here. apply-preset is deliberately absent from this list: it runs the
    // engine, so it is enveloped like every other operation.
    private static readonly string[] FlatSchemeResources =
    {
        "/scheme/concepts",
        "/scheme/categories",
        "/scheme/formats",
        "/scheme/grammar",
        "/scheme/scopes",
        "/scheme/label-presets",
        "/access/rules",
    };

    public string? ResolvedVersionHeader { get; set; } = "1.0.0";
    public List<(string Method, string Path, string? Body)> Requests { get; } = new();

    public CtxMockHandler(
        Dictionary<string, (HttpStatusCode, string)>? byContains = null,
        string describeBody = ContextProbe.Body)
    {
        _byContains = byContains ?? new();
        _describeBody = describeBody;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var path = request.RequestUri?.PathAndQuery ?? "";
        string? body = request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add((request.Method.Method, path, body));

        var status = HttpStatusCode.OK;
        var respBody = _describeBody;
        foreach (var kv in _byContains)
        {
            if (path.Contains(kv.Key))
            {
                status = kv.Value.Status;
                respBody = kv.Value.Body;
                break;
            }
        }

        var flatResource = Array.Exists(FlatSchemeResources, suffix => path.Contains(suffix));
        if (status == HttpStatusCode.OK
            && !flatResource
            && (path.Contains("/scheme/") || path.Contains("/guide/") || path.Contains("/access/") || path.Contains("/crosswalk/"))
            && !respBody.Contains("\"resolution\""))
        {
            respBody = """{"resolution":{"contextId":"acme-marking","resolvedVersion":"1.0.0","artifacts":[],"sources":[]},"result":"""
                + respBody
                + ""","operationLimitations":[]}""";
        }

        var resp = new HttpResponseMessage(status)
        {
            Content = new StringContent(respBody, Encoding.UTF8, "application/json"),
        };
        if (ResolvedVersionHeader is not null)
            resp.Headers.TryAddWithoutValidation("X-EzClassify-Resolved-Version", ResolvedVersionHeader);
        return resp;
    }
}

/// <summary>
/// Handler that serves the connect-time describe probe normally but throws
/// HttpRequestException for every context op, simulating a connection drop
/// after a successful connect.
/// </summary>
internal sealed class CtxOpConnectionFailHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var path = request.RequestUri?.PathAndQuery ?? "";
        if (path.Contains("/scheme/") || path.Contains("/guide/") || path.Contains("/access/") || path.Contains("/crosswalk/") || path.Contains("/document/"))
            throw new HttpRequestException("Connection refused");

        var resp = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(ContextProbe.Body, Encoding.UTF8, "application/json"),
        };
        resp.Headers.TryAddWithoutValidation("X-EzClassify-Resolved-Version", "1.0.0");
        return Task.FromResult(resp);
    }
}

internal static class CtxClientFactory
{
    public static async Task<(EzClassifyClient client, CtxMockHandler handler)> ConnectAsync(
        Dictionary<string, (HttpStatusCode, string)>? routes = null,
        string contextId = "acme-marking",
        string? version = "1.0.0",
        string resolvedVersion = "1.0.0",
        ContextCacheOptions? cacheOptions = null)
    {
        var handler = new CtxMockHandler(routes) { ResolvedVersionHeader = resolvedVersion };
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:8080") };
        var client = cacheOptions is null
            ? await EzClassifyClient.ConnectAsync(http, contextId, version)
            : await EzClassifyClient.ConnectAsync(http, contextId, version, RestClientCredentials.None, cacheOptions);
        return (client, handler);
    }
}

/// <summary>Tests for the context-bound client surface (ConnectAsync + ctx.Scheme/Guide/Access/Document).</summary>
public class ContextClientTests
{
    // Every marking result carries the assessment of its one rendering.
    internal const string EmptyAssessment = """{ "emitted": [], "omitted": [], "collapsed": [], "conflicts": [], "unrepresentable": [] }""";

    private const string ProcessJson = """
        { "valid": true, "rendered": "H", "inputWasValid": true,
          "renders": {},
          "assessment": { "emitted": [], "omitted": [], "collapsed": [], "conflicts": [], "unrepresentable": [] },
          "assertions": [ { "conceptId": "LEVEL_HIGH", "categoryId": "level", "confidence": "certain", "source": "parse" } ],
          "errors": [], "warnings": [], "appliedFixes": [], "proposedFixes": [],
          "notices": [ { "conceptId": "LEVEL_HIGH", "conceptName": "Level High", "type": "handling", "text": "Handle per policy", "required": true } ] }
        """;

    [Fact]
    public async Task RepeatedContextProcessSharesOneWireRequest()
    {
        var handler = new CtxMockHandler(new Dictionary<string, (HttpStatusCode, string)>
        {
            ["/scheme/process"] = (HttpStatusCode.OK, ProcessJson),
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:8080") };
        using var client = await EzClassifyClient.ConnectAsync(http, "acme-marking", "1.0.0");

        var first = await client.Scheme!.ProcessAsync("H");
        var second = await client.Scheme.ProcessAsync("H");

        Assert.Equal("H", first.Result.Rendered);
        Assert.Equal("H", second.Result.Rendered);
        Assert.Single(handler.Requests.FindAll(request => request.Path.Contains("/scheme/process")));
    }

    [Fact]
    public async Task ClearCacheForcesAFreshRead()
    {
        var routes = new Dictionary<string, (HttpStatusCode, string)>
        {
            ["/scheme/process"] = (HttpStatusCode.OK, ProcessJson),
        };
        var (client, handler) = await CtxClientFactory.ConnectAsync(routes);
        using (client)
        {
            await client.Scheme!.ProcessAsync("H");
            await client.Scheme.ProcessAsync("H");
            Assert.Single(handler.Requests.FindAll(request => request.Path.Contains("/scheme/process")));

            client.ClearCache();
            await client.Scheme.ProcessAsync("H");
            Assert.Equal(2, handler.Requests.FindAll(request => request.Path.Contains("/scheme/process")).Count);
        }
    }

    [Fact]
    public async Task ConnectionTimeCacheOptionsDisableResponseCaching()
    {
        var routes = new Dictionary<string, (HttpStatusCode, string)>
        {
            ["/scheme/process"] = (HttpStatusCode.OK, ProcessJson),
        };
        var (client, handler) = await CtxClientFactory.ConnectAsync(
            routes, cacheOptions: new ContextCacheOptions(Enabled: false));
        using (client)
        {
            await client.Scheme!.ProcessAsync("H");
            await client.Scheme.ProcessAsync("H");
            Assert.Equal(2, handler.Requests.FindAll(request => request.Path.Contains("/scheme/process")).Count);
        }
    }

    [Fact]
    public async Task ApplyPresetCacheSeparatesOutputFormats()
    {
        var routes = new Dictionary<string, (HttpStatusCode, string)>
        {
            ["/scheme/apply-preset"] = (HttpStatusCode.OK, ValidateJson),
        };
        var (client, handler) = await CtxClientFactory.ConnectAsync(routes);
        using (client)
        {
            await client.Scheme!.ApplyPresetAsync("starter", outputFormat: "alpha");
            await client.Scheme.ApplyPresetAsync("starter", outputFormat: "beta");
            await client.Scheme.ApplyPresetAsync("starter", outputFormat: "alpha");
            Assert.Equal(2, handler.Requests.FindAll(request => request.Path.Contains("/scheme/apply-preset")).Count);
        }
    }

    [Fact]
    public async Task AccessDecisionsReachServerUnlessPinnedCallerOptsIn()
    {
        var routes = new Dictionary<string, (HttpStatusCode, string)>
        {
            ["/access/check"] = (HttpStatusCode.OK, """{ "granted": false, "rules": [] }"""),
        };
        var (client, handler) = await CtxClientFactory.ConnectAsync(routes);
        using (client)
        {
            await client.Access!.CheckByMarkingAsync("H", "{}");
            await client.Access.CheckByMarkingAsync("H", "{}");
            Assert.Equal(2, handler.Requests.FindAll(request => request.Path.Contains("/access/check")).Count);
        }

        var (optedIn, optedInHandler) = await CtxClientFactory.ConnectAsync(
            routes, cacheOptions: new ContextCacheOptions(CacheAccessDecisions: true));
        using (optedIn)
        {
            await optedIn.Access!.CheckByMarkingAsync("H", "{}");
            await optedIn.Access.CheckByMarkingAsync("H", "{}");
            Assert.Single(optedInHandler.Requests.FindAll(request => request.Path.Contains("/access/check")));
        }
    }

    [Theory]
    [InlineData("latest")]
    [InlineData(null)]
    public async Task AccessDecisionCachingIsRefusedAtConnectionForAnUnpinnedBinding(string? version)
    {
        var options = new ContextCacheOptions(CacheAccessDecisions: true);

        var handler = new CtxMockHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:8080") };
        var refusal = await Assert.ThrowsAsync<ArgumentException>(() => EzClassifyClient.ConnectAsync(
            http, "acme-marking", version, RestClientCredentials.None, options));
        Assert.Equal("cacheOptions", refusal.ParamName);
        Assert.Empty(handler.Requests);

        await Assert.ThrowsAsync<ArgumentException>(() => EzClassifyClient.ConnectAsync(
            "http://localhost:8080", "acme-marking", version, RestClientCredentials.None, options));
    }

    [Fact]
    public async Task NullCacheOptionsAreRefusedAtConnection()
    {
        using var http = new HttpClient(new CtxMockHandler()) { BaseAddress = new Uri("http://localhost:8080") };
        await Assert.ThrowsAsync<ArgumentNullException>(() => EzClassifyClient.ConnectAsync(
            http, "acme-marking", "1.0.0", RestClientCredentials.None, null!));
    }

    // --- connect / version resolution -------------------------------------

    [Fact]
    public async Task ConnectAsync_DefaultVersion_ProbesLatestAndSurfacesResolvedVersion()
    {
        var handler = new CtxMockHandler() { ResolvedVersionHeader = "2.3.4" };
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:8080") };
        using var client = await EzClassifyClient.ConnectAsync(http, "acme-marking", version: null);

        Assert.Equal("2.3.4", client.ResolvedVersion);
        Assert.Single(handler.Requests);
        Assert.Equal("/v1/ctx/acme-marking/latest", handler.Requests[0].Path);
        Assert.Equal("GET", handler.Requests[0].Method);
    }

    [Fact]
    public async Task ConnectAsync_ConcreteVersion_UsesItInProbePath()
    {
        var (client, handler) = await CtxClientFactory.ConnectAsync(version: "1.2.0", resolvedVersion: "1.2.0");
        using (client)
        {
            Assert.Equal("1.2.0", client.ResolvedVersion);
            Assert.Equal("/v1/ctx/acme-marking/1.2.0", handler.Requests[0].Path);
        }
    }

    [Fact]
    public async Task ConnectAsync_EncodesContextIdSpecialChars()
    {
        var handler = new CtxMockHandler();
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:8080") };
        using var client = await EzClassifyClient.ConnectAsync(http, "tenant a/marking", "1.0.0+gov");
        Assert.Equal("/v1/ctx/tenant%20a%2Fmarking/1.0.0%2Bgov", handler.Requests[0].Path);
    }

    [Fact]
    public async Task ResolvedVersion_UpdatesFromEveryOpResponse()
    {
        var routes = new Dictionary<string, (HttpStatusCode, string)>
        {
            ["/scheme/process"] = (HttpStatusCode.OK, ProcessJson),
        };
        var handler = new CtxMockHandler(routes) { ResolvedVersionHeader = "1.0.0" };
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:8080") };
        using var client = await EzClassifyClient.ConnectAsync(http, "acme-marking", "latest");
        Assert.Equal("1.0.0", client.ResolvedVersion);

        handler.ResolvedVersionHeader = "1.4.2";
        await client.Scheme!.ProcessAsync("H");
        Assert.Equal("1.4.2", client.ResolvedVersion);
    }

    // --- scheme ops --------------------------------------------------------

    [Fact]
    public async Task Process_RoutesToCtxPath_AndOmitsSchemeId()
    {
        var routes = new Dictionary<string, (HttpStatusCode, string)>
        {
            ["/scheme/process"] = (HttpStatusCode.OK, ProcessJson),
        };
        var (client, handler) = await CtxClientFactory.ConnectAsync(routes);
        using (client)
        {
            var result = await client.Scheme!.ProcessAsync("LEVEL-HIGH");
            Assert.True(result.Result.Valid);
            Assert.Equal("H", result.Result.Rendered);
            Assert.Single(result.Result.Assertions);
            // The process result carries the notices the scheme attaches, so a
            // caller does not follow up with a separate notices call.
            Assert.Single(result.Result.Notices);
            Assert.Equal("LEVEL_HIGH", result.Result.Notices[0].ConceptId);
            Assert.True(result.Result.Notices[0].Required);

            var (_, path, body) = handler.Requests[^1];
            Assert.Equal("/v1/ctx/acme-marking/1.0.0/scheme/process", path);
            Assert.Contains("\"input\":\"LEVEL-HIGH\"", body);
            Assert.DoesNotContain("schemeId", body);
        }
    }

    [Fact]
    public async Task Process_SendsOutputsAndSuggestForAndOmitsThemWhenUnset()
    {
        var routes = new Dictionary<string, (HttpStatusCode, string)>
        {
            ["/scheme/process"] = (HttpStatusCode.OK, ProcessJson),
        };
        var (client, handler) = await CtxClientFactory.ConnectAsync(routes);
        using (client)
        {
            await client.Scheme!.ProcessAsync("H", outputs: new[] { "marking/short", "marking/long" }, suggestFor: new[] { "audience" });
            Assert.Equal(
                """{"input":"H","outputs":["marking/short","marking/long"],"suggestFor":["audience"]}""",
                handler.Requests[^1].Body);

            // Nothing beyond the one rendering is asked for unless named.
            await client.Scheme!.ProcessAsync("H");
            Assert.Equal("""{"input":"H"}""", handler.Requests[^1].Body);
        }
    }

    [Fact]
    public async Task Process_SendsTypedTextAndChosenFactsTogether()
    {
        var routes = new Dictionary<string, (HttpStatusCode, string)>
        {
            ["/scheme/process"] = (HttpStatusCode.OK, ProcessJson),
        };
        var (client, handler) = await CtxClientFactory.ConnectAsync(routes);
        using (client)
        {
            var chosen = new Assertion("level", "level-high", Confidence.Certain, "stated");
            await client.Scheme!.ProcessAsync("high//staff", assertions: new[] { chosen });
            Assert.Equal(
                """{"input":"high//staff","assertions":[{"conceptId":"level-high","categoryId":"level","confidence":"certain","source":"stated"}]}""",
                handler.Requests[^1].Body);
        }
    }

    [Fact]
    public async Task Process_ParsesNamedRendersSuggestionsAndCorrections()
    {
        // The process instance the contract publishes: high//staff with two
        // outputs and the audience picker named.
        var routes = new Dictionary<string, (HttpStatusCode, string)>
        {
            ["/scheme/process"] = (HttpStatusCode.OK, """
                { "assertions": [ { "categoryId": "level", "conceptId": "level-high", "confidence": "certain", "source": "parse" },
                                  { "categoryId": "audience", "conceptId": "audience-staff", "confidence": "certain", "source": "parse" } ],
                  "valid": true, "inputWasValid": true, "errors": [], "warnings": [],
                  "appliedFixes": [ { "ruleId": "_recognition_case", "action": "reorder", "message": "Read `high` as `HIGH` (case)." } ],
                  "rendered": "HIGH//STAFF",
                  "assessment": { "emitted": [ { "conceptId": "level-high", "categoryId": "level" }, { "conceptId": "audience-staff", "categoryId": "audience" } ], "omitted": [], "collapsed": [], "conflicts": [], "unrepresentable": [] },
                  "recognition": { "status": "complete", "inputFormat": "long", "surface": "marking", "unmatched": [],
                                   "corrections": [ { "start": 0, "end": 4, "original": "high", "replacement": "HIGH", "reason": "case" } ] },
                  "notices": [], "displayColor": { "background": "#b00020", "text": "#ffffff" },
                  "proposedFixes": [],
                  "renders": {
                    "marking/short": { "rendered": "H//STF", "assessment": { "emitted": [], "omitted": [], "collapsed": [], "conflicts": [], "unrepresentable": [] } },
                    "marking/long": { "rendered": "HIGH//STAFF", "assessment": { "emitted": [], "omitted": [], "collapsed": [], "conflicts": [], "unrepresentable": [] } } },
                  "suggestionsFor": { "audience": { "suggestions": [ { "conceptId": "audience-staff", "categoryId": "audience", "label": "STAFF", "description": "Staff only", "name": "Staff", "views": { "long": "STAFF", "short": "STF" }, "group": "Audience", "selected": true, "order": 0 } ], "warnings": [] } } }
                """),
        };
        var (client, _) = await CtxClientFactory.ConnectAsync(routes);
        using (client)
        {
            var result = (await client.Scheme!.ProcessAsync(
                "high//staff", outputs: new[] { "marking/short", "marking/long" }, suggestFor: new[] { "audience" })).Result;

            Assert.Equal("HIGH//STAFF", result.Rendered);
            Assert.Equal(2, result.Assessment.Emitted.Count);
            Assert.Equal(new[] { "marking/short", "marking/long" }, result.Renders.Keys);
            Assert.Equal("H//STF", result.Renders["marking/short"].Rendered);
            var correction = Assert.Single(result.Recognition!.Corrections);
            Assert.Equal(RecognitionCorrectionReason.Case, correction.Reason);
            Assert.Equal("HIGH", correction.Replacement);
            var audience = result.SuggestionsFor!["audience"];
            Assert.True(Assert.Single(audience.Suggestions).Selected);
            Assert.Empty(audience.Warnings);
        }
    }

    [Fact]
    public async Task Process_WithoutSuggestForLeavesSuggestionsAbsent()
    {
        var routes = new Dictionary<string, (HttpStatusCode, string)>
        {
            ["/scheme/process"] = (HttpStatusCode.OK, ProcessJson),
        };
        var (client, _) = await CtxClientFactory.ConnectAsync(routes);
        using (client)
        {
            var result = (await client.Scheme!.ProcessAsync("H")).Result;
            // Absent and empty are different answers: nothing was asked for.
            Assert.Null(result.SuggestionsFor);
            Assert.Empty(result.Renders);
        }
    }

    [Fact]
    public async Task Process_ForwardsContextVerbatimAndRejectsNonObjectContext()
    {
        var routes = new Dictionary<string, (HttpStatusCode, string)>
        {
            ["/scheme/process"] = (HttpStatusCode.OK, ProcessJson),
        };
        var (client, handler) = await CtxClientFactory.ConnectAsync(routes);
        using (client)
        {
            await client.Scheme!.ProcessAsync("H", contextJson: """{"role":"part","partOf":{"rendered":"H//A"}}""");
            Assert.Contains("\"context\":{\"role\":\"part\",\"partOf\":{\"rendered\":\"H//A\"}}", handler.Requests[^1].Body);

            var sent = handler.Requests.Count;
            var error = await Assert.ThrowsAsync<ArgumentException>(
                () => client.Scheme!.ProcessAsync("H", contextJson: "[]"));
            Assert.Equal("contextJson", error.ParamName);
            Assert.Equal(sent, handler.Requests.Count);
        }
    }

    [Fact]
    public async Task Process_SendsPartsAndOmitsThemWhenUnset()
    {
        var routes = new Dictionary<string, (HttpStatusCode, string)>
        {
            ["/scheme/process"] = (HttpStatusCode.OK, ProcessJson),
        };
        var (client, handler) = await CtxClientFactory.ConnectAsync(routes);
        using (client)
        {
            await client.Scheme!.ProcessAsync("H//A", parts: new[] { "H//B", "H//C" });
            Assert.Equal("""{"input":"H//A","parts":["H//B","H//C"]}""", handler.Requests[^1].Body);

            await client.Scheme!.ProcessAsync("H//A");
            Assert.DoesNotContain("parts", handler.Requests[^1].Body);
        }
    }

    // The validate instance the contract publishes: HIGH read in the long view,
    // settled by an inference, so valid but not valid as written.
    private const string ValidateJson = """
        { "assertions": [ { "categoryId": "level", "conceptId": "level-high", "confidence": "certain", "source": "parse" },
                          { "categoryId": "audience", "conceptId": "audience-staff", "confidence": "inferred", "source": "infer" } ],
          "valid": true, "inputWasValid": false, "errors": [], "warnings": [],
          "appliedFixes": [ { "ruleId": "high-needs-staff", "action": "add", "add": [ { "conceptId": "audience-staff", "categoryId": "audience" } ], "triggerConceptIds": [ "level-high" ], "message": "Added STAFF: HIGH requires it." } ],
          "rendered": "HIGH//STAFF",
          "assessment": { "emitted": [ { "conceptId": "level-high", "categoryId": "level" }, { "conceptId": "audience-staff", "categoryId": "audience" } ], "omitted": [], "collapsed": [], "conflicts": [], "unrepresentable": [] },
          "recognition": { "status": "complete", "inputFormat": "sensitivity/full", "role": "whole", "surface": "sensitivity", "unmatched": [] },
          "notices": [], "displayColor": { "background": "#b00020", "text": "#ffffff" } }
        """;

    [Fact]
    public async Task Validate_RoutesToCtxPath_AndParses()
    {
        var routes = new Dictionary<string, (HttpStatusCode, string)>
        {
            ["/scheme/validate"] = (HttpStatusCode.OK, ValidateJson),
        };
        var (client, handler) = await CtxClientFactory.ConnectAsync(routes);
        using (client)
        {
            var result = (await client.Scheme!.ValidateAsync("HIGH")).Result;
            // Valid once settled, not valid as written: the client reports both
            // exactly as the server judged them.
            Assert.True(result.Valid);
            Assert.False(result.InputWasValid);
            Assert.Equal(FixAction.Add, Assert.Single(result.AppliedFixes).Action);
            Assert.Equal("HIGH//STAFF", result.Rendered);
            Assert.Equal(2, result.Assessment.Emitted.Count);
            Assert.Equal("sensitivity/full", result.Recognition!.InputFormat);
            Assert.Equal(StructuralRole.Whole, result.Recognition.Role);
            Assert.Equal("sensitivity", result.Recognition.Surface);
            Assert.Equal("#b00020", result.DisplayColor!.Background);
            Assert.Equal("/v1/ctx/acme-marking/1.0.0/scheme/validate", handler.Requests[^1].Path);
            Assert.Equal("""{"input":"HIGH"}""", handler.Requests[^1].Body);
        }
    }

    [Fact]
    public async Task Validate_RecognitionWithNoDecidedViewHasNoRoleOrFormat()
    {
        // CONF reads exactly in more than one view, so no view and no role is
        // decided; the client invents neither.
        var routes = new Dictionary<string, (HttpStatusCode, string)>
        {
            ["/scheme/validate"] = (HttpStatusCode.OK, """
                { "assertions": [ { "categoryId": "level", "conceptId": "CONFIDENTIAL", "confidence": "certain", "source": "parse" } ],
                  "valid": true, "inputWasValid": true, "errors": [], "warnings": [], "appliedFixes": [], "rendered": "CONFIDENTIAL",
                  "assessment": { "emitted": [ { "conceptId": "CONFIDENTIAL", "categoryId": "level" } ], "omitted": [], "collapsed": [], "conflicts": [], "unrepresentable": [] },
                  "recognition": { "status": "complete", "surface": "sensitivity", "unmatched": [] }, "notices": [] }
                """),
        };
        var (client, _) = await CtxClientFactory.ConnectAsync(routes);
        using (client)
        {
            var recognition = (await client.Scheme!.ValidateAsync("CONF")).Result.Recognition!;
            Assert.Equal(RecognitionStatus.Complete, recognition.Status);
            Assert.Null(recognition.InputFormat);
            Assert.Null(recognition.Role);
            Assert.Equal("sensitivity", recognition.Surface);
        }
    }

    [Fact]
    public async Task Validate_FactsGoOutAsAssertionsWithNoInput()
    {
        var routes = new Dictionary<string, (HttpStatusCode, string)>
        {
            ["/scheme/validate"] = (HttpStatusCode.OK, ValidateJson),
        };
        var (client, handler) = await CtxClientFactory.ConnectAsync(routes);
        using (client)
        {
            var stated = new Assertion("level", "level-high", Confidence.Certain, "stated");
            await client.Scheme!.ValidateAsync(new[] { stated }, surface: "marking", outputFormat: "marking/long",
                parts: new[] { "(H)" }, contextJson: """{"role":"whole"}""");

            // The route takes the marking in exactly one of input and assertions.
            Assert.Equal(
                """{"assertions":[{"conceptId":"level-high","categoryId":"level","confidence":"certain","source":"stated"}],"outputFormat":"marking/long","surface":"marking","context":{"role":"whole"},"parts":["(H)"]}""",
                handler.Requests[^1].Body);
        }
    }

    [Fact]
    public async Task Validate_RejectsNullFactsBeforeSending()
    {
        var (client, handler) = await CtxClientFactory.ConnectAsync();
        using (client)
        {
            var sent = handler.Requests.Count;
            var error = await Assert.ThrowsAsync<ArgumentNullException>(
                () => client.Scheme!.ValidateAsync((IEnumerable<Assertion>)null!));
            Assert.Equal("assertions", error.ParamName);
            Assert.Equal(sent, handler.Requests.Count);
        }
    }

    [Fact]
    public async Task Validate_ForwardsContextVerbatimAndRejectsNonObjectContext()
    {
        var routes = new Dictionary<string, (HttpStatusCode, string)>
        {
            ["/scheme/validate"] = (HttpStatusCode.OK, ValidateJson),
        };
        var (client, handler) = await CtxClientFactory.ConnectAsync(routes);
        using (client)
        {
            await client.Scheme!.ValidateAsync("(H)", contextJson: """{"role":"part"}""");
            Assert.Equal("""{"input":"(H)","context":{"role":"part"}}""", handler.Requests[^1].Body);

            var sent = handler.Requests.Count;
            var error = await Assert.ThrowsAsync<ArgumentException>(
                () => client.Scheme!.ValidateAsync("H", contextJson: "\"part\""));
            Assert.Equal("contextJson", error.ParamName);
            Assert.Equal(sent, handler.Requests.Count);
        }
    }

    [Fact]
    public async Task Validate_SendsPartsAndOmitsThemWhenUnset()
    {
        var routes = new Dictionary<string, (HttpStatusCode, string)>
        {
            ["/scheme/validate"] = (HttpStatusCode.OK, ValidateJson),
        };
        var (client, handler) = await CtxClientFactory.ConnectAsync(routes);
        using (client)
        {
            await client.Scheme!.ValidateAsync("H//A", parts: new[] { "H//B" });
            Assert.Equal("""{"input":"H//A","parts":["H//B"]}""", handler.Requests[^1].Body);

            await client.Scheme!.ValidateAsync("H//A");
            Assert.DoesNotContain("parts", handler.Requests[^1].Body);
        }
    }

    [Fact]
    public async Task Suggest_ParsesSuggestResultEnvelope()
    {
        var routes = new Dictionary<string, (HttpStatusCode, string)>
        {
            ["/scheme/suggest"] = (HttpStatusCode.OK, """
                { "suggestions": [ { "conceptId": "LEVEL_HIGH", "label": "H", "categoryId": "level", "name": "High", "views": { "default": "H" }, "description": "", "group": "Level", "order": 1, "selected": false } ],
                  "warnings": [ { "kind": "rule", "message": "The concept source answered in part." } ] }
                """),
        };
        var (client, handler) = await CtxClientFactory.ConnectAsync(routes);
        using (client)
        {
            var result = await client.Scheme!.SuggestAsync("L");
            Assert.Single(result.Result.Suggestions);
            Assert.Equal("The concept source answered in part.", Assert.Single(result.Result.Warnings).Message);
            Assert.Equal("/v1/ctx/acme-marking/1.0.0/scheme/suggest", handler.Requests[^1].Path);
        }
    }

    [Fact]
    public async Task Suggest_SendsFalseFlagsAndOmitsUnsetOptions()
    {
        var routes = new Dictionary<string, (HttpStatusCode, string)>
        {
            ["/scheme/suggest"] = (HttpStatusCode.OK, """{ "suggestions": [], "warnings": [] }"""),
        };
        var (client, handler) = await CtxClientFactory.ConnectAsync(routes);
        using (client)
        {
            // False is an answer, not an absence: it must reach the server.
            await client.Scheme!.SuggestAsync("H", includeGated: false);
            Assert.Equal("""{"input":"H","includeGated":false}""", handler.Requests[^1].Body);

            await client.Scheme!.SuggestAsync("H");
            Assert.Equal("""{"input":"H"}""", handler.Requests[^1].Body);
        }
    }

    [Fact]
    public async Task SuggestForCategories_RoutesAssertionsAndParsesTheMapByCategory()
    {
        var routes = new Dictionary<string, (HttpStatusCode, string)>
        {
            ["/scheme/suggest-for-categories"] = (HttpStatusCode.OK, """
                { "level": { "suggestions": [ { "conceptId": "HIGH", "label": "H", "categoryId": "level", "name": "High", "views": { "default": "H" }, "description": "", "group": "Level", "order": 0, "selected": true } ], "warnings": [] },
                  "audience": { "suggestions": [], "warnings": [] } }
                """),
        };
        var (client, handler) = await CtxClientFactory.ConnectAsync(routes);
        using (client)
        {
            var assertions = new[]
            {
                new Assertion(CategoryId: "level", ConceptId: "HIGH", Confidence: Confidence.Certain, Source: "parse"),
            };
            var result = await client.Scheme!.SuggestForCategoriesAsync(
                assertions, new[] { "level", "audience" }, outputFormat: "banner", surface: "whole");

            Assert.Equal("HIGH", Assert.Single(result.Result["level"].Suggestions).ConceptId);
            Assert.Empty(result.Result["audience"].Suggestions);
            var (_, path, body) = handler.Requests[^1];
            Assert.Equal("/v1/ctx/acme-marking/1.0.0/scheme/suggest-for-categories", path);
            Assert.Equal(
                """{"assertions":[{"conceptId":"HIGH","categoryId":"level","confidence":"certain","source":"parse"}],"categoryIds":["level","audience"],"outputFormat":"banner","surface":"whole"}""",
                body);
        }
    }

    [Fact]
    public async Task SuggestForCategories_NamingNoCategoryAnswersAnEmptyMap()
    {
        var routes = new Dictionary<string, (HttpStatusCode, string)>
        {
            ["/scheme/suggest-for-categories"] = (HttpStatusCode.OK, "{}"),
        };
        var (client, _) = await CtxClientFactory.ConnectAsync(routes);
        using (client)
        {
            var result = await client.Scheme!.SuggestForCategoriesAsync(Array.Empty<Assertion>(), Array.Empty<string>());
            Assert.Empty(result.Result);
        }
    }

    [Fact]
    public async Task SuggestForCategories_AnswerMissingARequestedCategoryIsRefusedAndNotCached()
    {
        var routes = new Dictionary<string, (HttpStatusCode, string)>
        {
            ["/scheme/suggest-for-categories"] = (HttpStatusCode.OK, """{ "level": { "suggestions": [], "warnings": [] } }"""),
        };
        var (client, handler) = await CtxClientFactory.ConnectAsync(routes);
        using (client)
        {
            var error = await Assert.ThrowsAsync<EzClassifyClientException>(() =>
                client.Scheme!.SuggestForCategoriesAsync(Array.Empty<Assertion>(), new[] { "level", "audience" }));
            Assert.Contains("audience", error.Message);

            // A refused answer is not filed: the next identical call asks again.
            await Assert.ThrowsAsync<EzClassifyClientException>(() =>
                client.Scheme!.SuggestForCategoriesAsync(Array.Empty<Assertion>(), new[] { "level", "audience" }));
            Assert.Equal(2, handler.Requests.FindAll(r => r.Path.Contains("/scheme/suggest-for-categories")).Count);
        }
    }

    [Fact]
    public async Task SuggestForCategories_AnswerWithANullEntryIsMalformed()
    {
        var routes = new Dictionary<string, (HttpStatusCode, string)>
        {
            ["/scheme/suggest-for-categories"] = (HttpStatusCode.OK, """{ "level": null }"""),
        };
        var (client, _) = await CtxClientFactory.ConnectAsync(routes);
        using (client)
        {
            await Assert.ThrowsAsync<JsonException>(() =>
                client.Scheme!.SuggestForCategoriesAsync(Array.Empty<Assertion>(), new[] { "level" }));
        }
    }

    [Fact]
    public async Task Explain_PostsTheProcessBodyAndParsesTheTrace()
    {
        var routes = new Dictionary<string, (HttpStatusCode, string)>
        {
            ["/scheme/explain"] = (HttpStatusCode.OK, """
                { "input": "HIGH", "schemeId": "example-scheme",
                  "entries": [ { "stage": "infer", "action": "add", "detail": "Added STAFF: HIGH requires it.", "conceptId": "audience-staff", "ruleId": "high-needs-staff", "triggerConceptIds": [ "level-high" ] } ],
                  "repairs": [], "validationErrors": [],
                  "inferences": [ { "ruleId": "high-needs-staff", "conceptId": "audience-staff", "explanation": "HIGH requires STAFF." } ],
                  "finalAssertions": [ { "categoryId": "level", "conceptId": "level-high", "confidence": "certain", "source": "parse" },
                                       { "categoryId": "audience", "conceptId": "audience-staff", "confidence": "inferred", "source": "infer" } ],
                  "rendered": "HIGH//STAFF" }
                """),
        };
        var (client, handler) = await CtxClientFactory.ConnectAsync(routes);
        using (client)
        {
            var chosen = new Assertion("level", "level-high", Confidence.Certain, "stated");
            var trace = (await client.Scheme!.ExplainAsync("HIGH", outputFormat: "marking/long", surface: "marking",
                inputFormat: "long", assertions: new[] { chosen }, contextJson: """{"role":"whole"}""", parts: new[] { "(H)" })).Result;

            Assert.Equal("HIGH//STAFF", trace.Rendered);
            Assert.Equal("high-needs-staff", Assert.Single(trace.Inferences).RuleId);
            Assert.Equal("infer", Assert.Single(trace.Entries).Stage);
            Assert.Equal(2, trace.FinalAssertions.Count);
            var (_, path, body) = handler.Requests[^1];
            Assert.Equal("/v1/ctx/acme-marking/1.0.0/scheme/explain", path);
            Assert.Equal(
                """{"input":"HIGH","inputFormat":"long","outputFormat":"marking/long","surface":"marking","assertions":[{"conceptId":"level-high","categoryId":"level","confidence":"certain","source":"stated"}],"context":{"role":"whole"},"parts":["(H)"]}""",
                body);
        }
    }

    [Fact]
    public async Task Aggregate_RoutesAndParses()
    {
        var routes = new Dictionary<string, (HttpStatusCode, string)>
        {
            ["/scheme/aggregate"] = (HttpStatusCode.OK, """
                { "valid": true, "rendered": "H", "assertions": [], "errors": [], "warnings": [], "appliedFixes": [], "proposedFixes": [],
                  "notices": [ { "conceptId": "LEVEL_HIGH", "conceptName": "Level High", "type": "handling", "text": "Handle per policy", "required": false } ] }
                """),
        };
        var (client, handler) = await CtxClientFactory.ConnectAsync(routes);
        using (client)
        {
            var result = await client.Scheme!.AggregateAsync(new[] { "L", "H" });
            Assert.Equal("H", result.Result.Rendered);
            // The aggregate result carries notices on the same terms as process.
            Assert.Single(result.Result.Notices);
            Assert.Equal("LEVEL_HIGH", result.Result.Notices[0].ConceptId);
            Assert.Contains("\"inputs\":[\"L\",\"H\"]", handler.Requests[^1].Body);
            Assert.Equal("/v1/ctx/acme-marking/1.0.0/scheme/aggregate", handler.Requests[^1].Path);
        }
    }

    [Fact]
    public async Task Scan_RoutesAndParses()
    {
        var routes = new Dictionary<string, (HttpStatusCode, string)>
        {
            ["/scheme/scan"] = (HttpStatusCode.OK, """
                { "results":
                    [ { "marking": "H", "start": 5, "end": 6, "type": "marking", "wrapped": false, "role": "part",
                        "result": { "valid": true, "rendered": "H", "inputWasValid": true, "assertions": [], "errors": [], "warnings": [], "appliedFixes": [], "notices": [],
                                    "assessment": { "emitted": [], "omitted": [], "collapsed": [], "conflicts": [], "unrepresentable": [] } } } ],
                  "limits": { "status": "complete", "regionsFound": 1 } }
                """),
        };
        var (client, handler) = await CtxClientFactory.ConnectAsync(routes);
        using (client)
        {
            var matches = await client.Scheme!.ScanAsync("doc H");
            Assert.Single(matches.Result.Results);
            Assert.Equal("H", matches.Result.Results[0].Marking);
            Assert.True(matches.Result.Results[0].Result.Valid);
            // The distinction the object exists to carry: this document was
            // read to the end, so an empty list would have meant "clean".
            Assert.Equal(ScanStatus.Complete, matches.Result.Limits.Status);
            Assert.Equal("/v1/ctx/acme-marking/1.0.0/scheme/scan", handler.Requests[^1].Path);
        }
    }

    [Fact]
    public async Task Render_UnwrapsResultEnvelope_AndSendsAssertions()
    {
        var routes = new Dictionary<string, (HttpStatusCode, string)>
        {
            ["/scheme/render"] = (HttpStatusCode.OK, "{\"result\":\"LEVEL-HIGH\",\"assessment\":{\"emitted\":[{\"conceptId\":\"LEVEL_HIGH\",\"categoryId\":\"level\",\"value\":\"A&B\"}],\"omitted\":[],\"collapsed\":[],\"conflicts\":[],\"unrepresentable\":[{\"conceptId\":\"AUTH-X\",\"reason\":\"value-lossy\",\"detail\":\"value omitted\"},{\"output\":\"authority/block\",\"reason\":\"unresolved-context\"}]}}"),
        };
        var (client, handler) = await CtxClientFactory.ConnectAsync(routes);
        using (client)
        {
            var assertions = new[] { new Assertion(CategoryId: "level", ConceptId: "LEVEL_HIGH", Confidence: Confidence.Certain, Source: "parse") };
            var rendered = await client.Scheme!.RenderAsync(assertions, "banner", RenderPreservation.Full);
            Assert.Equal("LEVEL-HIGH", rendered.Result.Result);
            Assert.Equal("A&B", rendered.Result.Assessment.Emitted[0].Value);
            // A fact the format cannot write, and an output refused whole.
            var fact = Assert.IsType<UnrepresentableFact>(rendered.Result.Assessment.Unrepresentable[0]);
            Assert.Equal("AUTH-X", fact.ConceptId);
            Assert.Equal(UnrepresentableFactReason.ValueLossy, fact.Reason);
            Assert.Equal("value omitted", fact.Detail);
            var output = Assert.IsType<UnrepresentableOutput>(rendered.Result.Assessment.Unrepresentable[1]);
            Assert.Equal("authority/block", output.Output);
            Assert.Equal(UnrepresentableOutputReason.UnresolvedContext, output.Reason);
            Assert.Null(output.Detail);
            var body = handler.Requests[^1].Body;
            Assert.Contains("\"conceptId\":\"LEVEL_HIGH\"", body);
            Assert.Contains("\"outputFormat\":\"banner\"", body);
            Assert.Contains("\"preservation\":\"full\"", body);
            Assert.DoesNotContain("schemeId", body);
        }
    }

    [Fact]
    public async Task RenderFullPreservationRefusal_PreservesTypedError()
    {
        var routes = new Dictionary<string, (HttpStatusCode, string)>
        {
            ["/scheme/render"] = (HttpStatusCode.BadRequest,
                "{\"error\":\"render would lose facts\",\"code\":\"RENDER_NOT_PRESERVING\",\"detail\":\"omitted authority AUTH-X=A&B\"}"),
        };
        var (client, _) = await CtxClientFactory.ConnectAsync(routes);
        using (client)
        {
            var assertion = new Assertion("authority", "AUTH-X", Confidence.Certain, "parse", Value: "A&B");
            var ex = await Assert.ThrowsAsync<EzClassifyClientException>(() =>
                client.Scheme!.RenderAsync(new[] { assertion }, preservation: RenderPreservation.Full));
            Assert.Equal(400, ex.StatusCode);
            Assert.Equal("RENDER_NOT_PRESERVING", ex.Code);
            Assert.Equal("omitted authority AUTH-X=A&B", ex.Detail);
        }
    }

    [Fact]
    public async Task Normalize_UnwrapsResultEnvelope()
    {
        var routes = new Dictionary<string, (HttpStatusCode, string)>
        {
            ["/scheme/normalize"] = (HttpStatusCode.OK, """{ "result": "LEVEL-HIGH" }"""),
        };
        var (client, handler) = await CtxClientFactory.ConnectAsync(routes);
        using (client)
        {
            var normalized = await client.Scheme!.NormalizeAsync("high");
            Assert.Equal("LEVEL-HIGH", normalized.Result.Result);
            Assert.Equal("/v1/ctx/acme-marking/1.0.0/scheme/normalize", handler.Requests[^1].Path);
        }
    }

    [Fact]
    public async Task CheckCoverage_ParsesReport()
    {
        var routes = new Dictionary<string, (HttpStatusCode, string)>
        {
            ["/scheme/check-coverage"] = (HttpStatusCode.OK, """
                { "covered": false, "missing": [ { "conceptId": "RD", "categoryId": "ctrl", "confidence": "certain", "source": "parse" } ],
                  "missingLabels": ["RD"], "excess": [], "excessLabels": [], "rendered": "RS/RD" }
                """),
        };
        var (client, handler) = await CtxClientFactory.ConnectAsync(routes);
        using (client)
        {
            var report = await client.Scheme!.CheckCoverageAsync(new[] { "RS" }, "RS/RD");
            Assert.False(report.Result.Covered);
            Assert.Single(report.Result.Missing);
            Assert.Equal("RD", report.Result.MissingLabels[0]);
            Assert.Empty(report.Result.Excess);
            Assert.Empty(report.Result.ExcessLabels);
            Assert.Null(report.Result.Refusals);
            Assert.Equal("/v1/ctx/acme-marking/1.0.0/scheme/check-coverage", handler.Requests[^1].Path);
        }
    }

    [Fact]
    public async Task DescribeConcept_UsesGetWithQueryParams()
    {
        var routes = new Dictionary<string, (HttpStatusCode, string)>
        {
            ["/scheme/describe-concept"] = (HttpStatusCode.OK, """
                { "conceptId": "LEVEL_HIGH", "categoryId": "level", "name": "High", "lines": ["High — top level"], "definition": "the high level" }
                """),
        };
        var (client, handler) = await CtxClientFactory.ConnectAsync(routes);
        using (client)
        {
            var desc = await client.Scheme!.DescribeConceptAsync("LEVEL_HIGH", "level");
            Assert.Equal("High", desc.Result.Name);
            Assert.Single(desc.Result.Lines);
            var (method, path, _) = handler.Requests[^1];
            Assert.Equal("GET", method);
            Assert.Contains("/v1/ctx/acme-marking/1.0.0/scheme/describe-concept", path);
            Assert.Contains("conceptId=LEVEL_HIGH", path);
            Assert.Contains("categoryId=level", path);
        }
    }

    [Fact]
    public async Task Diff_ParsesResult()
    {
        var routes = new Dictionary<string, (HttpStatusCode, string)>
        {
            ["/scheme/diff"] = (HttpStatusCode.OK, """
                { "added": [ { "conceptId": "A", "categoryId": "c", "confidence": "certain", "source": "parse" } ], "removed": [], "common": [] }
                """),
        };
        var (client, handler) = await CtxClientFactory.ConnectAsync(routes);
        using (client)
        {
            var diff = await client.Scheme!.DiffAsync("A", "B");
            Assert.Single(diff.Result.Added);
            Assert.Equal("/v1/ctx/acme-marking/1.0.0/scheme/diff", handler.Requests[^1].Path);
        }
    }

    [Fact]
    public async Task FilterConcepts_ParsesCountsAndIds()
    {
        var routes = new Dictionary<string, (HttpStatusCode, string)>
        {
            ["/scheme/filter-concepts"] = (HttpStatusCode.OK, """
                { "visibleIds": ["a", "b"], "categoryCounts": { "level": { "visible": 2, "total": 5 } }, "total": { "visible": 2, "total": 9 } }
                """),
        };
        var (client, handler) = await CtxClientFactory.ConnectAsync(routes);
        using (client)
        {
            var result = await client.Scheme!.FilterConceptsAsync("hi");
            Assert.Equal(2, result.Result.VisibleIds.Count);
            Assert.Equal(2, result.Result.CategoryCounts["level"].Visible);
            Assert.Equal(5, result.Result.CategoryCounts["level"].Total);
            Assert.Equal(9, result.Result.Total.Total);
            Assert.Equal("/v1/ctx/acme-marking/1.0.0/scheme/filter-concepts", handler.Requests[^1].Path);
        }
    }

    // The fixture carries a property the contract does not define. Parsing has to
    // ignore it, so a server that adds a field ahead of an SDK release does not
    // break an already-deployed client.
    [Fact]
    public async Task Notices_ParsesList()
    {
        var routes = new Dictionary<string, (HttpStatusCode, string)>
        {
            ["/scheme/notices"] = (HttpStatusCode.OK, """
                [ { "conceptId": "RD", "conceptName": "Restricted Section", "type": "handling", "text": "Sharing controlled", "required": true, "futureServerField": "value" } ]
                """),
        };
        var (client, handler) = await CtxClientFactory.ConnectAsync(routes);
        using (client)
        {
            var assertions = new[] { new Assertion(CategoryId: "ctrl", ConceptId: "RD", Confidence: Confidence.Certain, Source: "parse") };
            var notices = await client.Scheme!.NoticesAsync(assertions);
            Assert.Single(notices.Result);
            Assert.Equal("handling", notices.Result[0].Type);
            Assert.True(notices.Result[0].Required);
            Assert.Equal("/v1/ctx/acme-marking/1.0.0/scheme/notices", handler.Requests[^1].Path);
        }
    }

    [Fact]
    public async Task SchemeView_ParsesStructuralTree()
    {
        var routes = new Dictionary<string, (HttpStatusCode, string)>
        {
            ["/scheme/view"] = (HttpStatusCode.OK, """
                { "id": "s", "name": "Scheme", "description": "d",
                  "categories": [ { "id": "level", "label": "Level", "items": [ { "id": "H", "name": "High", "views": { "default": "H" }, "aliases": [], "categories": ["level"], "parents": [], "position": 1 } ] } ],
                  "uncategorized": [] }
                """),
        };
        var (client, handler) = await CtxClientFactory.ConnectAsync(routes);
        using (client)
        {
            var view = await client.Scheme!.ViewAsync();
            Assert.Equal("Scheme", view.Result.Name);
            Assert.Single(view.Result.Categories);
            Assert.Equal("Level", view.Result.Categories[0].Label);
            Assert.Single(view.Result.Categories[0].Items);
            Assert.Equal("/v1/ctx/acme-marking/1.0.0/scheme/view", handler.Requests[^1].Path);
        }
    }

    // --- scheme sub-resources ----------------------------------------------

    private const string SummaryJson = """
        { "id": "acme", "version": "1.2.0", "name": "Acme Marking", "description": "d",
          "roleAware": true,
          "embeddedMarking": [ { "pattern": "\\((.+)\\)", "template": "({marking}) ", "autoInsert": "first-line" } ],
          "defaultRenderFormat": "banner",
          "referenceSurfaceLabel": { "singular": "Handbook", "plural": "Handbooks" },
          "definition": "def", "guidance": "guide", "colorFrom": "level",
          "colorRules": [ { "color": { "background": "#fff", "text": "#000" }, "when": { "all": [] } } ],
          "vocabulary": { "rule": { "singular": "rule", "plural": "rules" } },
          "contextFormats": { "whole": ["banner"], "part": ["portion"] },
          "defaultScope": "default", "scopeLabel": "Scope" }
        """;

    [Fact]
    public async Task SchemeSummary_GetsBareSchemeRoute_AndParsesFlatBody()
    {
        var routes = new Dictionary<string, (HttpStatusCode, string)>
        {
            ["/1.0.0/scheme"] = (HttpStatusCode.OK, SummaryJson),
        };
        var (client, handler) = await CtxClientFactory.ConnectAsync(routes);
        using (client)
        {
            var summary = await client.Scheme!.SummaryAsync();

            Assert.Equal("acme", summary.Id);
            Assert.Equal("1.2.0", summary.Version);

            Assert.True(summary.RoleAware);
            // embeddedMarking is a list of styles, carried opaque.
            Assert.Equal(1, summary.EmbeddedMarking!.Value.GetArrayLength());
            Assert.Equal(
                "first-line",
                summary.EmbeddedMarking!.Value[0].GetProperty("autoInsert").GetString());
            Assert.Equal("banner", summary.DefaultRenderFormat);
            Assert.Equal("Handbooks", summary.ReferenceSurfaceLabel.Plural);
            Assert.Equal("level", summary.ColorFrom);
            Assert.Equal("#fff", Assert.Single(summary.ColorRules!).Color.Background);
            Assert.NotNull(summary.ColorRules![0].When);
            Assert.Equal("rules", summary.Vocabulary!["rule"].Plural);
            Assert.Equal("banner", Assert.Single(summary.ContextFormats!.Whole));
            Assert.Equal("portion", Assert.Single(summary.ContextFormats!.Part));
            Assert.Equal("default", summary.DefaultScope);
            Assert.Equal("Scope", summary.ScopeLabel);

            var (method, path, body) = handler.Requests[^1];
            Assert.Equal("GET", method);
            Assert.Equal("/v1/ctx/acme-marking/1.0.0/scheme", path);
            Assert.Null(body);
        }
    }

    [Fact]
    public async Task SchemeSummary_OmittedOptionalFields_StayNull()
    {
        var routes = new Dictionary<string, (HttpStatusCode, string)>
        {
            // Only the fields the contract requires. roleAware and
            // embeddedMarking are both optional, so a conformant server may
            // legitimately send neither.
            ["/1.0.0/scheme"] = (HttpStatusCode.OK, """
                { "id": "acme", "version": "1.0.0", "name": "Acme", "description": "",
                  "defaultRenderFormat": "banner",
                  "referenceSurfaceLabel": { "singular": "Handbook", "plural": "Handbooks" } }
                """),
        };
        var (client, _) = await CtxClientFactory.ConnectAsync(routes);
        using (client)
        {
            var summary = await client.Scheme!.SummaryAsync();

            Assert.Null(summary.RoleAware);
            Assert.Null(summary.EmbeddedMarking);
            Assert.Null(summary.Definition);
            Assert.Null(summary.ColorRules);
            Assert.Null(summary.Vocabulary);
            Assert.Null(summary.ContextFormats);
            Assert.Null(summary.DefaultScope);
            // One default for a scheme whose formats are its outputs.
            Assert.Null(summary.DefaultPartRenderFormat);
            Assert.Null(summary.DefaultWholeRenderFormat);
        }
    }

    [Fact]
    public async Task SchemeSummary_CarriesRoleDefaultsForASchemeWithSurfaces()
    {
        var routes = new Dictionary<string, (HttpStatusCode, string)>
        {
            ["/1.0.0/scheme"] = (HttpStatusCode.OK, """
                { "id": "example-scheme", "version": "1.0.0", "name": "Example", "description": "",
                  "defaultRenderFormat": "sensitivity/full",
                  "defaultPartRenderFormat": "sensitivity/short",
                  "defaultWholeRenderFormat": "sensitivity/full",
                  "contextFormats": { "whole": ["full_line", "full_line_2019"], "part": ["short_line", "short_shared_line"] },
                  "referenceSurfaceLabel": { "singular": "Handbook", "plural": "Handbooks" } }
                """),
        };
        var (client, _) = await CtxClientFactory.ConnectAsync(routes);
        using (client)
        {
            var summary = await client.Scheme!.SummaryAsync();

            Assert.Equal("sensitivity/full", summary.DefaultRenderFormat);
            Assert.Equal("sensitivity/short", summary.DefaultPartRenderFormat);
            Assert.Equal("sensitivity/full", summary.DefaultWholeRenderFormat);
            Assert.Equal(new[] { "short_line", "short_shared_line" }, summary.ContextFormats!.Part);
        }
    }

    [Fact]
    public async Task SchemeConcepts_ParsesConceptViews()
    {
        var routes = new Dictionary<string, (HttpStatusCode, string)>
        {
            ["/scheme/concepts"] = (HttpStatusCode.OK, """
                [ { "id": "LEVEL_HIGH", "name": "High", "views": { "default": "HIGH", "short": "H" },
                    "categories": ["level"], "parents": [], "position": 2, "rank": 30,
                    "aliases": ["HI"], "members": [], "scopes": ["default"],
                    "deprecated": false,
                    "textInput": { "placeholder": "code", "required": true },
                    "color": { "background": "#ff0000", "text": "#ffffff" },
                    "tier": "primary",
                    "definition": "The high level", "guidance": "Use sparingly",
                    "notices": [ { "type": "warning", "required": true, "text": "Handle with care",
                                   "condition": "LEVEL_LOW", "views": { "short": "HC" } },
                                 { "type": "handling", "required": false, "text": "Optional note" } ],
                    "rulesReferencing": ["level-rule"],
                    "citation": "Source register, row 42" },
                  { "id": "LEVEL_LOW", "name": "Low", "views": { "default": "LOW" },
                    "categories": ["level"], "parents": ["LEVEL_HIGH"], "position": 1, "rank": 10,
                    "aliases": [], "members": [], "scopes": [],
                    "deprecated": true, "deprecatedMessage": "use High",
                    "deprecatedReplacement": "LEVEL_HIGH" } ]
                """),
        };
        var (client, handler) = await CtxClientFactory.ConnectAsync(routes);
        using (client)
        {
            var concepts = await client.Scheme!.ConceptsAsync();

            Assert.Equal(2, concepts.Count);
            Assert.Equal("LEVEL_HIGH", concepts[0].Id);
            Assert.Equal(2, concepts[0].Position);
            Assert.Equal(30, concepts[0].Rank);
            Assert.Equal("HI", Assert.Single(concepts[0].Aliases));
            Assert.Equal("#ff0000", concepts[0].Color!.Background);
            Assert.Equal("primary", concepts[0].Tier);
            // Views is a typed map from view name to the concept's spelling.
            Assert.Equal("H", concepts[0].Views["short"]);
            Assert.Equal("HIGH", concepts[0].Views["default"]);
            Assert.Equal("The high level", concepts[0].Definition);
            Assert.Equal("Use sparingly", concepts[0].Guidance);
            Assert.Equal("level-rule", Assert.Single(concepts[0].RulesReferencing!));
            Assert.Equal("Source register, row 42", concepts[0].Citation);
            // textInput stays opaque and outlives the JsonDocument it was read from.
            Assert.Equal("code", concepts[0].TextInput!.Value.GetProperty("placeholder").GetString());

            Assert.Equal(2, concepts[0].Notices!.Count);
            var notice = concepts[0].Notices![0];
            Assert.Equal(("warning", true, "Handle with care", "LEVEL_LOW"),
                (notice.Type, notice.Required, notice.Text, notice.Condition));
            Assert.Equal("HC", notice.Views!["short"]);
            Assert.False(concepts[0].Notices![1].Required);
            Assert.Null(concepts[0].Notices![1].Condition);
            Assert.Null(concepts[0].Notices![1].Views);

            Assert.True(concepts[1].Deprecated);
            Assert.Equal("use High", concepts[1].DeprecatedMessage);
            Assert.Equal("LEVEL_HIGH", concepts[1].DeprecatedReplacement);
            Assert.Null(concepts[1].TextInput);
            Assert.Null(concepts[1].Notices);
            Assert.Null(concepts[1].Definition);
            Assert.Null(concepts[1].Citation);
            Assert.Null(concepts[1].Color);
            Assert.Null(concepts[1].Tier);

            var (method, path, _) = handler.Requests[^1];
            Assert.Equal("GET", method);
            Assert.Equal("/v1/ctx/acme-marking/1.0.0/scheme/concepts", path);
        }
    }

    [Fact]
    public async Task SchemeConcepts_OmittedOptionalFields_StayNull()
    {
        var routes = new Dictionary<string, (HttpStatusCode, string)>
        {
            // Every key the contract requires, and nothing else. rank,
            // members, scopes, deprecated, textInput, the documentation text and
            // the notices are all optional, so a scheme that declares none of
            // them produces exactly this body.
            ["/scheme/concepts"] = (HttpStatusCode.OK, """
                [ { "id": "LEVEL_LOW", "name": "Low", "views": { "default": "LOW" },
                    "categories": ["level"], "parents": [], "position": 1, "aliases": [] } ]
                """),
        };
        var (client, _) = await CtxClientFactory.ConnectAsync(routes);
        using (client)
        {
            var concept = Assert.Single(await client.Scheme!.ConceptsAsync());

            Assert.Equal("LEVEL_LOW", concept.Id);
            Assert.Equal(1, concept.Position);
            Assert.Empty(concept.Aliases);
            Assert.Null(concept.Rank);
            Assert.Null(concept.Members);
            Assert.Null(concept.Scopes);
            Assert.Null(concept.Deprecated);
            Assert.Null(concept.TextInput);
            Assert.Null(concept.Definition);
            Assert.Null(concept.Guidance);
            Assert.Null(concept.Notices);
            Assert.Null(concept.RulesReferencing);
            Assert.Null(concept.Citation);
        }
    }

    [Fact]
    public async Task SchemeConcepts_RefuseAConceptWithoutViews()
    {
        var routes = new Dictionary<string, (HttpStatusCode, string)>
        {
            ["/scheme/concepts"] = (HttpStatusCode.OK, """
                [ { "id": "LEVEL_LOW", "name": "Low",
                    "categories": ["level"], "parents": [], "position": 1, "aliases": [] } ]
                """),
        };
        var (client, _) = await CtxClientFactory.ConnectAsync(routes);
        using (client)
        {
            var ex = await Assert.ThrowsAsync<JsonException>(() => client.Scheme!.ConceptsAsync());
            Assert.Equal("Response is missing required member $[0].views.", ex.Message);
            Assert.Equal("$[0].views", ex.Path);
        }
    }

    [Theory]
    [InlineData("\"rank\": \"high\"", "rank must be a number")]
    [InlineData("\"deprecated\": \"yes\"", "deprecated must be a boolean")]
    [InlineData("\"notices\": { \"type\": \"warning\" }", "notices must be an array")]
    [InlineData("\"notices\": [ { \"type\": \"warning\", \"required\": \"yes\", \"text\": \"t\" } ]", "required must be a boolean")]
    [InlineData("\"scopes\": \"default\"", "scopes must be an array")]
    public async Task SchemeConcepts_WrongTypeOnOptionalField_Throws(string field, string message)
    {
        var routes = new Dictionary<string, (HttpStatusCode, string)>
        {
            ["/scheme/concepts"] = (HttpStatusCode.OK, $$"""
                [ { "id": "LEVEL_LOW", "name": "Low", "views": { "default": "LOW" },
                    "categories": ["level"], "parents": [], "position": 1, "aliases": [],
                    {{field}} } ]
                """),
        };
        var (client, _) = await CtxClientFactory.ConnectAsync(routes);
        using (client)
        {
            var ex = await Assert.ThrowsAsync<JsonException>(() => client.Scheme!.ConceptsAsync());
            Assert.Equal(message, ex.Message);
        }
    }

    [Fact]
    public async Task SchemeCategories_ParsesCategoryViews()
    {
        var routes = new Dictionary<string, (HttpStatusCode, string)>
        {
            ["/scheme/categories"] = (HttpStatusCode.OK, """
                [ { "id": "level", "label": "Level", "required": true, "single": true,
                    "position": 2.5, "scopes": ["default"], "hierarchyRoot": true,
                    "rollup": "max", "defaultConcept": "LEVEL_LOW",
                    "sort": ["rank", "name"],
                    "positionOverrides": { "LEVEL_LOW": -0.5, "LEVEL_HIGH": 3 } } ]
                """),
        };
        var (client, handler) = await CtxClientFactory.ConnectAsync(routes);
        using (client)
        {
            var categories = await client.Scheme!.CategoriesAsync();

            var category = Assert.Single(categories);
            Assert.Equal("level", category.Id);
            Assert.True(category.Required);
            Assert.True(category.Single);
            Assert.True(category.HierarchyRoot);
            Assert.Equal("default", Assert.Single(category.Scopes!));
            Assert.Equal("LEVEL_LOW", category.DefaultConcept!.Value.GetString());
            // rollup is the merge operator's token as the server sent it.
            Assert.Equal("max", category.Rollup);
            Assert.Equal(2.5, category.Position);
            Assert.Equal(2, category.Sort!.Value.GetArrayLength());
            Assert.Equal(-0.5, category.PositionOverrides!["LEVEL_LOW"]);
            Assert.Equal(3, category.PositionOverrides["LEVEL_HIGH"]);
            Assert.Equal("/v1/ctx/acme-marking/1.0.0/scheme/categories", handler.Requests[^1].Path);
        }
    }

    [Theory]
    [InlineData("\"ORIGIN\"")]
    [InlineData("{\"concept\":\"ORIGIN\",\"when\":{\"all\":[{\"category\":\"level\",\"is\":\"has_content\"}]}}")]
    public async Task SchemeCategories_PreservesDefaultConceptExpression(string expression)
    {
        var routes = new Dictionary<string, (HttpStatusCode, string)>
        {
            ["/scheme/categories"] = (HttpStatusCode.OK, $$"""
                [ { "id": "origin", "label": "Origin", "required": false, "single": true,
                    "rollup": "first", "defaultConcept": {{expression}} } ]
                """),
        };
        var (client, _) = await CtxClientFactory.ConnectAsync(routes);
        using (client)
        {
            var category = Assert.Single(await client.Scheme!.CategoriesAsync());

            Assert.Equal(expression, JsonSerializer.Serialize(category.DefaultConcept));
        }
    }

    [Fact]
    public async Task SchemeCategories_OmittedOptionalFields_StayNull()
    {
        var routes = new Dictionary<string, (HttpStatusCode, string)>
        {
            // Only id, label and rollup are required on the wire.
            ["/scheme/categories"] = (HttpStatusCode.OK, """
                [ { "id": "level", "label": "Level", "rollup": "max" } ]
                """),
        };
        var (client, _) = await CtxClientFactory.ConnectAsync(routes);
        using (client)
        {
            var category = Assert.Single(await client.Scheme!.CategoriesAsync());

            Assert.Equal("level", category.Id);
            Assert.Null(category.Required);
            Assert.Null(category.Single);
            Assert.Null(category.Scopes);
            Assert.Null(category.HierarchyRoot);
            Assert.Null(category.Position);
            Assert.Null(category.DefaultConcept);
            Assert.Null(category.Sort);
            Assert.Null(category.PositionOverrides);
        }
    }

    [Fact]
    public async Task SchemeCategories_RefuseACategoryWithoutARollup()
    {
        var routes = new Dictionary<string, (HttpStatusCode, string)>
        {
            ["/scheme/categories"] = (HttpStatusCode.OK, """[ { "id": "level", "label": "Level" } ]"""),
        };
        var (client, _) = await CtxClientFactory.ConnectAsync(routes);
        using (client)
        {
            var ex = await Assert.ThrowsAsync<JsonException>(() => client.Scheme!.CategoriesAsync());
            Assert.Equal("Response is missing required member $[0].rollup.", ex.Message);
            Assert.Equal("$[0].rollup", ex.Path);
        }
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("{\"level\": \"1\"}")]
    [InlineData("{\"level\": null}")]
    public async Task SchemeCategories_RejectsMalformedPositionOverrides(string value)
    {
        var routes = new Dictionary<string, (HttpStatusCode, string)>
        {
            ["/scheme/categories"] = (HttpStatusCode.OK, $$"""
                [ { "id": "level", "label": "Level", "required": true, "single": true,
                    "rollup": "max", "positionOverrides": {{value}} } ]
                """),
        };
        var (client, _) = await CtxClientFactory.ConnectAsync(routes);
        using (client)
        {
            await Assert.ThrowsAsync<JsonException>(() => client.Scheme!.CategoriesAsync());
        }
    }

    [Fact]
    public async Task SchemeFormats_ParsesCurrentFormatFields()
    {
        var routes = new Dictionary<string, (HttpStatusCode, string)>
        {
            ["/scheme/formats"] = (HttpStatusCode.OK, """
                [ { "id": "banner", "surface": "whole", "mode": "inline", "categoryOrder": ["level", "ctrl"],
                    "separator": "//", "categorySeparators": { "ctrl": "/" }, "view": "short" },
                  { "id": "portion", "surface": "part", "mode": "block", "categoryOrder": ["level"] } ]
                """),
        };
        var (client, handler) = await CtxClientFactory.ConnectAsync(routes);
        using (client)
        {
            var formats = await client.Scheme!.FormatsAsync();

            Assert.Equal(2, formats.Count);
            Assert.Equal("banner", formats[0].Id);
            Assert.Equal(new[] { "level", "ctrl" }, formats[0].CategoryOrder);
            Assert.Equal("//", formats[0].Separator);
            Assert.Equal("/", formats[0].CategorySeparators!["ctrl"]);
            Assert.Equal(RenderFormatMode.Block, formats[1].Mode);
            // Optional format fields stay null rather than defaulting or throwing.
            Assert.Null(formats[1].Separator);
            Assert.Null(formats[1].CategorySeparators);
            Assert.Null(formats[1].View);
            // A scheme whose formats are its outputs sends no view metadata.
            Assert.Null(formats[0].OutputView);
            Assert.Null(formats[0].DefaultFor);
            Assert.Null(formats[0].Role);
            Assert.Equal("/v1/ctx/acme-marking/1.0.0/scheme/formats", handler.Requests[^1].Path);
        }
    }

    [Fact]
    public async Task SchemeFormats_CarriesViewDefaultsAndRolesForASchemeWithSurfaces()
    {
        var routes = new Dictionary<string, (HttpStatusCode, string)>
        {
            ["/scheme/formats"] = (HttpStatusCode.OK, """
                [ { "id": "sensitivity/short", "surface": "sensitivity", "outputView": "short", "defaultFor": ["part"], "role": "part",
                    "mode": "inline", "separator": " // ", "categoryOrder": ["level", "sharing"] },
                  { "id": "sensitivity/full", "surface": "sensitivity", "outputView": "full", "defaultFor": ["whole"], "role": "whole",
                    "mode": "inline", "separator": " // ", "categoryOrder": ["level", "sharing"] },
                  { "id": "sensitivity/xml", "surface": "sensitivity", "outputView": "xml", "defaultFor": [],
                    "mode": "inline", "categoryOrder": ["level", "sharing"] },
                  { "id": "retention/block", "surface": "retention", "outputView": "block", "defaultFor": ["whole", "part"],
                    "mode": "block", "categoryOrder": ["retention"] } ]
                """),
        };
        var (client, _) = await CtxClientFactory.ConnectAsync(routes);
        using (client)
        {
            var formats = await client.Scheme!.FormatsAsync();

            Assert.Equal("short", formats[0].OutputView);
            Assert.Equal(new[] { StructuralRole.Part }, formats[0].DefaultFor);
            Assert.Equal(StructuralRole.Part, formats[0].Role);
            Assert.Equal(new[] { StructuralRole.Whole }, formats[1].DefaultFor);
            Assert.Equal(StructuralRole.Whole, formats[1].Role);
            // A default for no role is an answer, not an absence.
            Assert.NotNull(formats[2].DefaultFor);
            Assert.Empty(formats[2].DefaultFor!);
            Assert.Null(formats[2].Role);
            Assert.Equal(new[] { StructuralRole.Whole, StructuralRole.Part }, formats[3].DefaultFor);
            Assert.Null(formats[3].Role);
        }
    }

    [Fact]
    public async Task SchemeGrammar_ParsesParseRules()
    {
        var routes = new Dictionary<string, (HttpStatusCode, string)>
        {
            ["/scheme/grammar"] = (HttpStatusCode.OK,
                """{ "caseSensitive": false, "stripWhitespace": true }"""),
        };
        var (client, handler) = await CtxClientFactory.ConnectAsync(routes);
        using (client)
        {
            var grammar = await client.Scheme!.GrammarAsync();

            Assert.False(grammar.CaseSensitive);
            Assert.True(grammar.StripWhitespace);
            Assert.Equal("/v1/ctx/acme-marking/1.0.0/scheme/grammar", handler.Requests[^1].Path);
        }
    }

    [Fact]
    public async Task SchemeScopes_CarriesOrderedArray()
    {
        var routes = new Dictionary<string, (HttpStatusCode, string)>
        {
            // An array, not a map: declaration order is the order a selector
            // offers the scopes in, and a JSON object does not carry order.
            ["/scheme/scopes"] = (HttpStatusCode.OK, """
                [ { "id": "default", "label": "Whole" },
                  { "id": "portion", "label": "Part" } ]
                """),
        };
        var (client, handler) = await CtxClientFactory.ConnectAsync(routes);
        using (client)
        {
            var scopes = await client.Scheme!.ScopesAsync();

            Assert.Equal(new[] { ("default", "Whole"), ("portion", "Part") }, scopes.Select(scope => (scope.Id, scope.Label)));
            Assert.Equal("/v1/ctx/acme-marking/1.0.0/scheme/scopes", handler.Requests[^1].Path);
        }
    }

    [Fact]
    public async Task SchemeScopes_ObjectBody_Throws()
    {
        var routes = new Dictionary<string, (HttpStatusCode, string)>
        {
            ["/scheme/scopes"] = (HttpStatusCode.OK, """{ "default": { "label": "Whole" } }"""),
        };
        var (client, _) = await CtxClientFactory.ConnectAsync(routes);
        using (client)
        {
            var ex = await Assert.ThrowsAsync<JsonException>(() => client.Scheme!.ScopesAsync());
            Assert.Equal("Response member $ must be an array.", ex.Message);
        }
    }

    [Fact]
    public async Task SchemeScopes_ASchemeWithNoScopesAnswersAnEmptyList()
    {
        var routes = new Dictionary<string, (HttpStatusCode, string)>
        {
            ["/scheme/scopes"] = (HttpStatusCode.OK, "[]"),
        };
        var (client, _) = await CtxClientFactory.ConnectAsync(routes);
        using (client)
        {
            Assert.Empty(await client.Scheme!.ScopesAsync());
        }
    }

    [Fact]
    public async Task SchemeScopes_RefuseANullBody()
    {
        var routes = new Dictionary<string, (HttpStatusCode, string)>
        {
            ["/scheme/scopes"] = (HttpStatusCode.OK, "null"),
        };
        var (client, _) = await CtxClientFactory.ConnectAsync(routes);
        using (client)
        {
            await Assert.ThrowsAsync<JsonException>(() => client.Scheme!.ScopesAsync());
        }
    }

    [Fact]
    public async Task SchemeLabelPresets_ParsesNameAndScopeOnly()
    {
        var routes = new Dictionary<string, (HttpStatusCode, string)>
        {
            ["/scheme/label-presets"] = (HttpStatusCode.OK, """
                [ { "id": "default-label", "label": "Default", "description": "The usual one", "scope": "default" },
                  { "id": "bare", "label": "Bare" } ]
                """),
        };
        var (client, handler) = await CtxClientFactory.ConnectAsync(routes);
        using (client)
        {
            var presets = await client.Scheme!.LabelPresetsAsync();

            Assert.Equal(2, presets.Count);
            Assert.Equal("default-label", presets[0].Id);
            Assert.Equal("The usual one", presets[0].Description);
            Assert.Equal("default", presets[0].Scope);
            Assert.Null(presets[1].Description);
            Assert.Null(presets[1].Scope);
            Assert.Equal("/v1/ctx/acme-marking/1.0.0/scheme/label-presets", handler.Requests[^1].Path);
        }
    }

    [Fact]
    public async Task SchemeLabelPresets_EmptyList_Parses()
    {
        var routes = new Dictionary<string, (HttpStatusCode, string)>
        {
            ["/scheme/label-presets"] = (HttpStatusCode.OK, "[]"),
        };
        var (client, _) = await CtxClientFactory.ConnectAsync(routes);
        using (client)
        {
            Assert.Empty(await client.Scheme!.LabelPresetsAsync());
        }
    }

    [Fact]
    public async Task SchemeApplyPreset_PostsPresetIdAndParsesContextEnvelope()
    {
        var routes = new Dictionary<string, (HttpStatusCode, string)>
        {
            ["/scheme/apply-preset"] = (HttpStatusCode.OK, ValidateJson),
        };
        var (client, handler) = await CtxClientFactory.ConnectAsync(routes);
        using (client)
        {
            var result = await client.Scheme!.ApplyPresetAsync("default-label", outputFormat: "portion");

            // apply-preset runs the engine, so it answers in the Context
            // envelope the sibling resources do not use, with what validate
            // answers for the marking the preset stands for.
            Assert.Equal("acme-marking", result.Resolution.ContextId);
            Assert.True(result.Result.Valid);
            Assert.Equal("HIGH//STAFF", result.Result.Rendered);

            var (method, path, body) = handler.Requests[^1];
            Assert.Equal("POST", method);
            Assert.Equal("/v1/ctx/acme-marking/1.0.0/scheme/apply-preset", path);
            Assert.Contains("\"presetId\":\"default-label\"", body);
            Assert.Contains("\"outputFormat\":\"portion\"", body);
        }
    }

    [Fact]
    public async Task SchemeApplyPreset_OmitsOutputFormatWhenNotSupplied()
    {
        var routes = new Dictionary<string, (HttpStatusCode, string)>
        {
            ["/scheme/apply-preset"] = (HttpStatusCode.OK, ValidateJson),
        };
        var (client, handler) = await CtxClientFactory.ConnectAsync(routes);
        using (client)
        {
            await client.Scheme!.ApplyPresetAsync("default-label");
            Assert.DoesNotContain("ormat", handler.Requests[^1].Body);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public async Task SchemeApplyPreset_RejectsEmptyPresetId(string? presetId)
    {
        var (client, handler) = await CtxClientFactory.ConnectAsync();
        using (client)
        {
            await Assert.ThrowsAsync<ArgumentException>(() => client.Scheme!.ApplyPresetAsync(presetId!));
            // Rejected client-side: only the connect probe was ever sent.
            Assert.Single(handler.Requests);
        }
    }

    // These sub-resources take the same optional scheme selector, so the query
    // plumbing is proved once per route rather than duplicated per assertion.
    // The label presets and apply-preset take none: the server refuses a query
    // parameter an operation does not declare.
    public static TheoryData<string, string> SchemeSelectorRoutes() => new()
    {
        { "/1.0.0/scheme", "/v1/ctx/acme-marking/1.0.0/scheme?scheme=source" },
        { "/scheme/concepts", "/v1/ctx/acme-marking/1.0.0/scheme/concepts?scheme=source" },
        { "/scheme/categories", "/v1/ctx/acme-marking/1.0.0/scheme/categories?scheme=source" },
        { "/scheme/formats", "/v1/ctx/acme-marking/1.0.0/scheme/formats?scheme=source" },
        { "/scheme/grammar", "/v1/ctx/acme-marking/1.0.0/scheme/grammar?scheme=source" },
        { "/scheme/scopes", "/v1/ctx/acme-marking/1.0.0/scheme/scopes?scheme=source" },
    };

    [Theory]
    [MemberData(nameof(SchemeSelectorRoutes))]
    public async Task SchemeSubResources_SendSchemeSelectorAsQuery(string route, string expectedPath)
    {
        var (client, handler) = await CtxClientFactory.ConnectAsync(RoutesFor(route));
        using (client)
        {
            await InvokeSubResource(client, route, scheme: "source");
            Assert.Equal(expectedPath, handler.Requests[^1].Path);
        }
    }

    [Theory]
    [MemberData(nameof(SchemeSelectorRoutes))]
    public async Task SchemeSubResources_OmitSchemeSelectorWhenNotSupplied(string route, string expectedPath)
    {
        var (client, handler) = await CtxClientFactory.ConnectAsync(RoutesFor(route));
        using (client)
        {
            await InvokeSubResource(client, route, scheme: null);
            Assert.Equal(expectedPath.Replace("?scheme=source", ""), handler.Requests[^1].Path);
            Assert.DoesNotContain("scheme=", handler.Requests[^1].Path);
        }
    }

    [Fact]
    public async Task LabelPresetsAndApplyPreset_SendNoQuery()
    {
        var routes = new Dictionary<string, (HttpStatusCode, string)>
        {
            ["/scheme/label-presets"] = (HttpStatusCode.OK, "[]"),
            ["/scheme/apply-preset"] = (HttpStatusCode.OK, ValidateJson),
        };
        var (client, handler) = await CtxClientFactory.ConnectAsync(routes);
        using (client)
        {
            await client.Scheme!.LabelPresetsAsync();
            await client.Scheme.ApplyPresetAsync("default-label", "portion");
            var sent = handler.Requests.Skip(1).Select(request => request.Path).ToArray();
            Assert.Equal(new[]
            {
                "/v1/ctx/acme-marking/1.0.0/scheme/label-presets",
                "/v1/ctx/acme-marking/1.0.0/scheme/apply-preset",
            }, sent);
        }
    }

    [Fact]
    public async Task SchemeSubResources_EscapeSchemeSelector()
    {
        var (client, handler) = await CtxClientFactory.ConnectAsync(RoutesFor("/scheme/concepts"));
        using (client)
        {
            await client.Scheme!.ConceptsAsync("acme/target v2");
            Assert.Equal(
                "/v1/ctx/acme-marking/1.0.0/scheme/concepts?scheme=acme%2Ftarget%20v2",
                handler.Requests[^1].Path);
        }
    }

    [Fact]
    public async Task SchemeSubResources_BlankSchemeSelectorIsTreatedAsAbsent()
    {
        var (client, handler) = await CtxClientFactory.ConnectAsync(RoutesFor("/scheme/concepts"));
        using (client)
        {
            await client.Scheme!.ConceptsAsync("   ");
            Assert.Equal("/v1/ctx/acme-marking/1.0.0/scheme/concepts", handler.Requests[^1].Path);
        }
    }

    private static Dictionary<string, (HttpStatusCode, string)> RoutesFor(string route) => new()
    {
        [route] = (HttpStatusCode.OK, route switch
        {
            "/1.0.0/scheme" => SummaryJson,
            "/scheme/grammar" => """{ "caseSensitive": false, "stripWhitespace": true }""",
            "/scheme/apply-preset" => ValidateJson,
            _ => "[]",
        }),
    };

    private static Task InvokeSubResource(EzClassifyClient client, string route, string? scheme) => route switch
    {
        "/1.0.0/scheme" => client.Scheme!.SummaryAsync(scheme),
        "/scheme/concepts" => client.Scheme!.ConceptsAsync(scheme),
        "/scheme/categories" => client.Scheme!.CategoriesAsync(scheme),
        "/scheme/formats" => client.Scheme!.FormatsAsync(scheme),
        "/scheme/grammar" => client.Scheme!.GrammarAsync(scheme),
        "/scheme/scopes" => client.Scheme!.ScopesAsync(scheme),
        _ => throw new ArgumentOutOfRangeException(nameof(route), route, "Unmapped sub-resource route"),
    };

    // --- guide ops ---------------------------------------------------------

    [Fact]
    public async Task GuideScan_RoutesAndParses()
    {
        var routes = new Dictionary<string, (HttpStatusCode, string)>
        {
            ["/guide/scan"] = (HttpStatusCode.OK, """
                [ { "rule": { "id": "r1", "name": "Rule", "outcome": {} }, "matchedKeywords": ["secret"], "spans": [{ "start": 0, "end": 6, "keyword": "secret" }], "guideId": "g1" } ]
                """),
        };
        var (client, handler) = await CtxClientFactory.ConnectAsync(routes);
        using (client)
        {
            var results = await client.Guide!.ScanAsync("secret text", "g1");
            Assert.Single(results.Result);
            Assert.Equal("r1", results.Result[0].Rule.Id);
            var (_, path, body) = handler.Requests[^1];
            Assert.Equal("/v1/ctx/acme-marking/1.0.0/guide/scan", path);
            Assert.DoesNotContain("schemeId", body);
        }
    }

    [Fact]
    public async Task GuideEvaluate_RoutesAndParses()
    {
        var routes = new Dictionary<string, (HttpStatusCode, string)>
        {
            ["/guide/evaluate"] = (HttpStatusCode.OK, """
                { "aggregatedMarking": "H", "selectedRules": [{ "id": "r1", "name": "Rule", "outcome": {} }], "activeComputedRules": [], "effectiveMarkings": ["H"], "breakdown": [], "contributingBreakdown": [], "guideId": "g1" }
                """),
        };
        var (client, handler) = await CtxClientFactory.ConnectAsync(routes);
        using (client)
        {
            var result = await client.Guide!.EvaluateAsync("g1", new[] { "r1" });
            Assert.Equal("H", result.Result.AggregatedMarking);
            var (_, path, body) = handler.Requests[^1];
            Assert.Equal("/v1/ctx/acme-marking/1.0.0/guide/evaluate", path);
            Assert.Contains("\"guideId\":\"g1\"", body);
            Assert.DoesNotContain("schemeId", body);
        }
    }

    [Fact]
    public async Task GuideEvaluateMany_SendsAggregateScope()
    {
        var routes = new Dictionary<string, (HttpStatusCode, string)>
        {
            ["/guide/evaluate"] = (HttpStatusCode.OK, """
                { "aggregatedMarking": "H", "selectedRules": [], "activeComputedRules": [], "effectiveMarkings": ["H"], "breakdown": [], "contributingBreakdown": [] }
                """),
        };
        var (client, handler) = await CtxClientFactory.ConnectAsync(routes);
        using (client)
        {
            var result = await client.Guide!.EvaluateManyAsync(new[] { "r2" });
            Assert.Equal("H", result.Result.AggregatedMarking);
            var (_, path, body) = handler.Requests[^1];
            Assert.Equal("/v1/ctx/acme-marking/1.0.0/guide/evaluate", path);
            Assert.Contains("\"allGuides\":true", body);
            Assert.Contains("\"selectedRuleIds\":[\"r2\"]", body);
            Assert.DoesNotContain("guideId", body);
        }
    }

    [Fact]
    public async Task GuideFilterRules_ParsesResult()
    {
        var routes = new Dictionary<string, (HttpStatusCode, string)>
        {
            ["/guide/filter-rules"] = (HttpStatusCode.OK, """
                { "visibleIds": ["r1"], "categoryCounts": {}, "total": { "visible": 1, "total": 3 } }
                """),
        };
        var (client, handler) = await CtxClientFactory.ConnectAsync(routes);
        using (client)
        {
            var result = await client.Guide!.FilterRulesAsync("sec");
            Assert.Single(result.Result.VisibleIds);
            Assert.Equal(3, result.Result.Total.Total);
            Assert.Equal("/v1/ctx/acme-marking/1.0.0/guide/filter-rules", handler.Requests[^1].Path);
        }
    }

    [Fact]
    public async Task GuideDescribeRule_ParsesResult()
    {
        var routes = new Dictionary<string, (HttpStatusCode, string)>
        {
            ["/guide/describe-rule"] = (HttpStatusCode.OK, """
                { "rule":{"artifact":{"kind":"guide","id":"g1","version":"1.0.0"},"ruleId":"r1","ruleDigest":"sha256:r1"}, "guide":{"kind":"guide","id":"g1","version":"1.0.0"}, "name": "Rule", "outcomeText": "marks H", "isComputed": false, "dependsOn": [], "overrides": [], "lines": ["Rule — marks H"] }
                """),
        };
        var (client, handler) = await CtxClientFactory.ConnectAsync(routes);
        using (client)
        {
            var desc = await client.Guide!.DescribeRuleAsync("r1", "g1");
            Assert.Equal("Rule", desc.Result.Name);
            Assert.Equal("marks H", desc.Result.OutcomeText);
            Assert.False(desc.Result.IsComputed);
            Assert.Equal("/v1/ctx/acme-marking/1.0.0/guide/describe-rule", handler.Requests[^1].Path);
        }
    }

    [Fact]
    public async Task GuideView_ParsesStructuralTree()
    {
        var routes = new Dictionary<string, (HttpStatusCode, string)>
        {
            ["/guide/view"] = (HttpStatusCode.OK, """
                { "id": "g1", "name": "Guide", "description": "", "categories": [], "uncategorized": [ { "id": "r1", "name": "Rule", "outcomeText": "H", "isComputed": false, "keywords": [], "matcherLabels": [], "dependsOn": [], "dependsOnNames": [], "overrides": [], "overrideNames": [] } ] }
                """),
        };
        var (client, handler) = await CtxClientFactory.ConnectAsync(routes);
        using (client)
        {
            var view = await client.Guide!.ViewAsync("g1");
            Assert.Equal("Guide", view.Result.Name);
            Assert.Single(view.Result.Uncategorized);
            Assert.Equal("/v1/ctx/acme-marking/1.0.0/guide/view", handler.Requests[^1].Path);
        }
    }

    // --- access ops --------------------------------------------------------

    [Fact]
    public async Task AccessCheck_OmitsSchemeId_UsesUserAttributes()
    {
        var routes = new Dictionary<string, (HttpStatusCode, string)>
        {
            ["/access/check"] = (HttpStatusCode.OK, """{ "granted": true, "rules": [ { "name": "Membership", "granted": true, "skipped": false, "ruleIndex": 0, "activation":{"matched":true,"assertions":[],"contextPaths":[]}, "comparisons":[], "rule":{"artifact":{"kind":"access","id":"ap","version":"1.0.0"},"ruleId":"r1","ruleDigest":"sha256:r1"} } ] }"""),
        };
        var (client, handler) = await CtxClientFactory.ConnectAsync(routes);
        using (client)
        {
            var assertions = new[] { new Assertion(CategoryId: "level", ConceptId: "LEVEL_HIGH", Confidence: Confidence.Certain, Source: "parse") };
            var decision = await client.Access!.CheckAsync(assertions, """{"membership":"HIGH"}""");
            Assert.True(decision.Result.Granted);
            Assert.Single(decision.Result.Rules);
            var (_, path, body) = handler.Requests[^1];
            Assert.Equal("/v1/ctx/acme-marking/1.0.0/access/check", path);
            Assert.Contains("\"userAttributes\"", body);
            Assert.Contains("\"assertions\"", body);
            Assert.DoesNotContain("dataAssertions", body);
            Assert.DoesNotContain("schemeId", body);
        }
    }

    [Fact]
    public async Task AccessCheckByMarking_SendsMarking()
    {
        var routes = new Dictionary<string, (HttpStatusCode, string)>
        {
            ["/access/check"] = (HttpStatusCode.OK, """{ "granted": false, "rules": [] }"""),
        };
        var (client, handler) = await CtxClientFactory.ConnectAsync(routes);
        using (client)
        {
            var decision = await client.Access!.CheckByMarkingAsync("RS/SO", """{"membership":"S"}""");
            Assert.False(decision.Result.Granted);
            var body = handler.Requests[^1].Body;
            Assert.Contains("\"marking\":\"RS/SO\"", body);
            Assert.DoesNotContain("schemeId", body);
        }
    }

    [Fact]
    public async Task AccessDescribeRequirements_ParsesResult()
    {
        var routes = new Dictionary<string, (HttpStatusCode, string)>
        {
            ["/access/describe-requirements"] = (HttpStatusCode.OK, """
                { "marking": "RS/SO", "assertions": [], "assertionDisplays": [],
                  "access":{"kind":"access","id":"ap","version":"1.0.0"}, "scheme":{"kind":"scheme","id":"s","version":"1.0.0"},
                  "appliedRules": [{"artifact":{"kind":"access","id":"ap","version":"1.0.0"},"ruleId":"r1","ruleDigest":"sha256:r1"}],
                  "rules": [ { "rule":{"artifact":{"kind":"access","id":"ap","version":"1.0.0"},"ruleId":"r1","ruleDigest":"sha256:r1"}, "access":{"kind":"access","id":"ap","version":"1.0.0"}, "scheme":{"kind":"scheme","id":"s","version":"1.0.0"}, "name": "SO", "requirementText": "Staff only", "attributesNeeded": ["nationality"], "dependsOn": [], "overrides": [], "lines": [] } ],
                  "combinedRequirementText": "Staff only", "combinedAttributesNeeded": ["nationality"], "lines": ["Staff only"] }
                """),
        };
        var (client, handler) = await CtxClientFactory.ConnectAsync(routes);
        using (client)
        {
            var req = await client.Access!.DescribeRequirementsAsync("RS/SO");
            Assert.Single(req.Result.Rules);
            Assert.Equal("nationality", req.Result.CombinedAttributesNeeded[0]);
            var (_, path, body) = handler.Requests[^1];
            Assert.Equal("/v1/ctx/acme-marking/1.0.0/access/describe-requirements", path);
            Assert.DoesNotContain("\"schemeId\":\"acme", body);
        }
    }

    [Fact]
    public async Task AccessDescribeRequirements_AcceptsAssertionsOnlyOrMergedWithMarking()
    {
        var routes = new Dictionary<string, (HttpStatusCode, string)>
        {
            ["/access/describe-requirements"] = (HttpStatusCode.OK, """
                { "marking": "HIGH", "assertions": [], "assertionDisplays": [],
                  "access":{"kind":"access","id":"ap","version":"1.0.0"}, "scheme":{"kind":"scheme","id":"s","version":"1.0.0"},
                  "appliedRules": [], "rules": [], "combinedRequirementText": "", "combinedAttributesNeeded": [], "lines": [] }
                """),
        };
        var (client, handler) = await CtxClientFactory.ConnectAsync(routes);
        using (client)
        {
            var assertions = new[]
            {
                new Assertion(CategoryId: "level", ConceptId: "HIGH", Confidence: Confidence.Certain, Source: "parse"),
            };

            await client.Access!.DescribeRequirementsAsync(assertions);
            Assert.Contains("\"assertions\"", handler.Requests[^1].Body);
            Assert.DoesNotContain("\"marking\"", handler.Requests[^1].Body);

            await client.Access.DescribeRequirementsAsync("HIGH", assertions);
            Assert.Contains("\"marking\":\"HIGH\"", handler.Requests[^1].Body);
            Assert.Contains("\"assertions\"", handler.Requests[^1].Body);
        }
    }

    [Fact]
    public async Task AccessDescribeRequirements_RejectsEmptyStructuredTarget()
    {
        var (client, _) = await CtxClientFactory.ConnectAsync();
        using (client)
        {
            await Assert.ThrowsAsync<ArgumentException>(
                () => client.Access!.DescribeRequirementsAsync(Array.Empty<Assertion>()));
        }
    }

    [Fact]
    public async Task AccessDescribeRule_ParsesResult()
    {
        var routes = new Dictionary<string, (HttpStatusCode, string)>
        {
            ["/access/describe-rule"] = (HttpStatusCode.OK, """
                { "rule":{"artifact":{"kind":"access","id":"ap","version":"1.0.0"},"ruleId":"r1","ruleDigest":"sha256:r1"}, "access":{"kind":"access","id":"ap","version":"1.0.0"}, "scheme":{"kind":"scheme","id":"s","version":"1.0.0"}, "name": "SO", "requirementText": "Staff only", "attributesNeeded": ["nationality"], "dependsOn": [], "overrides": [], "lines": [] }
                """),
        };
        var (client, handler) = await CtxClientFactory.ConnectAsync(routes);
        using (client)
        {
            var rule = await client.Access!.DescribeRuleAsync("r1");
            Assert.Equal("SO", rule.Result.Name);
            Assert.Single(rule.Result.AttributesNeeded);
            Assert.Equal("/v1/ctx/acme-marking/1.0.0/access/describe-rule", handler.Requests[^1].Path);
        }
    }

    [Fact]
    public async Task AccessRules_ListsDescriptionsAndIdsFromOneResource()
    {
        var routes = new Dictionary<string, (HttpStatusCode, string)>
        {
            ["/access/rules"] = (HttpStatusCode.OK, """
                { "rules": [
                  { "rule":{"artifact":{"kind":"access","id":"ap","version":"1.0.0"},"ruleId":"r1","ruleDigest":"sha256:r1"},
                    "access":{"kind":"access","id":"ap","version":"1.0.0"}, "scheme":{"kind":"scheme","id":"s","version":"1.0.0"},
                    "name":"Staff", "requirementText":"Staff only", "attributesNeeded":["membership"], "dependsOn":[], "overrides":[], "lines":[] }
                ] }
                """),
        };
        var (client, handler) = await CtxClientFactory.ConnectAsync(routes);
        using (client)
        {
            var rules = await client.Access!.RulesAsync();
            Assert.Single(rules);
            Assert.Equal("Staff", rules[0].Name);

            var ids = await client.Access.RuleIdsAsync();
            Assert.Equal(new[] { "r1" }, ids);
            Assert.Equal("GET", handler.Requests[^1].Method);
            Assert.Equal("/v1/ctx/acme-marking/1.0.0/access/rules", handler.Requests[^1].Path);
        }
    }

    [Fact]
    public async Task CrosswalkTranslate_SupportsMarkingAndStructuredSources()
    {
        var routes = new Dictionary<string, (HttpStatusCode, string)>
        {
            ["/crosswalk/translate"] = (HttpStatusCode.OK, """
                { "status":"forbidden", "crosswalk":{"kind":"crosswalk","id":"x","version":"1.0.0"},
                  "sourceScheme":{"kind":"scheme","id":"s1","version":"1.0.0"},
                  "targetScheme":{"kind":"scheme","id":"s2","version":"1.0.0"},
                  "source":{ "assertions": [], "valid": true, "inputWasValid": true, "errors": [], "warnings": [], "appliedFixes": [], "rendered": "H", "assessment": { "emitted": [], "omitted": [], "collapsed": [], "conflicts": [], "unrepresentable": [] }, "notices": [] }, "outcomes":[], "selections":[], "forbiddenBy":[] }
                """),
        };
        var (client, handler) = await CtxClientFactory.ConnectAsync(routes);
        using (client)
        {
            var byMarking = await client.Crosswalk!.TranslateAsync("x", "HIGH");
            Assert.Equal("forbidden", byMarking.Result.GetProperty("status").GetString());
            Assert.Contains("\"marking\":\"HIGH\"", handler.Requests[^1].Body);

            var source = new[]
            {
                new Assertion(CategoryId: "level", ConceptId: "HIGH", Confidence: Confidence.Certain, Source: "parse"),
            };
            await client.Crosswalk.TranslateAsync("x", source);
            Assert.Contains("\"assertions\"", handler.Requests[^1].Body);
            Assert.DoesNotContain("\"marking\"", handler.Requests[^1].Body);
            Assert.Equal("/v1/ctx/acme-marking/1.0.0/crosswalk/translate", handler.Requests[^1].Path);
        }
    }

    [Fact]
    public async Task CrosswalkTranslate_RejectsMissingInputsBeforeSending()
    {
        var (client, handler) = await CtxClientFactory.ConnectAsync();
        using (client)
        {
            await Assert.ThrowsAsync<ArgumentException>(() => client.Crosswalk!.TranslateAsync("", "HIGH"));
            await Assert.ThrowsAsync<ArgumentException>(() => client.Crosswalk!.TranslateAsync("x", ""));
            await Assert.ThrowsAsync<ArgumentException>(
                () => client.Crosswalk!.TranslateAsync("x", Array.Empty<Assertion>()));
            Assert.Single(handler.Requests);
        }
    }

    // --- document ----------------------------------------------------------

    [Fact]
    public async Task DocumentAudit_ParsesReport()
    {
        var routes = new Dictionary<string, (HttpStatusCode, string)>
        {
            ["/document/audit"] = (HttpStatusCode.OK, """
                { "resolution":{"contextId":"acme-marking","requestedVersion":"1.0.0","resolvedVersion":"1.0.0","artifacts":[],"sources":[]},
                  "operationLimitations":[], "responseProjection":{"profile":"standard","detailsComplete":false,"omittedFields":["/result/unmarkedSpans/0/text"],"counts":{"nominations":0,"evidenceSpans":0,"guideUnits":0,"accessRuleResults":0,"contentRequirements":0,"contextRequirements":0}},
                  "result": { "whole": "RS/SO",
                  "wholes": [ { "marking": "RS/SO", "start": 0, "end": 6, "offsetEncoding":"utf-8-bytes" } ],
                  "wholeValidation": { "assertions": [ { "categoryId": "level", "conceptId": "RS", "confidence": "certain", "source": "parse" } ],
                                       "recognition": { "status": "complete", "inputFormat": "banner", "unmatched": [] },
                                       "valid": true, "inputWasValid": true, "errors": [], "proposedFixes":[], "appliedFixes": [], "rendered": "RS/SO" },
                  "parts": [ { "marking": "S", "text": "para", "start": 10, "end": 14, "offsetEncoding":"utf-8-bytes", "scopeId":"part:1",
                               "validation": { "assertions": [ { "categoryId": "level", "conceptId": "S", "confidence": "certain", "source": "parse" } ],
                                               "recognition": { "status": "complete", "inputFormat": "sensitivity/short", "role": "part", "surface": "sensitivity", "unmatched": [] },
                                               "valid": true, "inputWasValid": false, "errors": [], "proposedFixes":[],
                                               "appliedFixes": [ { "ruleId": "_recognition_case", "action": "reorder", "message": "Read s as S (case)." } ],
                                               "rendered": "S" } } ],
                  "wholeCoversParts": { "covered": true, "missing": [], "missingLabels": [], "excess": [], "excessLabels": [], "rendered": "RS/SO" },
                  "unmarkedSpans": [ { "start": 20, "end": 25, "offsetEncoding":"utf-8-bytes" } ],
                  "proceduralFlags": ["unmarked-content"], "overallVerdict": "mixed", "vocabulary":null,
                  "documentDigest":"sha256:doc", "guideAssessment":null,"markingRequirements":{"whole":false,"part":false}, "limitations":[] } }
                """),
        };
        var (client, handler) = await CtxClientFactory.ConnectAsync(routes);
        using (client)
        {
            var envelope = await client.Document!.AuditAsync("a document");
            var report = envelope.Result;
            Assert.Equal("RS/SO", report.Whole);
            Assert.Single(report.Wholes);
            Assert.Single(report.Parts);
            Assert.Equal("part:1", report.Parts[0].ScopeId);
            // An audit entry carries the facts and the recognition account, so a
            // caller works from the audit instead of reading the marking again.
            var part = report.Parts[0].Validation;
            Assert.Equal("S", Assert.Single(part.Assertions).ConceptId);
            Assert.Equal("sensitivity/short", part.Recognition.InputFormat);
            Assert.Equal(StructuralRole.Part, part.Recognition.Role);
            Assert.True(part.Valid);
            Assert.False(part.InputWasValid);
            Assert.Equal(FixAction.Reorder, Assert.Single(part.AppliedFixes).Action);
            Assert.True(report.WholeValidation!.InputWasValid);
            Assert.Null(report.WholeValidation.Recognition.Role);
            Assert.NotNull(report.WholeCoversParts);
            Assert.Single(report.UnmarkedSpans);
            Assert.Equal(AuditVerdict.Mixed, report.OverallVerdict);
            Assert.Equal(DocumentResponseProfile.Standard, envelope.ResponseProjection!.Profile);
            var (_, path, body) = handler.Requests[^1];
            Assert.Equal("/v1/ctx/acme-marking/1.0.0/document/audit", path);
            Assert.Contains("\"text\":\"a document\"", body);
        }
    }

    [Fact]
    public async Task DocumentAssessAccess_RoutesProfileContextAndParsesScenarios()
    {
        const string audit = """{"whole":null,"wholes":[],"wholeValidation":null,"parts":[],"wholeCoversParts":null,"unmarkedSpans":[],"proceduralFlags":[],"overallVerdict":"pass","vocabulary":null,"documentDigest":"sha256:d","guideAssessment":null,"markingRequirements":{"whole":false,"part":false},"limitations":[]}""";
        var response = """
          {"resolution":{"contextId":"acme-marking","resolvedVersion":"1.0.0","artifacts":[],"sources":[]},"operationLimitations":[],
           "responseProjection":{"profile":"summary","detailsComplete":false,"omittedFields":["/result/authored/decision"],"counts":{"nominations":0,"evidenceSpans":0,"guideUnits":0,"accessRuleResults":0,"contentRequirements":0,"contextRequirements":0}},
           "result":{"audit":AUDIT,"authored":{"basis":"authored","advisory":false,"status":"not-evaluated","target":null,"limitations":[]},
             "authoritative":{"basis":"authoritative","advisory":false,"status":"not-evaluated","target":null,"limitations":[]},"projected":null}}
          """.Replace("AUDIT", audit);
        var routes = new Dictionary<string, (HttpStatusCode, string)>
        {
            ["/document/assess-access"] = (HttpStatusCode.OK, response),
        };
        var (client, handler) = await CtxClientFactory.ConnectAsync(routes);
        using (client)
        {
            var result = await client.Document!.AssessAccessAsync(
                "document", """{"clearance":"high"}""", """{"team":"a"}""",
                responseProfile: DocumentResponseProfile.Summary);
            Assert.Equal(AccessScenarioStatus.NotEvaluated, result.Result.Authored.Status);
            Assert.Null(result.Result.Projected);
            Assert.Equal(DocumentResponseProfile.Summary, result.ResponseProjection!.Profile);
            var (_, path, body) = handler.Requests[^1];
            Assert.Equal("/v1/ctx/acme-marking/1.0.0/document/assess-access", path);
            Assert.Contains("\"userAttributes\":{\"clearance\":\"high\"}", body);
            Assert.Contains("\"context\":{\"team\":\"a\"}", body);
            Assert.Contains("\"responseProfile\":\"summary\"", body);
        }
    }

    // --- errors ------------------------------------------------------------

    [Fact]
    public async Task Op_ServerError_ThrowsEzClassifyClientException()
    {
        var routes = new Dictionary<string, (HttpStatusCode, string)>
        {
            ["/scheme/process"] = (HttpStatusCode.NotFound, """{ "error": "Context not found" }"""),
        };
        var (client, _) = await CtxClientFactory.ConnectAsync(routes);
        using (client)
        {
            var ex = await Assert.ThrowsAsync<EzClassifyClientException>(() => client.Scheme!.ProcessAsync("H"));
            Assert.Contains("404", ex.Message);
        }
    }

    [Theory]
    [InlineData("stale-guide-determination")]
    [InlineData("stale-manual-selection")]
    public async Task DocumentConflict_PreservesStatusCodeAndStableDetail(string detail)
    {
        var routes = new Dictionary<string, (HttpStatusCode, string)>
        {
            ["/document/audit"] = (HttpStatusCode.Conflict, $$"""{"error":"stale determination","code":"INVALID_INPUT","detail":"{{detail}}"}"""),
        };
        var (client, _) = await CtxClientFactory.ConnectAsync(routes);
        using (client)
        {
            var ex = await Assert.ThrowsAsync<EzClassifyClientException>(() => client.Document!.AuditAsync("document"));
            Assert.Equal(409, ex.StatusCode);
            Assert.Equal("INVALID_INPUT", ex.Code);
            Assert.Equal(detail, ex.Detail);
            Assert.Contains("stale determination", ex.ResponseBody);
        }
    }

    [Fact]
    public async Task Op_ServerError_ExposesStatusCode()
    {
        var routes = new Dictionary<string, (HttpStatusCode, string)>
        {
            ["/scheme/process"] = (HttpStatusCode.Forbidden, """{ "error": "Access denied" }"""),
        };
        var (client, _) = await CtxClientFactory.ConnectAsync(routes);
        using (client)
        {
            var ex = await Assert.ThrowsAsync<EzClassifyClientException>(() => client.Scheme!.ProcessAsync("H"));
            Assert.Equal(403, ex.StatusCode);
        }
    }

    [Fact]
    public async Task Op_ConnectionFailure_StatusCodeIsNull()
    {
        // Connect succeeds via the describe probe; the op itself fails before any
        // HTTP response, so no status code applies.
        var handler = new CtxOpConnectionFailHandler();
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:8080") };
        using var client = await EzClassifyClient.ConnectAsync(http, "acme-marking", "1.0.0");

        var ex = await Assert.ThrowsAsync<EzClassifyClientException>(() => client.Scheme!.ProcessAsync("H"));
        Assert.Null(ex.StatusCode);
    }

    [Fact]
    public async Task ConnectAsync_ContextNotFound_Throws()
    {
        // The connect-time probe path matches the describe stub, so register a
        // route that the describe path will hit: any path contains "/ctx/".
        var routes = new Dictionary<string, (HttpStatusCode, string)>
        {
            ["/ctx/"] = (HttpStatusCode.NotFound, """{ "error": "Context not found: missing" }"""),
        };
        var handler = new CtxMockHandler(routes);
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:8080") };
        await Assert.ThrowsAsync<EzClassifyClientException>(
            () => EzClassifyClient.ConnectAsync(http, "missing", "1.0.0"));
    }

    // --- root discovery client has no Context operation groups --------------

    [Fact]
    public void RootClient_HasNoContextSubObjects()
    {
        using var client = new EzClassifyClient(
            new HttpClient { BaseAddress = new Uri("http://localhost:8080") });
        Assert.Null(client.Scheme);
        Assert.Null(client.Guide);
        Assert.Null(client.Access);
        Assert.Null(client.Document);
        Assert.Null(client.ResolvedVersion);
    }
}

// ---------------------------------------------------------------------------
// Owned-HttpClient disposal on connect failure.
//
// ConnectAsync(string, ...) creates and owns an HttpClient. When the initial
// context probe fails (server down, unknown context, cancellation) no client
// instance is returned, so nothing else can ever dispose it: the connect core
// must dispose it on the failure path or every failed connect leaks a handler
// and its sockets. Exercised through the internal owned-connect seam because
// the public overload constructs the HttpClient itself.
// ---------------------------------------------------------------------------

public class ConnectOwnedClientDisposalTests
{
    private sealed class DisposalTrackingHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode? _status;

        public bool Disposed { get; private set; }

        /// <param name="status">Response status for the probe; null throws HttpRequestException instead.</param>
        public DisposalTrackingHandler(HttpStatusCode? status) => _status = status;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_status is null)
                throw new HttpRequestException("Connection refused");
            return Task.FromResult(new HttpResponseMessage(_status.Value)
            {
                Content = new StringContent(ContextProbe.Body, Encoding.UTF8, "application/json"),
            });
        }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }

    [Fact]
    public async Task ConnectOwned_ConnectionFailure_DisposesOwnedClient()
    {
        var handler = new DisposalTrackingHandler(status: null);
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:8080") };

        await Assert.ThrowsAsync<EzClassifyClientException>(
            () => EzClassifyClient.ConnectOwnedAsync(http, "ctx-demo", null, CancellationToken.None));
        Assert.True(handler.Disposed);
    }

    [Fact]
    public async Task ConnectOwned_ContextNotFound_DisposesOwnedClient()
    {
        var handler = new DisposalTrackingHandler(HttpStatusCode.NotFound);
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:8080") };

        await Assert.ThrowsAsync<EzClassifyClientException>(
            () => EzClassifyClient.ConnectOwnedAsync(http, "missing", "1.0.0", CancellationToken.None));
        Assert.True(handler.Disposed);
    }

    [Fact]
    public async Task ConnectOwned_Canceled_DisposesOwnedClient()
    {
        var handler = new DisposalTrackingHandler(HttpStatusCode.OK);
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:8080") };
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => EzClassifyClient.ConnectOwnedAsync(http, "ctx-demo", null, cts.Token));
        Assert.True(handler.Disposed);
    }

    [Fact]
    public async Task ConnectOwned_UnpinnedAccessCaching_DisposesOwnedClientWithoutProbing()
    {
        var handler = new DisposalTrackingHandler(HttpStatusCode.OK);
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:8080") };

        await Assert.ThrowsAsync<ArgumentException>(() => EzClassifyClient.ConnectOwnedAsync(
            http, "ctx-demo", "latest", RestClientCredentials.None,
            new ContextCacheOptions(CacheAccessDecisions: true), CancellationToken.None));
        Assert.True(handler.Disposed);
    }

    [Fact]
    public async Task ConnectOwned_Success_ReturnsUsableClientAndDisposesOnlyOnClientDispose()
    {
        var handler = new DisposalTrackingHandler(HttpStatusCode.OK);
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:8080") };

        var client = await EzClassifyClient.ConnectOwnedAsync(http, "ctx-demo", null, CancellationToken.None);
        Assert.False(handler.Disposed);
        client.Dispose();
        Assert.True(handler.Disposed);
    }
}

// ---------------------------------------------------------------------------
// Server-wide health remains available on a Context-bound client.
// ---------------------------------------------------------------------------

public class ContextBoundHealthTests
{
    private static async Task<EzClassifyClient> ConnectAsync()
    {
        var (client, _) = await CtxClientFactory.ConnectAsync();
        return client;
    }

    [Fact]
    public async Task PingAsync_OnContextBoundClient_StillWorks()
    {
        // Ping is Context-independent and stays usable on the bound shape.
        var routes = new Dictionary<string, (HttpStatusCode, string)>
        {
            ["/health"] = (HttpStatusCode.OK, """{ "status": "ok", "engineVersion": "1.0.0", "engineSha": "", "gitTag": "", "openapiVersion": "1.0.0", "abiVersion": "v1" }"""),
        };
        var (client, _) = await CtxClientFactory.ConnectAsync(routes);
        using (client)
        {
            var ping = await client.PingAsync();
            Assert.Equal("1.0.0", ping.EngineVersion);
        }
    }
}

/// <summary>
/// Argument guards on the three operations that take caller-authored JSON.
/// Bad input is rejected at the boundary with the parameter named, instead of
/// a parser exception or a mis-shaped request reaching the server.
/// </summary>
public class ContextClientJsonArgumentGuardTests
{
    private static async Task<EzClassifyClient> ConnectAsync()
    {
        var (client, _) = await CtxClientFactory.ConnectAsync(new Dictionary<string, (HttpStatusCode, string)>());
        return client;
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{not json")]
    [InlineData("[\"membership\"]")]
    [InlineData("\"HIGH\"")]
    public async Task CheckByMarking_RejectsNonObjectAttributes(string attributes)
    {
        using var client = await ConnectAsync();

        var error = await Assert.ThrowsAsync<ArgumentException>(
            () => client.Access!.CheckByMarkingAsync("RS/SO", attributes));
        Assert.Equal("userAttributesJson", error.ParamName);
    }

    [Fact]
    public async Task Check_RejectsNonObjectAttributes()
    {
        using var client = await ConnectAsync();
        var assertions = new[]
        {
            new Assertion(CategoryId: "level", ConceptId: "LEVEL_HIGH", Confidence: Confidence.Certain, Source: "parse"),
        };

        var error = await Assert.ThrowsAsync<ArgumentException>(
            () => client.Access!.CheckAsync(assertions, "[]"));
        Assert.Equal("userAttributesJson", error.ParamName);
    }

    [Fact]
    public async Task DescribeRequirements_EmptyMarkingIsNotSent()
    {
        var routes = new Dictionary<string, (HttpStatusCode, string)>
        {
            ["/access/describe-requirements"] = (HttpStatusCode.OK, """
                { "assertions": [], "assertionDisplays": [],
                  "access":{"kind":"access","id":"ap","version":"1.0.0"}, "scheme":{"kind":"scheme","id":"s","version":"1.0.0"},
                  "appliedRules": [], "rules": [], "combinedRequirementText": "", "combinedAttributesNeeded": [], "lines": [] }
                """),
        };
        var (client, handler) = await CtxClientFactory.ConnectAsync(routes);
        using (client)
        {
            var assertions = new[]
            {
                new Assertion(CategoryId: "level", ConceptId: "LEVEL_HIGH", Confidence: Confidence.Certain, Source: "parse"),
            };

            await client.Access!.DescribeRequirementsAsync("", assertions);

            var (_, _, body) = handler.Requests[^1];
            Assert.DoesNotContain("\"marking\"", body);
        }
    }

    [Fact]
    public async Task AssessAccess_RejectsNonObjectContext()
    {
        using var client = await ConnectAsync();

        var error = await Assert.ThrowsAsync<ArgumentException>(
            () => client.Document!.AssessAccessAsync("text", """{"membership":"HIGH"}""", contextJson: "7"));
        Assert.Equal("contextJson", error.ParamName);
    }
}
