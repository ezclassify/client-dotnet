using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Xunit;

namespace EzClassify.Tests;

/// <summary>
/// Runs the replayer-contract corpus from vendor/spec/replayer-contract/v1/corpus.json
/// against the local Replayer implementation.  All cases must pass before the
/// replayer may be used to run engine-slice conformance.
/// </summary>
public class ReplayerContractTests
{
    private static string CorpusPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "EzClassify.Rest.sln")))
            {
                var candidate = Path.Combine(
                    dir.FullName, "vendor", "spec",
                    "replayer-contract", "v1", "corpus.json");
                return candidate;
            }
            dir = dir.Parent;
        }
        throw new FileNotFoundException("Cannot locate EzClassify.Rest.sln to resolve spec path");
    }

    [Fact]
    public async Task ReplayerContract_AllCasesPass()
    {
        var corpusBytes = await File.ReadAllBytesAsync(CorpusPath());
        var corpus = JsonNode.Parse(corpusBytes)!;
        var cases = corpus["cases"]!.AsArray();

        using var http = new HttpClient();

        foreach (var tc in cases)
        {
            var id = tc!["id"]!.GetValue<string>();

            // Build recordings map for this case.
            var recordings = new Dictionary<string, ReplayerRecording>();
            var recs = tc["replayerRecordings"]?.AsObject();
            if (recs != null)
            {
                foreach (var kv in recs)
                {
                    var statusCode = kv.Value!["statusCode"]!.GetValue<int>();
                    var body = kv.Value["body"]!.DeepClone();
                    recordings[kv.Key] = new ReplayerRecording(statusCode, body);
                }
            }

            using var replayer = Replayer.Start(recordings);

            var request = tc["request"]!;
            var method = request["method"]!.GetValue<string>();
            var path = request["path"]!.GetValue<string>();
            var bodyNode = request["body"];
            var expectedStatus = tc["expectedStatus"]!.GetValue<int>();

            var bodyJson = request["rawBody"]?.GetValue<string>() ?? bodyNode?.ToJsonString() ?? "{}";
            var content = new StringContent(bodyJson, Encoding.UTF8, "application/json");

            // Apply optional extra headers.
            var headers = request["headers"]?.AsObject();
            var reqMsg = new HttpRequestMessage(new HttpMethod(method), replayer.Url + path)
            {
                Content = content,
            };
            if (headers != null)
            {
                foreach (var h in headers)
                {
                    var val = h.Value!.GetValue<string>();
                    // Content-Type is set on the content, not the request itself.
                    if (!h.Key.Equals("content-type", StringComparison.OrdinalIgnoreCase))
                        reqMsg.Headers.TryAddWithoutValidation(h.Key, val);
                }
            }

            var resp = await http.SendAsync(reqMsg);
            var respBody = await resp.Content.ReadAsStringAsync();
            var actualStatus = (int)resp.StatusCode;

            if (request["rawBody"] is JsonNode rawBody)
                Assert.Equal(Encoding.UTF8.GetBytes(rawBody.GetValue<string>()), replayer.LastRequestBody);

            Assert.True(
                actualStatus == expectedStatus,
                $"[{id}] status: got {actualStatus}, want {expectedStatus}\nbody: {respBody}");

            var actualNode = JsonNode.Parse(respBody);

            if (expectedStatus == 200)
            {
                var expectedBody = tc["expectedBody"];
                if (expectedBody != null)
                {
                    Assert.True(
                        JsonSubset.Matches(actualNode, expectedBody),
                        $"[{id}] body subset mismatch\n  want: {expectedBody}\n  got:  {respBody}");
                }
            }
            else if (expectedStatus == 404)
            {
                var expectedMiss = tc["expectedMissEnvelope"];
                if (expectedMiss != null)
                {
                    Assert.True(
                        JsonSubset.Matches(actualNode, expectedMiss),
                        $"[{id}] miss envelope mismatch\n  want: {expectedMiss}\n  got:  {respBody}");
                }
            }
        }
    }
}
