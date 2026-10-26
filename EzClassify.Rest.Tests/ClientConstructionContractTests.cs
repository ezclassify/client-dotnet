using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;

namespace EzClassify.Tests;

// ---------------------------------------------------------------------------
// EzClassifyClient construction and lifetime contract.
//
// The string-URL constructor and ConnectAsync overloads create and own an
// HttpClient, so Dispose must release it. Caller-facing arguments (baseUrl,
// contextId) are validated before any network call so a bad argument surfaces
// as ArgumentException rather than a connection failure.
// ---------------------------------------------------------------------------

public class EzClassifyClientConstructionTests
{
    [Fact]
    public async Task StringUrlConstructor_Dispose_DisposesOwnedHttpClient()
    {
        var client = new EzClassifyClient("http://localhost:8080");
        client.Dispose();

        // The string-URL constructor owns its HttpClient, so a request after
        // Dispose must surface the disposed transport instead of connecting.
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.PingAsync());
    }

    [Fact]
    public async Task HttpClientConstructor_Dispose_LeavesCallerClientUsable()
    {
        const string json = """{"status":"ok","engineVersion":"1","engineSha":"","gitTag":"","openapiVersion":"1.0.0","abiVersion":"v1"}""";
        var http = new HttpClient(new MockHttpHandler(json))
        { BaseAddress = new Uri("http://localhost:8080") };
        var client = new EzClassifyClient(http);
        client.Dispose();

        // A caller-supplied HttpClient is not owned; it must survive Dispose.
        var response = await http.GetAsync("v1/health");
        Assert.True(response.IsSuccessStatusCode);
    }

    [Fact]
    public async Task ConnectAsync_StringUrl_EmptyBaseUrl_ThrowsBeforeRequest()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => EzClassifyClient.ConnectAsync("", "my-ctx", "1.0.0"));
    }

    [Fact]
    public async Task ConnectAsync_StringUrl_NullBaseUrl_ThrowsBeforeRequest()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => EzClassifyClient.ConnectAsync((string)null!, "my-ctx", "1.0.0"));
    }

    [Fact]
    public async Task ConnectAsync_HttpClient_EmptyContextId_Throws()
    {
        using var http = new HttpClient { BaseAddress = new Uri("http://localhost:8080/") };
        await Assert.ThrowsAsync<ArgumentException>(
            () => EzClassifyClient.ConnectAsync(http, "", "1.0.0"));
    }
}

public class EzClassifyClientExceptionTests
{
    [Fact]
    public void Constructor_WithInner_PreservesMessageAndInner()
    {
        var inner = new InvalidOperationException("root cause");
        var ex = new EzClassifyClientException("outer message", inner);

        Assert.Equal("outer message", ex.Message);
        Assert.Same(inner, ex.InnerException);
    }
}

public class EzClassifyClientPingTests
{
    [Fact]
    public async Task PingAsync_VersionWithoutMinor_DoesNotThrow()
    {
        // A wire version with no dot (e.g. "1") must not break the major.minor
        // comparison used for the version-mismatch warning.
        const string json = """{"status":"ok","engineVersion":"1","engineSha":"","gitTag":"","openapiVersion":"1.0.0","abiVersion":"v1"}""";
        var http = new HttpClient(new MockHttpHandler(json))
        { BaseAddress = new Uri("http://localhost:8080") };
        using var client = new EzClassifyClient(http);

        var result = await client.PingAsync();

        Assert.Equal("1", result.EngineVersion);
    }

    // The skew notice must not go to System.Diagnostics.Debug, which is
    // [Conditional("DEBUG")] and therefore compiled out of the Release assembly
    // that gets packed. These pin the notice to a channel a package consumer
    // can observe.

    [Fact]
    public async Task PingAsync_SkewedServerVersion_RaisesTheNoticeOnce()
    {
        const string json = """{"status":"ok","engineVersion":"9.9.9","engineSha":"","gitTag":"","openapiVersion":"1.0.0","abiVersion":"v1"}""";
        using var http = new HttpClient(new MockHttpHandler(json))
        { BaseAddress = new Uri("http://localhost:8080") };
        using var client = new EzClassifyClient(http);
        var notices = new List<string>();
        client.DiagnosticSink = notices.Add;

        await client.PingAsync();
        await client.PingAsync();

        Assert.Single(notices);
        Assert.Contains("9.9.9", notices[0]);
        Assert.Contains(client.EngineSpecVersion, notices[0]);
    }

    [Fact]
    public async Task PingAsync_MatchingServerVersion_RaisesNoNotice()
    {
        var json =
            $$"""{"status":"ok","engineVersion":"{{SpecVersionForTest}}","engineSha":"","gitTag":"","openapiVersion":"1.0.0","abiVersion":"v1"}""";
        using var http = new HttpClient(new MockHttpHandler(json))
        { BaseAddress = new Uri("http://localhost:8080") };
        using var client = new EzClassifyClient(http);
        var notices = new List<string>();
        client.DiagnosticSink = notices.Add;

        await client.PingAsync();

        Assert.Empty(notices);
    }

    private static string SpecVersionForTest
    {
        get
        {
            using var probe = new HttpClient { BaseAddress = new Uri("http://localhost:8080") };
            using var client = new EzClassifyClient(probe);
            return client.EngineSpecVersion;
        }
    }
}

// ---------------------------------------------------------------------------
// Context-bound request body contract: optional payload fields are only sent
// when supplied. An output format argument and an assertion value both ride the request
// body when present.
// ---------------------------------------------------------------------------

public class CtxRequestBodyTests
{
    private const string ProcessJson = """
        { "valid": true, "rendered": "H", "inputWasValid": true,
          "renders": {},
          "assessment":{"emitted":[],"omitted":[],"collapsed":[],"conflicts":[],"unrepresentable":[]},
          "assertions": [
            { "conceptId": "L", "categoryId": "level", "confidence": "certain", "source": "parse" }
          ],
          "errors": [], "warnings": [], "appliedFixes": [], "proposedFixes": [], "notices": [] }
        """;

    private const string RenderJson = """
        {"result":"H","assessment":{"emitted":[],"omitted":[],"collapsed":[],"conflicts":[],"unrepresentable":[]}}
        """;

    [Fact]
    public async Task ProcessAsync_WithOutputFormat_SendsOutputFormatInBody()
    {
        var routes = new Dictionary<string, (HttpStatusCode, string)>
        {
            ["scheme/process"] = (HttpStatusCode.OK, ProcessJson),
        };
        var (client, handler) = await CtxClientFactory.ConnectAsync(routes);
        using (client)
        {
            await client.Scheme!.ProcessAsync("H", outputFormat: "banner");
            var body = handler.Requests.Last().Body ?? "";
            Assert.Contains("\"outputFormat\":\"banner\"", body);
            Assert.DoesNotContain("\"format\"", body);
        }
    }

    [Fact]
    public async Task RenderAsync_AssertionWithValue_SendsValueInBody()
    {
        var routes = new Dictionary<string, (HttpStatusCode, string)>
        {
            ["scheme/render"] = (HttpStatusCode.OK, RenderJson),
        };
        var (client, handler) = await CtxClientFactory.ConnectAsync(routes);
        using (client)
        {
            var assertions = new[]
            {
                new Assertion(
                    CategoryId: "level",
                    ConceptId: "LEVEL_HIGH",
                    Confidence: Confidence.Certain,
                    Source: "parse",
                    Value: "H")
            };
            await client.Scheme!.RenderAsync(assertions);
            var body = handler.Requests.Last().Body ?? "";
            Assert.Contains("\"value\"", body);
            Assert.Contains("\"H\"", body);
            // The enum must encode to its wire token, never a .NET name or ordinal.
            Assert.Contains("\"confidence\":\"certain\"", body);
        }
    }
}
