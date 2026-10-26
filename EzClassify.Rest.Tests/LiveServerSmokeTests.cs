using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace EzClassify.Tests;

/// <summary>
/// Live-server smoke test: boots nothing itself, but when EZC_SMOKE_URL points at
/// a running @ezclassify/rest-api this drives one real round-trip through the
/// published <see cref="EzClassifyClient"/> wire path (a GET /v1/health ping and
/// a Context-bound process request) and asserts the SDK speaks the server's actual
/// prefix and version.
///
/// The conformance and replayer-contract suites replay recorded fixtures and are
/// self-consistent by construction, so a prefix or version regression can pass
/// them while 404-ing against a real server.
/// This test closes that gap: it is the one case that fails loudly if the client
/// and the live wire contract disagree.
///
/// It carries the <c>Live</c> category, which the default offline <c>make test</c>
/// excludes. Run it with <c>make smoke URL=&lt;server-url&gt;</c> against a running
/// REST API that serves the Context it names; it fails when EZC_SMOKE_URL is
/// unset rather than passing without having run.
/// </summary>
public class LiveServerSmokeTests
{
    private static string ContextId =>
        Environment.GetEnvironmentVariable("EZC_SMOKE_CONTEXT_ID") ?? "cedar-valley-protection";
    private static string ProcessInput =>
        Environment.GetEnvironmentVariable("EZC_SMOKE_INPUT") ?? "confidential";
    private static string ProcessExpected =>
        Environment.GetEnvironmentVariable("EZC_SMOKE_EXPECTED") ?? "CONFIDENTIAL";

    private readonly ITestOutputHelper _output;

    public LiveServerSmokeTests(ITestOutputHelper output) => _output = output;

    private static string? SmokeUrl =>
        Environment.GetEnvironmentVariable("EZC_SMOKE_URL")?.TrimEnd('/');

    [Fact]
    [Trait("Category", "Live")]
    public async Task LiveServer_PingAndProcess_SpeakV1()
    {
        var url = SmokeUrl;
        if (string.IsNullOrEmpty(url))
        {
            Assert.Fail(
                "EZC_SMOKE_URL is not set. Run `make smoke URL=<server-url>` against a " +
                "running REST API.");
        }

        var credentials = new RestClientCredentials(
            Environment.GetEnvironmentVariable("EZC_SMOKE_ACCESS_TOKEN"),
            Environment.GetEnvironmentVariable("EZC_SMOKE_CONTEXT_TOKEN"));
        using var client = new EzClassifyClient(url, credentials);

        // GET /v1/health via the client.
        var ping = await client.PingAsync();
        Assert.False(string.IsNullOrEmpty(ping.EngineVersion));
        Assert.Equal(
            client.EngineSpecVersion.Split('.')[0],
            ping.EngineVersion.Split('.')[0]);
        _output.WriteLine($"live server wire version: {ping.EngineVersion}");

        // Context-bound round-trip: GET /v1/ctx/{id}/latest resolves a version,
        // then process and access calls use only Context routes.
        using var ctx = await EzClassifyClient.ConnectAsync(url, ContextId, null, credentials);
        Assert.False(string.IsNullOrEmpty(ctx.ResolvedVersion));
        var result = await ctx.Scheme!.ProcessAsync(ProcessInput);
        Assert.True(result.Result.Valid);
        Assert.Equal(ProcessExpected, result.Result.Rendered);
        var recognition = Assert.IsType<RecognitionResult>(result.Result.Recognition);
        Assert.Equal(RecognitionStatus.Complete, recognition.Status);
        Assert.True(result.Result.Renders.Count >= 2);
        Assert.All(result.Result.Renders.Values, render => Assert.NotNull(render.Assessment));
        var refusal = await Assert.ThrowsAsync<EzClassifyClientException>(() =>
            ctx.Scheme.RenderAsync(
                result.Result.Assertions,
                "authority_block",
                RenderPreservation.Full));
        Assert.Equal(400, refusal.StatusCode);
        Assert.Equal("INVALID_INPUT", refusal.Code);
        _output.WriteLine(
            $"process('{ProcessInput}') -> {result.Result.Rendered} (valid={result.Result.Valid})");
    }
}
