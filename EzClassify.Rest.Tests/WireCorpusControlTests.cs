using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using System.Reflection;
using System.Security.Cryptography;
using Xunit;

namespace EzClassify.Tests;

public class WireCorpusControlTests
{
    private static string Key(string method, string target, JsonNode? body, bool hasBody)
    {
        var matcher = typeof(WireCorpus).GetMethod("MatchKey", BindingFlags.Static | BindingFlags.Public);
        Assert.NotNull(matcher);
        Assert.Equal(4, matcher.GetParameters().Length);
        return Assert.IsType<string>(matcher.Invoke(null, new object?[] { method, target, body, hasBody }));
    }

    [Theory]
    [InlineData("/x/", "/x")]
    [InlineData("/x//", "/x/")]
    [InlineData("/x?key=", "/x?key")]
    [InlineData("/x?a-=1&a=2", "/x?a=2&a-=1")]
    [InlineData("/a%2Fb?z=2&a=1", "/a%2Fb?a=1&z=2")]
    public void CanonicalTargetMatchesOwnerRules(string target, string expected) => Assert.Equal(expected, WireCorpus.CanonicalTarget(target));

    [Fact]
    public void AbsentNullAndEmptyObjectBodiesStayDistinct()
    {
        Assert.NotEqual(Key("POST", "/x", null, false), Key("POST", "/x", null, true));
        Assert.NotEqual(Key("POST", "/x", null, false), Key("POST", "/x", new JsonObject(), true));
    }

    [Theory]
    [InlineData("body")]
    [InlineData("array")]
    [InlineData("path")]
    [InlineData("method")]
    [InlineData("double-slash")]
    [InlineData("carrier")]
    [InlineData("bearer")]
    [InlineData("accept")]
    [InlineData("content-type")]
    public async Task ChangedRequestCannotPassReplay(string mutation)
    {
        var recorded = WireCorpus.Vendored.ById["ctx-scheme-process.outputs-and-options"];
        var corpus = WireCorpus.Vendored;
        if (mutation == "accept")
        {
            // This recording does not declare Accept. A finite synthetic case
            // proves arbitrary declared headers are enforced without inventing
            // a required Accept policy for recordings that omit it.
            recorded = recorded with { RequestHeaders = new Dictionary<string, string>(recorded.RequestHeaders) { ["accept"] = "application/json" } };
            corpus = Assert.IsType<WireCorpus>(Activator.CreateInstance(typeof(WireCorpus), BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null, args: new object?[] { new[] { recorded } }, culture: null));
        }
        using var handler = new WireReplayHandler(corpus, new[] { recorded.Id }, null);
        using var invoker = new HttpMessageInvoker(handler);
        var body = recorded.RequestBody!.DeepClone();
        if (mutation == "body") body["input"] = "CHANGED";
        if (mutation == "array") body["outputs"] = new JsonArray("changed", "default");
        var target = recorded.Target + (mutation == "path" ? "-changed" : mutation == "double-slash" ? "//" : "");
        using var request = new HttpRequestMessage(mutation == "method" ? HttpMethod.Get : HttpMethod.Post, "http://replay.invalid" + target)
        { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, mutation == "content-type" ? "text/plain" : "application/json") };
        request.Headers.TryAddWithoutValidation("accept", mutation == "accept" ? "text/plain" : "application/json");
        request.Headers.TryAddWithoutValidation("x-ezclassify-context-token", mutation == "carrier" ? "changed" : "fixture-context-carrier");
        if (mutation == "bearer") request.Headers.TryAddWithoutValidation("authorization", "Bearer unexpected");
        using var response = await invoker.SendAsync(request, CancellationToken.None);
        Assert.True(handler.Misses.Count != 0 || handler.HeaderProblems.Count != 0);
    }

    [Fact]
    public async Task ExplicitNullDoesNotMatchAnAbsentBodyRecording()
    {
        const string id = "route-miss.route-not-found";
        using var handler = new WireReplayHandler(WireCorpus.Vendored, new[] { id }, null);
        using var invoker = new HttpMessageInvoker(handler);
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://replay.invalid/v1/no-such-route") { Content = new StringContent("null") };
        using var response = await invoker.SendAsync(request, CancellationToken.None);
        Assert.NotEmpty(handler.Misses);
        Assert.Equal(new[] { id }, handler.Unemitted);
    }

    [Fact]
    public void DuplicateAndUnknownIdsFailBeforeServing()
    {
        Assert.Throws<ArgumentException>(() => new WireReplayHandler(WireCorpus.Vendored, new[] { "health.ok", "health.ok" }, null));
        Assert.Throws<ArgumentException>(() => new WireReplayHandler(WireCorpus.Vendored, new[] { "not-recorded" }, null));
    }
    [Theory]
    [InlineData("digest")]
    [InlineData("count")]
    [InlineData("divergence")]
    [InlineData("version")]
    [InlineData("operations")]
    [InlineData("empty")]
    public void StaleProvenanceCannotLoad(string mutation)
    {
        var directory = Path.Combine(Path.GetTempPath(), "ezc-wire-control-" + Guid.NewGuid().ToString("N"));
        var wire = Path.Combine(directory, "wire", "v1");
        Directory.CreateDirectory(wire);
        try
        {
            var yaml = Encoding.UTF8.GetBytes("fixture openapi");
            File.WriteAllBytes(Path.Combine(directory, "openapi.yaml"), yaml);
            var corpus = JsonNode.Parse("""{"corpusVersion":"v1","cases":[{"id":"one","operationId":"fixture","auth":{"credential":"none"},"request":{"method":"GET","path":"/test","headers":{}},"response":{"status":200,"body":{},"headers":{}}}]}""")!;
            var manifest = JsonNode.Parse("""{"corpusVersion":"v1","spec":{},"coverage":{"cases":1,"operationsCovered":1},"contractDivergences":[]}""")!;
            manifest["spec"]!["digest"] = "sha256:" + Convert.ToHexString(SHA256.HashData(yaml)).ToLowerInvariant();
            switch (mutation)
            {
                case "digest": manifest["spec"]!["digest"] = "sha256:changed"; break;
                case "count": manifest["coverage"]!["cases"] = 2; break;
                case "divergence": manifest["contractDivergences"] = new JsonArray(new JsonObject { ["unresolved"] = true }); break;
                case "version": manifest["corpusVersion"] = "v2"; break;
                case "operations": manifest["coverage"]!["operationsCovered"] = 2; break;
                case "empty": corpus["cases"] = new JsonArray(); manifest["coverage"]!["cases"] = 0; manifest["coverage"]!["operationsCovered"] = 0; break;
            }
            File.WriteAllText(Path.Combine(wire, "corpus.json"), corpus.ToJsonString());
            File.WriteAllText(Path.Combine(wire, "manifest.json"), manifest.ToJsonString());
            Assert.Throws<InvalidDataException>(() => WireCorpus.Load(Path.Combine(wire, "corpus.json")));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Theory]
    [InlineData("payload")]
    [InlineData("status")]
    [InlineData("header")]
    public void ChangedReceivedAnswerFailsTheFullGate(string mutation)
    {
        var recorded = WireCorpus.Vendored.ById["health.ok"];
        var body = recorded.ResponseBody!.DeepClone();
        var status = recorded.Status;
        var headers = new Dictionary<string, string>(recorded.ResponseHeaders, StringComparer.OrdinalIgnoreCase);
        switch (mutation)
        {
            case "payload": body["engineVersion"] = "changed"; break;
            case "status": status = 503; break;
            case "header": headers["content-type"] = "text/plain"; break;
        }
        var gate = typeof(WireCorpusReplayTests).GetMethod("VerifyReceived", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(gate);
        var observedType = typeof(WireCorpusReplayTests).Assembly.GetType("EzClassify.Tests.WireObservedResponse");
        Assert.NotNull(observedType);
        var observed = Activator.CreateInstance(observedType, status, body.ToJsonString(), headers);
        Assert.Throws<TargetInvocationException>(() => gate.Invoke(null, new object?[] { recorded, observed }));
    }

    [Fact]
    public async Task ConsumedBindingRecordingDoesNotBecomeAnUnlimitedProbe()
    {
        const string id = "ctx-describe.ok";
        using var handler = new WireReplayHandler(WireCorpus.Vendored, new[] { id }, ("ez-full", "1.0.0"));
        using var invoker = new HttpMessageInvoker(handler);
        for (var i = 0; i < 2; i++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "http://replay.invalid/v1/ctx/ez-full/1.0.0");
            request.Headers.TryAddWithoutValidation("x-ezclassify-context-token", "fixture-context-carrier");
            using var response = await invoker.SendAsync(request, CancellationToken.None);
        }
        Assert.NotEmpty(handler.Misses);
    }


    [Theory]
    [InlineData("{\"n\":1}", "POST /numeric\n{\"n\":1}")]
    [InlineData("{\"n\":1.0}", "POST /numeric\n{\"n\":1}")]
    [InlineData("{\"n\":1e0}", "POST /numeric\n{\"n\":1}")]
    [InlineData("{\"n\":-0}", "POST /numeric\n{\"n\":0}")]
    [InlineData("{\"n\":-0.0}", "POST /numeric\n{\"n\":0}")]
    [InlineData("{\"n\":0e100}", "POST /numeric\n{\"n\":0}")]
    [InlineData("{\"n\":1e-7}", "POST /numeric\n{\"n\":1e-7}")]
    [InlineData("{\"n\":1e-6}", "POST /numeric\n{\"n\":0.000001}")]
    [InlineData("{\"n\":1e-5}", "POST /numeric\n{\"n\":0.00001}")]
    [InlineData("{\"n\":0.0000001}", "POST /numeric\n{\"n\":1e-7}")]
    [InlineData("{\"n\":1e20}", "POST /numeric\n{\"n\":100000000000000000000}")]
    [InlineData("{\"n\":1e21}", "POST /numeric\n{\"n\":1e+21}")]
    [InlineData("{\"n\":1e22}", "POST /numeric\n{\"n\":1e+22}")]
    [InlineData("{\"n\":1000000000000000128}", "POST /numeric\n{\"n\":1000000000000000100}")]
    [InlineData("{\"n\":9007199254740991}", "POST /numeric\n{\"n\":9007199254740991}")]
    [InlineData("{\"n\":9007199254740992}", "POST /numeric\n{\"n\":9007199254740992}")]
    [InlineData("{\"n\":9007199254740993}", "POST /numeric\n{\"n\":9007199254740992}")]
    [InlineData("{\"n\":1.2345678901234567}", "POST /numeric\n{\"n\":1.2345678901234567}")]
    [InlineData("{\"n\":1.23456789012345678}", "POST /numeric\n{\"n\":1.2345678901234567}")]
    [InlineData("{\"n\":1.0000000000000001}", "POST /numeric\n{\"n\":1}")]
    [InlineData("{\"n\":1.0000000000000002}", "POST /numeric\n{\"n\":1.0000000000000002}")]
    [InlineData("{\"n\":5e-324}", "POST /numeric\n{\"n\":5e-324}")]
    [InlineData("{\"n\":1e-324}", "POST /numeric\n{\"n\":0}")]
    [InlineData("{\"n\":2.2250738585072014e-308}", "POST /numeric\n{\"n\":2.2250738585072014e-308}")]
    [InlineData("{\"n\":1.7976931348623157e308}", "POST /numeric\n{\"n\":1.7976931348623157e+308}")]
    [InlineData("{\"n\":1e309}", "POST /numeric\n{\"n\":null}")]
    [InlineData("{\"n\":-1e309}", "POST /numeric\n{\"n\":null}")]
    [InlineData("{\"n\":1.2345678901234567e20}", "POST /numeric\n{\"n\":123456789012345670000}")]
    [InlineData("{\"n\":1.2345678901234567e21}", "POST /numeric\n{\"n\":1.2345678901234568e+21}")]
    [InlineData("{\"n\":-0.000001}", "POST /numeric\n{\"n\":-0.000001}")]
    [InlineData("{\"n\":-0.0000001}", "POST /numeric\n{\"n\":-1e-7}")]
    [InlineData("{\"n\":333333333.33333329}", "POST /numeric\n{\"n\":333333333.3333333}")]
    [InlineData("{\"n\":4.50}", "POST /numeric\n{\"n\":4.5}")]
    [InlineData("{\"n\":2e+3}", "POST /numeric\n{\"n\":2000}")]
    public void NumericBodyMatchesActualOwnerNumberVectors(string raw, string expected) =>
        Assert.Equal(expected, Key("POST", "/numeric", JsonNode.Parse(raw), true));

    [Theory]
    [InlineData("1", false)]
    [InlineData("1", true)]
    [InlineData("1.0", false)]
    [InlineData("1.0", true)]
    [InlineData("1e0", false)]
    [InlineData("1e0", true)]
    public async Task EquivalentNumbersConsumeOneFiniteRecording(string raw, bool actualHttp)
    {
        var seed = WireCorpus.Vendored.ById["ctx-scheme-process.ok"];
        var recorded = seed with { Target = "/numeric", RequestBody = JsonNode.Parse("{\"n\":1}"), Credential = "none", RequestHeaders = new Dictionary<string, string> { ["content-type"] = "application/json" } };
        var corpus = Assert.IsType<WireCorpus>(Activator.CreateInstance(typeof(WireCorpus), BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null, args: new object?[] { new[] { recorded } }, culture: null));
        using var handler = new WireReplayHandler(corpus, new[] { recorded.Id }, null);
        await using var server = actualHttp ? WireLoopbackServer.Start(handler) : null;
        using var invoker = actualHttp ? new HttpMessageInvoker(new HttpClientHandler()) : new HttpMessageInvoker(handler);
        using var request = new HttpRequestMessage(HttpMethod.Post, (server?.Url ?? "http://replay.invalid/") + "numeric")
        { Content = new StringContent("{\"n\":" + raw + "}", Encoding.UTF8, "application/json") };
        using var response = await invoker.SendAsync(request, CancellationToken.None);
        Assert.Equal(recorded.Status, (int)response.StatusCode);
        Assert.Empty(handler.Misses);
        Assert.Empty(handler.Unemitted);
        Assert.Empty(handler.HeaderProblems);
    }

    [Fact]
    public void NumericAliasesDoNotMatchChangedValuesTypesOrArrayOrder()
    {
        var one = Key("POST", "/numeric", JsonNode.Parse("{\"n\":1}"), true);
        Assert.NotEqual(one, Key("POST", "/numeric", JsonNode.Parse("{\"n\":2}"), true));
        Assert.NotEqual(one, Key("POST", "/numeric", JsonNode.Parse("{\"n\":\"1\"}"), true));
        Assert.NotEqual(one, Key("POST", "/numeric", JsonNode.Parse("{\"n\":true}"), true));
        Assert.NotEqual(Key("POST", "/numeric", JsonNode.Parse("[1,2]"), true), Key("POST", "/numeric", JsonNode.Parse("[2,1]"), true));
        Assert.Equal(Key("POST", "/numeric", JsonNode.Parse("{\"outer\":[{\"n\":1}]}"), true), Key("POST", "/numeric", JsonNode.Parse("{\"outer\":[{\"n\":1e0}]}"), true));
    }
}
