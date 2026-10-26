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
/// The Context description an integrator reads to record which component
/// versions a decision was made against. It is connection state: read once and
/// held, shared by racing callers, untouched by ClearCache, and dropped only when
/// the composition moves.
/// </summary>
public class DescribeContextTests
{
    private const string Description = """
        {"id":"c","version":"1.0.0","description":"Demo","scheme":{"id":"s","version":"latest","resolvedVersion":"1.0.0","layers":[{"id":"l","version":"2.0.0","resolvedVersion":"2.0.0"}]},
         "schemes":[{"selector":"main","id":"s","version":"1.0.0","roles":["primary","crosswalk-source"]},{"selector":"t","id":"t","version":"3.1.0","roles":["crosswalk-target"]}],
         "guides":[{"id":"g","version":"latest","resolvedVersion":"1.4.0"}],
         "access":{"id":"a","version":"1.0.0","resolvedVersion":"1.0.0"},
         "crosswalks":[{"id":"x","version":"latest","resolvedVersion":"1.1.0","layers":[{"id":"xl","version":"1.0.0","resolvedVersion":"1.0.0"}]}]}
        """;

    private sealed class DescribeHandler : HttpMessageHandler
    {
        private readonly Func<int, HttpResponseMessage> _reply;
        private TaskCompletionSource<bool>? _hold;
        internal int Requests;
        internal readonly List<(string Path, string? ContextToken)> Seen = new();

        internal DescribeHandler(Func<int, HttpResponseMessage> reply) => _reply = reply;

        internal TaskCompletionSource<bool> Hold() => _hold = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var count = Interlocked.Increment(ref Requests);
            lock (Seen)
                Seen.Add((request.RequestUri!.AbsolutePath,
                    request.Headers.TryGetValues("x-ezclassify-context-token", out var tokens) ? tokens.Single() : null));
            // The first request is the binding probe; later ones are description reads.
            if (count > 1 && _hold is { } hold) await hold.Task.WaitAsync(cancellationToken);
            return _reply(count);
        }
    }

    private static HttpResponseMessage Reply(string body, string version = "1.0.0")
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        response.Headers.TryAddWithoutValidation("X-EzClassify-Resolved-Version", version);
        return response;
    }

    private static async Task<(EzClassifyClient Client, HttpClient Http)> ConnectAsync(DescribeHandler handler, string? version = "1.0.0")
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:8080") };
        return (await EzClassifyClient.ConnectAsync(http, "c", version), http);
    }

    [Fact]
    public async Task ReadsTheCompositionWithDeclaredAndResolvedVersions()
    {
        var handler = new DescribeHandler(_ => Reply(Description));
        var (client, http) = await ConnectAsync(handler);
        using var _ = http;
        using var __ = client;

        var description = await client.DescribeContextAsync();

        Assert.Equal("c", description.Id);
        Assert.Equal("1.0.0", description.Version);
        Assert.Equal("Demo", description.Description);
        Assert.Equal("latest", description.Scheme.Version);
        Assert.Equal("1.0.0", description.Scheme.ResolvedVersion);
        Assert.Equal("l", Assert.Single(description.Scheme.Layers).Id);
        var guide = Assert.Single(description.Guides);
        Assert.Equal(("latest", "1.4.0"), (guide.Version, guide.ResolvedVersion));
        Assert.Equal("a", description.Access!.Id);
        var crosswalk = Assert.Single(description.Crosswalks);
        Assert.Equal("1.1.0", crosswalk.ResolvedVersion);
        Assert.Equal("xl", Assert.Single(crosswalk.Layers).Id);
        Assert.Equal(new[] { "main", "t" }, description.Schemes.Select(s => s.Selector));
        Assert.Equal(new[] { ContextSchemeRole.Primary, ContextSchemeRole.CrosswalkSource }, description.Schemes[0].Roles);
    }

    [Fact]
    public async Task OmittedCollectionsReadAsEmptyAndNoAccessPolicyReadsAsNull()
    {
        var handler = new DescribeHandler(_ => Reply("""{"id":"c","version":"1.0.0","scheme":{"id":"s","version":"1.0.0","resolvedVersion":"1.0.0"},"schemes":[{"selector":"s","id":"s","version":"1.0.0","roles":["primary"]}]}"""));
        var (client, http) = await ConnectAsync(handler);
        using var _ = http;
        using var __ = client;

        var description = await client.DescribeContextAsync();

        Assert.Empty(description.Guides);
        Assert.Empty(description.Crosswalks);
        Assert.Empty(description.Scheme.Layers);
        Assert.Null(description.Access);
        Assert.Null(description.Description);
    }

    [Fact]
    public async Task ARoleANewerServerAddsKeepsItsToken()
    {
        var handler = new DescribeHandler(_ => Reply("""{"id":"c","version":"1.0.0","scheme":{"id":"s","version":"1.0.0","resolvedVersion":"1.0.0"},"schemes":[{"selector":"s","id":"s","version":"1.0.0","roles":["primary","mirror"]}]}"""));
        var (client, http) = await ConnectAsync(handler);
        using var _ = http;
        using var __ = client;

        var roles = (await client.DescribeContextAsync()).Schemes[0].Roles;

        Assert.False(roles[1].IsKnown);
        Assert.Equal("mirror", roles[1].ToWire());
    }

    [Theory]
    [InlineData("""{"version":"1.0.0","scheme":{"id":"s","version":"1.0.0","resolvedVersion":"1.0.0"},"schemes":[]}""")]
    [InlineData("""{"id":"c","version":"1.0.0","schemes":[]}""")]
    [InlineData("""{"id":"c","version":"1.0.0","scheme":{"id":"s","version":"1.0.0","resolvedVersion":"1.0.0"}}""")]
    [InlineData("""{"id":"c","version":"1.0.0","scheme":{"id":"s","version":"1.0.0"},"schemes":[]}""")]
    [InlineData("""{"id":"c","version":"1.0.0","scheme":{"id":"s","version":"1.0.0","resolvedVersion":"1.0.0"},"schemes":[{"selector":"s","id":"s","version":"1.0.0","roles":[]}]}""")]
    [InlineData("""{"id":"c","version":"1.0.0","scheme":{"id":"s","version":"1.0.0","resolvedVersion":"1.0.0"},"schemes":[{"id":"s","version":"1.0.0","roles":["primary"]}]}""")]
    public void AnIncompleteDescriptionIsRefusedNotPatchedUp(string json) =>
        Assert.Throws<JsonException>(() => ContextResultParser.ParseContextDescription(json));

    [Fact]
    public async Task IsReadOnceAndHeldAcrossCallsAndClearCache()
    {
        var handler = new DescribeHandler(_ => Reply(Description));
        var (client, http) = await ConnectAsync(handler);
        using var _ = http;
        using var __ = client;

        var first = await client.DescribeContextAsync();
        client.ClearCache();
        var second = await client.DescribeContextAsync();

        Assert.Same(first, second);
        // The connect probe and the one description read.
        Assert.Equal(2, handler.Requests);
    }

    [Fact]
    public async Task RacingCallersShareOneRequestAndOneCancelsAlone()
    {
        var handler = new DescribeHandler(_ => Reply(Description));
        var (client, http) = await ConnectAsync(handler);
        using var _ = http;
        using var __ = client;
        var hold = handler.Hold();
        using var cancel = new CancellationTokenSource();

        var leaving = client.DescribeContextAsync(cancel.Token);
        var staying = client.DescribeContextAsync();
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => leaving);
        hold.SetResult(true);

        Assert.Equal("c", (await staying).Id);
        Assert.Equal(2, handler.Requests);
    }

    [Fact]
    public async Task AFailedOrMalformedReadIsNotHeld()
    {
        var handler = new DescribeHandler(count => count == 2 ? Reply("{}") : Reply(Description));
        var (client, http) = await ConnectAsync(handler);
        using var _ = http;
        using var __ = client;

        await Assert.ThrowsAsync<JsonException>(() => client.DescribeContextAsync());
        Assert.Equal("c", (await client.DescribeContextAsync()).Id);
        Assert.Equal(3, handler.Requests);
    }

    [Fact]
    public async Task ALatestBindingDropsTheDescriptionWhenTheCompositionMoves()
    {
        string resolved = "1.0.0";
        var handler = new DescribeHandler(_ => Reply(Description.Replace("\"version\":\"1.0.0\",\"description\"", $"\"version\":\"{resolved}\",\"description\""), resolved));
        var (client, http) = await ConnectAsync(handler, version: null);
        using var _ = http;
        using var __ = client;
        await client.DescribeContextAsync();

        resolved = "1.0.1";
        // Any response naming the new version tells the client the composition moved.
        await Assert.ThrowsAnyAsync<Exception>(() => client.Scheme!.SummaryAsync());
        var after = await client.DescribeContextAsync();

        Assert.Equal("1.0.1", after.Version);
    }

    // An omitted version lets the server choose: the Context token's default
    // version, or the newest published. The runtime answers the latest path under
    // a token with the token's default, so the binding probe and the description
    // both ask for latest and the client refuses nothing.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task AnOmittedVersionUnderAContextTokenDescribesTheVersionTheServerChose(string? version)
    {
        var handler = new DescribeHandler(_ => Reply(Description));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:8080") };
        using var client = await EzClassifyClient.ConnectAsync(
            http, "c", version, new RestClientCredentials(accessToken: null, contextToken: "ctx-token"));

        var description = await client.DescribeContextAsync();

        Assert.Equal(("c", "1.0.0"), (description.Id, client.ResolvedVersion));
        Assert.Equal(new[] { ("/v1/ctx/c/latest", (string?)"ctx-token"), ("/v1/ctx/c/latest", "ctx-token") }, handler.Seen);
    }

    [Fact]
    public async Task TheRootDiscoveryClientHasNoContextToDescribe()
    {
        using var http = new HttpClient(new DescribeHandler(_ => Reply("{}"))) { BaseAddress = new Uri("http://localhost:8080") };
        using var root = new EzClassifyClient(http);

        await Assert.ThrowsAsync<InvalidOperationException>(() => root.DescribeContextAsync());
    }
}
