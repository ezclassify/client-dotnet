using System.Net;
using Xunit;

namespace EzClassify.Tests;

public class ApplicationCredentialTests
{
    private const string Grant = "opaque.synthetic.application-grant";
    private const string Header = "X-EzClassify-Application-Grant";
    private static RestClientCredentials Application => new(null, null, Grant);

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(" leading")]
    [InlineData("trailing ")]
    [InlineData("tab\tvalue")]
    [InlineData("line\r\nvalue")]
    [InlineData("zero\0value")]
    [InlineData("delete\u007fvalue")]
    [InlineData("non-ascii\u00e9")]
    public void MalformedApplicationCredential_IsRefusedWithoutTransport(string value)
    {
        var error = Assert.Throws<ArgumentException>(() => new RestClientCredentials(null, null, value));
        Assert.Equal("applicationCredential", error.ParamName);
        if (value.Trim().Length > 0) Assert.DoesNotContain(value, error.Message);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void OrdinaryAndApplicationCredentials_AreMutuallyExclusive(bool caller, bool context)
    {
        Assert.Throws<ArgumentException>(() => new RestClientCredentials(caller ? "caller" : null, context ? "context" : null, Grant));
    }

    [Fact]
    public async Task OpaqueCredential_ReachesDiscoveryVersionsBindingAndOperations_ButNotHealth()
    {
        using var handler = new RecordingHandler();
        using var http = Transport(handler);
        using var root = new EzClassifyClient(http, Application);
        await root.PingAsync();
        await root.ListContextsAsync();
        await root.ListContextVersionsAsync("sample");
        using var bound = await EzClassifyClient.ConnectAsync(http, "sample", "1.0.0", Application);
        await bound.DescribeContextAsync();
        var normalized = await bound.Scheme!.NormalizeAsync("text");
        Assert.Equal("TEXT", normalized.Result.Result);

        Assert.Equal(6, handler.Requests.Count);
        Assert.Null(handler.Requests[0].Grant);
        Assert.All(handler.Requests.Skip(1), request => Assert.Equal(Grant, request.Grant));
        Assert.All(handler.Requests, request =>
        {
            Assert.Null(request.Authorization);
            Assert.Null(request.Context);
        });
        Assert.Equal(Grant, Application.ApplicationCredential);
    }

    [Theory]
    [InlineData("Authorization")]
    [InlineData("X-EzClassify-Context-Token")]
    [InlineData("x-ezclassify-application-grant")]
    public async Task ConflictingTransportDefaults_RefuseBeforeAnyRequest(string header)
    {
        using var handler = new RecordingHandler();
        using var http = Transport(handler);
        http.DefaultRequestHeaders.TryAddWithoutValidation(header, "conflict");
        var rootError = Assert.Throws<ArgumentException>(() => new EzClassifyClient(http, Application));
        var connectError = await Assert.ThrowsAsync<ArgumentException>(() => EzClassifyClient.ConnectAsync(http, "sample", "1.0.0", Application));
        Assert.Equal("httpClient", rootError.ParamName);
        Assert.Equal("httpClient", connectError.ParamName);
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData(".")]
    [InlineData("..")]
    public async Task DotSegments_CannotNormalizeAContextRoute(string segment)
    {
        using var handler = new RecordingHandler();
        using var http = Transport(handler);
        using var root = new EzClassifyClient(http, Application);
        await Assert.ThrowsAsync<ArgumentException>(() => root.ListContextVersionsAsync(segment));
        await Assert.ThrowsAsync<ArgumentException>(() => EzClassifyClient.ConnectAsync(http, segment, "1.0.0", Application));
        await Assert.ThrowsAsync<ArgumentException>(() => EzClassifyClient.ConnectAsync(http, "sample", segment, Application));
        await Assert.ThrowsAsync<ArgumentException>(() => EzClassifyClient.ConnectAsync("http://127.0.0.1:1", segment, "1.0.0", Application));
        await Assert.ThrowsAsync<ArgumentException>(() => EzClassifyClient.ConnectAsync("http://127.0.0.1:1", "sample", segment, Application));
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ForbiddenPost_IsNotRefreshedOrReplayed(bool owned)
    {
        using var handler = new RecordingHandler(forbidOperations: true);
        await using var server = WireLoopbackServer.Start(handler);
        using var http = owned ? null : new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false })
        {
            BaseAddress = new Uri(server.Url),
        };
        using var bound = owned
            ? await EzClassifyClient.ConnectAsync(server.Url, "sample", "1.0.0", Application)
            : await EzClassifyClient.ConnectAsync(http!, "sample", "1.0.0", Application);

        var error = await Assert.ThrowsAsync<EzClassifyClientException>(() => bound.Scheme!.NormalizeAsync("text"));
        Assert.Equal(403, error.StatusCode);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal("POST", handler.Requests[1].Method);
        Assert.All(handler.Requests, request => Assert.Equal(Grant, request.Grant));
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task OwnedTransports_DoNotFollowSameOrCrossOriginRedirects(bool connect, bool sameOrigin)
    {
        using var targetHandler = new RecordingHandler();
        await using var target = WireLoopbackServer.Start(targetHandler);
        using var sourceHandler = new RecordingHandler();
        await using var source = WireLoopbackServer.Start(sourceHandler);
        sourceHandler.RedirectTo = new Uri((sameOrigin ? source.Url : target.Url) + "v1/ctx/redirected/1.0.0");

        if (connect)
            await Assert.ThrowsAsync<EzClassifyClientException>(() => EzClassifyClient.ConnectAsync(source.Url, "sample", "1.0.0", Application));
        else
        {
            using var root = new EzClassifyClient(source.Url, Application);
            await Assert.ThrowsAsync<EzClassifyClientException>(() => root.ListContextsAsync());
        }

        Assert.Single(sourceHandler.Requests);
        Assert.Equal(Grant, sourceHandler.Requests[0].Grant);
        Assert.Empty(targetHandler.Requests);
    }

    [Fact]
    public async Task OrdinaryOwnedTransport_RetainsRedirectBehavior()
    {
        using var targetHandler = new RecordingHandler();
        await using var target = WireLoopbackServer.Start(targetHandler);
        using var sourceHandler = new RecordingHandler();
        await using var source = WireLoopbackServer.Start(sourceHandler);
        sourceHandler.RedirectTo = new Uri(target.Url + "v1/ctx");
        using var root = new EzClassifyClient(source.Url);

        Assert.Equal(new[] { "sample" }, await root.ListContextsAsync());
        Assert.Single(sourceHandler.Requests);
        Assert.Single(targetHandler.Requests);
        Assert.Null(targetHandler.Requests[0].Grant);
    }

    private static HttpClient Transport(HttpMessageHandler handler) => new(handler, disposeHandler: false)
    {
        BaseAddress = new Uri("http://localhost:8080/"),
    };

    private sealed record Observed(string Method, string? Grant, string? Authorization, string? Context);

    private sealed class RecordingHandler(bool forbidOperations = false) : HttpMessageHandler
    {
        public List<Observed> Requests { get; } = new();
        public Uri? RedirectTo { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            static string? Value(HttpRequestMessage message, string name) =>
                message.Headers.TryGetValues(name, out var values) ? string.Join(",", values) : null;
            Requests.Add(new Observed(request.Method.Method, Value(request, Header), Value(request, "Authorization"), Value(request, "X-EzClassify-Context-Token")));
            if (RedirectTo is not null)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Redirect) { Headers = { Location = RedirectTo }, Content = new StringContent("{}") });
            if (forbidOperations && request.Method == HttpMethod.Post)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent("""{"code":"ACCESS_DENIED","detail":"refused"}""") });
            var path = request.RequestUri?.AbsolutePath;
            var body = path switch
            {
                "/v1/health" => """{"status":"ok","engineVersion":"1.0.0","engineSha":"abc","gitTag":"v1.0.0","openapiVersion":"1.0.0","abiVersion":"v1"}""",
                "/v1/ctx" => """{"contexts":["sample"]}""",
                "/v1/ctx/sample" => """{"id":"sample","versions":["1.0.0"]}""",
                "/v1/ctx/sample/1.0.0/scheme/normalize" => """{"resolution":{"contextId":"sample","resolvedVersion":"1.0.0","artifacts":[],"sources":[]},"result":{"result":"TEXT"},"operationLimitations":[]}""",
                _ => ContextProbe.Body,
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }
}
