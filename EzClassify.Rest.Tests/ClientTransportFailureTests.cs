using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace EzClassify.Tests;

// A handler that never answers. HttpClient turns its own timeout into a
// cancellation, which is the shape the transport has to tell apart from a
// caller-requested cancellation.
internal sealed class NeverRespondingHandler : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
        throw new InvalidOperationException("unreachable");
    }
}

public class ClientTransportFailureTests
{
    [Fact]
    public async Task RequestTimeout_IsReportedAsClientException()
    {
        using var http = new HttpClient(new NeverRespondingHandler())
        {
            BaseAddress = new Uri("http://localhost:8080/"),
            Timeout = TimeSpan.FromMilliseconds(50),
        };
        using var client = new EzClassifyClient(http);

        var error = await Assert.ThrowsAsync<EzClassifyClientException>(() => client.PingAsync());

        Assert.Contains("timed out", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.IsType<TimeoutException>(error.InnerException);
    }

    [Fact]
    public async Task CallerCancellation_StaysCancellation()
    {
        using var http = new HttpClient(new NeverRespondingHandler())
        { BaseAddress = new Uri("http://localhost:8080/") };
        using var client = new EzClassifyClient(http);
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.PingAsync(cts.Token));
    }

    [Fact]
    public async Task ErrorMessage_CarriesServerCodeNotTheWholeBody()
    {
        var body = """{"code":"ACCESS_DENIED","detail":"subject attributes insufficient","echoedInput":"the entire submitted document text"}""";
        using var http = new HttpClient(new MockHttpHandler(body, HttpStatusCode.Forbidden))
        { BaseAddress = new Uri("http://localhost:8080/") };
        using var client = new EzClassifyClient(http);

        var error = await Assert.ThrowsAsync<EzClassifyClientException>(() => client.PingAsync());

        Assert.Contains("ACCESS_DENIED", error.Message);
        Assert.DoesNotContain("the entire submitted document text", error.Message);
        Assert.Equal(body, error.ResponseBody);
    }

    [Fact]
    public async Task ErrorMessage_FallsBackToATruncatedBodyWhenTheServerSendsNoCode()
    {
        var body = new string('x', 500);
        using var http = new HttpClient(new MockHttpHandler(body, HttpStatusCode.BadGateway))
        { BaseAddress = new Uri("http://localhost:8080/") };
        using var client = new EzClassifyClient(http);

        var error = await Assert.ThrowsAsync<EzClassifyClientException>(() => client.PingAsync());

        Assert.Contains("truncated", error.Message);
        Assert.True(error.Message.Length < body.Length);
        Assert.Equal(body, error.ResponseBody);
    }

    [Fact]
    public async Task ConnectAsync_RejectsAnEmptyContextId()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => EzClassifyClient.ConnectAsync("http://localhost:8080", "", null));
    }
}

public class ClientBaseUrlGuardTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_RejectsAnUnusableBaseUrl(string baseUrl)
    {
        var error = Assert.Throws<ArgumentException>(() => new EzClassifyClient(baseUrl));
        Assert.Equal("baseUrl", error.ParamName);
    }

    [Fact]
    public void Constructor_RejectsANullBaseUrl()
    {
        Assert.Throws<ArgumentException>(() => new EzClassifyClient((string)null!));
    }

    [Fact]
    public async Task ConnectAsync_RejectsAnUnusableBaseUrl()
    {
        var error = await Assert.ThrowsAsync<ArgumentException>(
            () => EzClassifyClient.ConnectAsync("", "ctx", null));
        Assert.Equal("baseUrl", error.ParamName);
    }
}
