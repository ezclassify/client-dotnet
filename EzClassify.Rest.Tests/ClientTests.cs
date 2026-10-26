using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace EzClassify.Tests;

internal sealed class MockHttpHandler : HttpMessageHandler
{
    private readonly string _responseJson;
    private readonly HttpStatusCode _statusCode;

    public string? LastRequestBody { get; private set; }
    public string? LastRequestPath { get; private set; }
    public string? LastAuthorization { get; private set; }
    public string? LastContextToken { get; private set; }

    public MockHttpHandler(string responseJson, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        _responseJson = responseJson;
        _statusCode = statusCode;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        LastRequestPath = request.RequestUri?.PathAndQuery;
        LastAuthorization = request.Headers.Authorization?.ToString();
        LastContextToken = request.Headers.TryGetValues("x-ezclassify-context-token", out var values)
            ? string.Join(",", values)
            : null;
        if (request.Content is not null)
            LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);
        return new HttpResponseMessage(_statusCode)
        {
            Content = new StringContent(
                _responseJson,
                System.Text.Encoding.UTF8,
                "application/json"),
        };
    }
}

internal sealed class ConnectionFailHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken) =>
        throw new HttpRequestException("connection refused");
}

public class EzClassifyRootClientTests
{
    [Fact]
    public async Task ListContextsAsync_ParsesDiscoveryResponse()
    {
        var handler = new MockHttpHandler("""{"contexts":["alpha","bravo"]}""");
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:8080/") };
        using var client = new EzClassifyClient(http);

        var contexts = await client.ListContextsAsync();

        Assert.Equal(new[] { "alpha", "bravo" }, contexts);
        Assert.Equal("/v1/ctx", handler.LastRequestPath);
    }

    [Fact]
    public async Task ListContextVersionsAsync_EncodesIdAndParsesVersions()
    {
        var handler = new MockHttpHandler("""{"id":"tenant a/context","versions":["1.0.0","1.1.0"]}""");
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:8080/") };
        using var client = new EzClassifyClient(http);

        var versions = await client.ListContextVersionsAsync("tenant a/context");

        Assert.Equal(new[] { "1.0.0", "1.1.0" }, versions);
        Assert.Equal("/v1/ctx/tenant%20a%2Fcontext", handler.LastRequestPath);
    }

    [Theory]
    [InlineData("{}", true, "$.contexts")]
    [InlineData("""{"contexts":"alpha"}""", true, "$.contexts")]
    [InlineData("""{"contexts":["alpha",null]}""", true, "$.contexts[1]")]
    [InlineData("""{"versions":["1.0.0"]}""", false, "$.id")]
    [InlineData("""{"id":"alpha"}""", false, "$.versions")]
    [InlineData("""{"id":"alpha","versions":[7]}""", false, "$.versions[0]")]
    public async Task ContextDiscovery_RefusesABodyTheContractDoesNotAllow(string response, bool listContexts, string path)
    {
        using var http = new HttpClient(new MockHttpHandler(response))
        { BaseAddress = new Uri("http://localhost:8080/") };
        using var client = new EzClassifyClient(http);

        var refusal = listContexts
            ? await Assert.ThrowsAsync<JsonException>(() => client.ListContextsAsync())
            : await Assert.ThrowsAsync<JsonException>(() => client.ListContextVersionsAsync("alpha"));
        Assert.Equal(path, refusal.Path);
    }

    [Fact]
    public async Task PingAsync_ParsesHealthResponse()
    {
        const string json =
            """{"status":"ok","engineVersion":"1.0.0","engineSha":"abc","gitTag":"v1.0.0","openapiVersion":"1.0.0","abiVersion":"v1"}""";
        using var http = new HttpClient(new MockHttpHandler(json))
        { BaseAddress = new Uri("http://localhost:8080/") };
        using var client = new EzClassifyClient(http);

        var result = await client.PingAsync();

        Assert.Equal("1.0.0", result.EngineVersion);
        Assert.Equal("abc", result.EngineSha);
    }

    [Fact]
    public async Task PingAsync_MapsHttpErrors()
    {
        using var http = new HttpClient(
            new MockHttpHandler(
                """{"code":"NOPE","detail":"missing"}""",
                HttpStatusCode.NotFound))
        { BaseAddress = new Uri("http://localhost:8080/") };
        using var client = new EzClassifyClient(http);

        var error = await Assert.ThrowsAsync<EzClassifyClientException>(() => client.PingAsync());

        Assert.Equal(404, error.StatusCode);
        Assert.Equal("NOPE", error.Code);
        Assert.Equal("missing", error.Detail);
    }

    [Fact]
    public async Task PingAsync_MapsConnectionErrors()
    {
        using var http = new HttpClient(new ConnectionFailHandler())
        { BaseAddress = new Uri("http://localhost:8080/") };
        using var client = new EzClassifyClient(http);

        await Assert.ThrowsAsync<EzClassifyClientException>(() => client.PingAsync());
    }

    [Fact]
    public async Task Credentials_KeepCallerIdentityAndContextAuthorizationIndependent()
    {
        var handler = new MockHttpHandler("""{"contexts":[]}""");
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:8080/") };
        using var client = new EzClassifyClient(
            http,
            new RestClientCredentials("caller-token", "context-token"));

        await client.ListContextsAsync();

        Assert.Equal("Bearer caller-token", handler.LastAuthorization);
        Assert.Equal("context-token", handler.LastContextToken);
    }

    [Fact]
    public async Task Credentials_DoNotSendContextAuthorizationToHealthRoute()
    {
        var handler = new MockHttpHandler(
            """{"status":"ok","engineVersion":"1.0.0","engineSha":"abc","gitTag":"v1.0.0","openapiVersion":"1.0.0","abiVersion":"v1"}""");
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:8080/") };
        using var client = new EzClassifyClient(
            http,
            new RestClientCredentials("caller-token", "context-token"));

        await client.PingAsync();

        Assert.Equal("Bearer caller-token", handler.LastAuthorization);
        Assert.Null(handler.LastContextToken);
    }
}
