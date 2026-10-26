using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;

namespace EzClassify.Tests;

public class ResourceErrorContractTests
{
    [Theory]
    [InlineData("""{"error":"concept_not_found","code":"NOT_FOUND","detail":"conceptId: NOPE"}""", false)]
    [InlineData("""{"error":"not_found"}""", true)]
    public async Task ResourceAndAuthentication404KeepTheirPayloadAndCacheMeaning(string body, bool clearsCache)
    {
        var (client, handler) = await CtxClientFactory.ConnectAsync(new()
        {
            ["/scheme/suggest"] = (HttpStatusCode.OK, """{"suggestions":[],"warnings":[]}"""),
            ["/scheme/describe-concept"] = (HttpStatusCode.NotFound, body),
        });
        using (client)
        {
            var scheme = Assert.IsType<ContextSchemeOps>(client.Scheme);
            await scheme.SuggestAsync("");
            var error = await Assert.ThrowsAsync<EzClassifyClientException>(() => scheme.DescribeConceptAsync("NOPE", "level"));
            Assert.Equal(404, error.StatusCode);
            Assert.Equal(body, error.ResponseBody);
            Assert.Equal(clearsCache ? null : "NOT_FOUND", error.Code);
            Assert.Equal(clearsCache ? null : "conceptId: NOPE", error.Detail);
            var before = handler.Requests.Count;
            await scheme.SuggestAsync("");
            Assert.Equal(before + (clearsCache ? 1 : 0), handler.Requests.Count);
        }
    }

    [Fact]
    public async Task ContextBinding404KeepsContextIdentity()
    {
        const string body = """{"error":"context_not_found","code":"NOT_FOUND","detail":"contextId: missing"}""";
        var handler = new CtxMockHandler(new() { ["/ctx/"] = (HttpStatusCode.NotFound, body) });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://replay.invalid/") };
        var error = await Assert.ThrowsAsync<EzClassifyClientException>(() => EzClassifyClient.ConnectAsync(http, "missing", "1.0.0"));
        Assert.Equal(404, error.StatusCode);
        Assert.Equal(body, error.ResponseBody);
        Assert.Equal("NOT_FOUND", error.Code);
        Assert.Equal("contextId: missing", error.Detail);
        Assert.Single(handler.Requests);
    }
}
