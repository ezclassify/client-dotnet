using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace EzClassify.Tests;

/// <summary>
/// The wait a rate-limited caller is told to observe.
///
/// The contract answers 429 from ahead of every route, with the wait in
/// seconds in a <c>Retry-After</c> header and in the body's <c>retryAfter</c>.
/// The client never waits or retries by itself, so the one thing it owes the
/// caller is that wait, on the exception, from whichever place the server put
/// it. The stub answers are built from what the vendored contract declares for
/// that response, so a contract that moves the header or the field fails here.
/// </summary>
public class RateLimitContractTests
{
    private static readonly OpenApiRequestSchemas Spec = OpenApiRequestSchemas.Load();

    [Fact]
    public void EveryOperation_DeclaresA429_WhoseWaitIsInTheHeaderAndTheBody()
    {
        var without = Spec.OperationsWithoutResponse("429").Select(op => $"{op.Method.ToUpperInvariant()} {op.Path}").ToList();
        Assert.True(without.Count == 0, "These operations declare no 429: " + string.Join(", ", without));

        var response = Spec.ComponentResponse("TooManyRequests");
        Assert.NotNull(response["headers"]!["Retry-After"]);
        var body = Spec.ComponentSchema("RateLimitError");
        Assert.Contains("retryAfter", body["required"]!.AsArray().Select(name => name!.GetValue<string>()));
    }

    [Fact]
    public async Task ARateLimitedOperation_ReportsTheHeaderWait()
    {
        using var client = Client(RateLimited(headerSeconds: "42"));

        var error = await Assert.ThrowsAsync<EzClassifyClientException>(() => client.PingAsync());

        Assert.Equal(429, error.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(42), error.RetryAfter);
    }

    [Fact]
    public async Task ARateLimitedConnect_ReportsTheWaitToo()
    {
        using var http = new HttpClient(RateLimited(headerSeconds: "7")) { BaseAddress = new Uri("http://localhost:8080/") };

        var error = await Assert.ThrowsAsync<EzClassifyClientException>(
            () => EzClassifyClient.ConnectAsync(http, "acme-marking", "1.0.0"));

        Assert.Equal(TimeSpan.FromSeconds(7), error.RetryAfter);
    }

    [Fact]
    public async Task TheHeaderWinsOverTheBody()
    {
        using var client = Client(RateLimited(headerSeconds: "30", bodySeconds: 99));

        var error = await Assert.ThrowsAsync<EzClassifyClientException>(() => client.PingAsync());

        Assert.Equal(TimeSpan.FromSeconds(30), error.RetryAfter);
    }

    [Fact]
    public async Task WithoutTheHeader_TheBodyWaitIsReported()
    {
        using var client = Client(RateLimited(bodySeconds: 42));

        var error = await Assert.ThrowsAsync<EzClassifyClientException>(() => client.PingAsync());

        Assert.Equal(TimeSpan.FromSeconds(42), error.RetryAfter);
    }

    [Fact]
    public async Task AHeaderThatIsNeitherSecondsNorADate_FallsBackToTheBody()
    {
        var handler = RateLimited(bodySeconds: 42);
        handler.Configure = response => response.Headers.TryAddWithoutValidation("Retry-After", "soon");
        using var client = Client(handler);

        var error = await Assert.ThrowsAsync<EzClassifyClientException>(() => client.PingAsync());

        Assert.Equal(TimeSpan.FromSeconds(42), error.RetryAfter);
    }

    [Fact]
    public async Task AnHttpDateHeader_ReadsAsTheTimeLeft()
    {
        var handler = RateLimited();
        handler.Configure = response => response.Headers.RetryAfter = new RetryConditionHeaderValue(DateTimeOffset.UtcNow.AddSeconds(90));
        using var client = Client(handler);

        var error = await Assert.ThrowsAsync<EzClassifyClientException>(() => client.PingAsync());

        Assert.InRange(error.RetryAfter!.Value, TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(90));
    }

    [Fact]
    public async Task ADateAlreadyPast_ReadsAsNoWaitLeft()
    {
        var handler = RateLimited();
        handler.Configure = response => response.Headers.RetryAfter = new RetryConditionHeaderValue(DateTimeOffset.UtcNow.AddMinutes(-5));
        using var client = Client(handler);

        var error = await Assert.ThrowsAsync<EzClassifyClientException>(() => client.PingAsync());

        Assert.Equal(TimeSpan.Zero, error.RetryAfter);
    }

    [Theory]
    [InlineData("""{"error":"Too Many Requests","retryAfter":-3}""")]
    [InlineData("""{"error":"Too Many Requests","retryAfter":1e300}""")]
    [InlineData("""{"error":"Too Many Requests","retryAfter":"soon"}""")]
    [InlineData("""{"error":"Too Many Requests"}""")]
    [InlineData("not json")]
    public async Task ABodyThatNamesNoUsableWait_LeavesItNull(string body)
    {
        var handler = new StubHandler(HttpStatusCode.TooManyRequests, body);
        using var client = Client(handler);

        var error = await Assert.ThrowsAsync<EzClassifyClientException>(() => client.PingAsync());

        Assert.Equal(429, error.StatusCode);
        Assert.Null(error.RetryAfter);
    }

    [Fact]
    public async Task AnAnswerThatAsksForNoWait_LeavesItNull()
    {
        var handler = new StubHandler(
            HttpStatusCode.BadRequest, """{"error":"invalid_input","code":"INVALID_INPUT","detail":"query: verbose"}""");
        using var client = Client(handler);

        var error = await Assert.ThrowsAsync<EzClassifyClientException>(() => client.PingAsync());

        Assert.Equal(400, error.StatusCode);
        Assert.Equal("INVALID_INPUT", error.Code);
        Assert.Equal("query: verbose", error.Detail);
        Assert.Null(error.RetryAfter);
    }

    // The stub answers the way the contract says a rate-limited operation does:
    // the example body of the TooManyRequests response, with the header it names.
    private static StubHandler RateLimited(string? headerSeconds = null, double? bodySeconds = null)
    {
        var example = (JsonObject)Spec.ComponentResponse("TooManyRequests")["content"]!["application/json"]!["example"]!.DeepClone();
        Assert.True(example.ContainsKey("retryAfter"), "The contract's 429 example no longer carries retryAfter.");
        if (bodySeconds is null && headerSeconds is not null) example.Remove("retryAfter");
        else if (bodySeconds is { } seconds) example["retryAfter"] = seconds;
        var handler = new StubHandler(HttpStatusCode.TooManyRequests, example.ToJsonString());
        if (headerSeconds is not null)
            handler.Configure = response => response.Headers.RetryAfter =
                new RetryConditionHeaderValue(TimeSpan.FromSeconds(double.Parse(headerSeconds, System.Globalization.CultureInfo.InvariantCulture)));
        return handler;
    }

    private static EzClassifyClient Client(HttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:8080/") });

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body;

        public StubHandler(HttpStatusCode status, string body)
        {
            _status = status;
            _body = body;
        }

        public Action<HttpResponseMessage>? Configure { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(_status)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json"),
            };
            Configure?.Invoke(response);
            return Task.FromResult(response);
        }
    }
}
