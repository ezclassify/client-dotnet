using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace EzClassify.Tests;

public class SuggestInputContractTests
{
    private static MethodInfo FactsSuggest()
    {
        var method = typeof(ContextSchemeOps).GetMethods().SingleOrDefault(m =>
            m.Name == nameof(ContextSchemeOps.SuggestAsync)
            && m.GetParameters()[0].ParameterType == typeof(IEnumerable<Assertion>));
        Assert.NotNull(method);
        return method;
    }

    // Reflection locates the published overload; invocation still executes the
    // actual public adapter.
    private static Task<ContextSchemeSuggestResult> SuggestFacts(ContextSchemeOps scheme, IEnumerable<Assertion>? facts,
        string? output = null, string? filter = null, string? surface = null, bool? includeGated = null) =>
        Assert.IsAssignableFrom<Task<ContextSchemeSuggestResult>>(FactsSuggest().Invoke(scheme,
            new object?[] { facts, output, filter, surface, includeGated, CancellationToken.None }));

    [Fact]
    public void SuggestOverloadsHaveExclusiveInputs()
    {
        var methods = typeof(ContextSchemeOps).GetMethods().Where(m => m.Name == nameof(ContextSchemeOps.SuggestAsync)).ToList();
        Assert.Equal(2, methods.Count);
        var text = Assert.Single(methods, m => m.GetParameters()[0].ParameterType == typeof(string));
        Assert.DoesNotContain(text.GetParameters(), p => p.Name == "assertions");
        Assert.Contains(text.GetParameters(), p => p.Name == "inputFormat");
        Assert.DoesNotContain(FactsSuggest().GetParameters(), p => p.Name == "inputFormat" || p.Name == "input");
    }

    [Fact]
    public async Task FactsSuggestDoesNotSynthesizeTextAndKeepsFalseOptionsAndCache()
    {
        var (client, handler) = await CtxClientFactory.ConnectAsync(new() { ["/scheme/suggest"] = (HttpStatusCode.OK, """{"suggestions":[],"warnings":[]}""") });
        using (client)
        {
            await SuggestFacts(Assert.IsType<ContextSchemeOps>(client.Scheme), Array.Empty<Assertion>(), "banner", "", "part", false);
            Assert.Equal("""{"assertions":[],"outputFormat":"banner","filter":"","surface":"part","includeGated":false}""", handler.Requests[^1].Body);
            var before = handler.Requests.Count;
            await SuggestFacts(Assert.IsType<ContextSchemeOps>(client.Scheme), Array.Empty<Assertion>(), "banner", "", "part", false);
            Assert.Equal(before, handler.Requests.Count);
        }
    }

    [Fact]
    public async Task FactsSuggestOmitsAllUnsetOptions()
    {
        var (client, handler) = await CtxClientFactory.ConnectAsync(new() { ["/scheme/suggest"] = (HttpStatusCode.OK, """{"suggestions":[],"warnings":[]}""") });
        using (client)
        {
            await SuggestFacts(Assert.IsType<ContextSchemeOps>(client.Scheme), Array.Empty<Assertion>());
            Assert.Equal("""{"assertions":[]}""", handler.Requests[^1].Body);
        }
    }

    [Fact]
    public async Task EmptyTextRemainsAProvidedInput()
    {
        var (client, handler) = await CtxClientFactory.ConnectAsync(new() { ["/scheme/suggest"] = (HttpStatusCode.OK, """{"suggestions":[],"warnings":[]}""") });
        using (client)
        {
            await Assert.IsType<ContextSchemeOps>(client.Scheme).SuggestAsync("", inputFormat: "part", includeGated: false);
            Assert.Equal("""{"input":"","inputFormat":"part","includeGated":false}""", handler.Requests[^1].Body);
        }
    }

    [Fact]
    public async Task NullTextIsRefusedBeforeTransport()
    {
        var (client, handler) = await CtxClientFactory.ConnectAsync();
        using (client)
        {
            var before = handler.Requests.Count;
            await Assert.ThrowsAsync<ArgumentNullException>(() => Assert.IsType<ContextSchemeOps>(client.Scheme).SuggestAsync((string)null!));
            Assert.Equal(before, handler.Requests.Count);
        }
    }

    [Fact]
    public async Task NullFactsAreRefusedBeforeTransport()
    {
        var (client, handler) = await CtxClientFactory.ConnectAsync();
        using (client)
        {
            var before = handler.Requests.Count;
            await Assert.ThrowsAsync<ArgumentNullException>(() => SuggestFacts(Assert.IsType<ContextSchemeOps>(client.Scheme), null));
            Assert.Equal(before, handler.Requests.Count);
        }
    }

    [Theory]
    [InlineData("parse")]
    [InlineData("user")]
    [InlineData("stated")]
    [InlineData("host-label")]
    [InlineData("infer")]
    [InlineData("default")]
    [InlineData("context")]
    [InlineData("repair")]
    public async Task SourceLabelsAndFactPayloadStayOpenOnTheWire(string source)
    {
        var (client, handler) = await CtxClientFactory.ConnectAsync(new() { ["/scheme/suggest"] = (HttpStatusCode.BadRequest, """{"error":"invalid_input","code":"INVALID_INPUT","detail":"fixture-refusal"}""") });
        using (client)
        {
            using var metadata = JsonDocument.Parse("""{"nested":{"label":"private"}}""");
            var fact = new Assertion("date", "CALENDAR", Confidence.Certain, source, Metadata: metadata.RootElement.Clone(), SourceRule: "rule-1", Value: "20351231");
            var error = await Assert.ThrowsAsync<EzClassifyClientException>(() => SuggestFacts(Assert.IsType<ContextSchemeOps>(client.Scheme), new[] { fact }));
            Assert.Equal(400, error.StatusCode);
            Assert.Equal("INVALID_INPUT", error.Code);
            Assert.Equal("fixture-refusal", error.Detail);
            Assert.Equal("""{"error":"invalid_input","code":"INVALID_INPUT","detail":"fixture-refusal"}""", error.ResponseBody);
            using var request = JsonDocument.Parse(Assert.IsType<string>(handler.Requests[^1].Body));
            var sent = request.RootElement.GetProperty("assertions")[0];
            Assert.Equal(source, sent.GetProperty("source").GetString());
            Assert.Equal("20351231", sent.GetProperty("value").GetString());
            Assert.Equal("rule-1", sent.GetProperty("sourceRule").GetString());
            Assert.Equal(metadata.RootElement.GetRawText(), sent.GetProperty("metadata").GetRawText());
            Assert.False(request.RootElement.TryGetProperty("input", out _));
            Assert.False(request.RootElement.TryGetProperty("inputFormat", out _));
        }
    }
}
