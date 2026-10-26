using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;
using YamlDotNet.RepresentationModel;

namespace EzClassify.Tests;

/// <summary>
/// Wire-coverage gate: every operation the vendored contract
/// (vendor/spec/openapi.yaml) declares is sent by a public call, or is declined
/// in writing.
///
/// The evidence is the wire corpus replay. <see cref="WireCorpusReplayTests"/>
/// drives each recorded exchange through the public client against a transport
/// that answers nothing else, so a recorded case it drives publicly is a request
/// some public method provably sends. An operation counts as covered when at
/// least one such case addresses it. Nothing here reads the client source or
/// keeps a hand list of methods, so a renamed route, a route only a test seam
/// sends, or a new contract operation with no public call all fail here.
///
/// The failure this catches is silent otherwise. A spec update lands new routes,
/// every existing test still passes, and the gap only shows up when an integrator
/// asks for something the client cannot do. The declined set is
/// <see cref="DeliberatelyUnimplemented"/>; every entry carries its reason.
/// </summary>
public class SpecOperationCoverageTests
{
    /// <summary>
    /// Contract operations this client does not implement, each with the reason.
    /// Keyed by operationId.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> DeliberatelyUnimplemented =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["getReady"] =
                "A readiness probe for whatever schedules the server. An SDK has no use for it: "
                + "a caller learns the server is up by getting an answer. PingAsync covers the one "
                + "handshake the client does need, the engine version off /v1/health.",
        };

    [Fact]
    public void EverySpecOperation_IsSentByAPublicCallOrDeliberatelyDeclined()
    {
        var operations = ReadSpecOperations();
        var sent = PubliclySentOperations(operations);

        var missing = operations
            .Where(op => !sent.Contains(op.OperationId) && !DeliberatelyUnimplemented.ContainsKey(op.OperationId))
            .OrderBy(op => op.Path, StringComparer.Ordinal)
            .ToList();
        if (missing.Count == 0) return;

        var message = new StringBuilder();
        message.AppendLine($"The vendored contract declares {missing.Count} operation(s) no public call is shown to send:");
        message.AppendLine();
        foreach (var op in missing)
            message.AppendLine($"  {op.Method,-6} {op.Path}   ({op.OperationId})");
        message.AppendLine();
        message.AppendLine("Add the public method and a WireCorpusReplayTests driver that calls it, or, if this SDK");
        message.AppendLine("should not carry the operation, add its operationId to");
        message.AppendLine("SpecOperationCoverageTests.DeliberatelyUnimplemented with the reason why.");
        Assert.Fail(message.ToString());
    }

    /// <summary>
    /// The allowlist must describe operations that exist and that the client
    /// still does not send. An entry left behind after the contract dropped the
    /// operation, or after a public call picked it up, is stale and goes.
    /// </summary>
    [Fact]
    public void DeclinedOperations_StillExistInTheContract_AndNoPublicCallSendsThem()
    {
        var operations = ReadSpecOperations();
        var ids = operations.Select(op => op.OperationId).ToHashSet(StringComparer.Ordinal);
        var sent = PubliclySentOperations(operations);

        var unknown = DeliberatelyUnimplemented.Keys.Where(id => !ids.Contains(id)).OrderBy(id => id, StringComparer.Ordinal).ToList();
        Assert.True(unknown.Count == 0,
            "These operationIds are declined but the contract no longer declares them: " + string.Join(", ", unknown));

        var nowSent = DeliberatelyUnimplemented.Keys.Where(sent.Contains).OrderBy(id => id, StringComparer.Ordinal).ToList();
        Assert.True(nowSent.Count == 0,
            "These operations are declined but a public call now sends them: " + string.Join(", ", nowSent));
    }

    /// <summary>Every reason is a real sentence; an entry that says nothing documents nothing.</summary>
    [Fact]
    public void EveryDeclinedOperation_CarriesASubstantiveReason()
    {
        foreach (var (operationId, reason) in DeliberatelyUnimplemented)
            Assert.True(reason.Trim().Length >= 40,
                $"'{operationId}' is declined with a reason too short to explain anything: '{reason}'");
    }

    /// <summary>
    /// Every request a public call sends addresses exactly one contract
    /// operation, so the coverage above cannot count a typo'd route, or a route
    /// two operations share, toward either one.
    /// </summary>
    [Fact]
    public void EveryPubliclySentCase_AddressesExactlyOneContractOperation()
    {
        var operations = ReadSpecOperations();
        var problems = WireCorpusReplayTests.PublicCases()
            .Select(recorded => (recorded, matches: Matching(operations, recorded).Count()))
            .Where(pair => pair.matches != 1)
            .Select(pair => $"{pair.recorded.Id}: {pair.matches} operations match {pair.recorded.Method} {pair.recorded.Target}")
            .ToList();
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    // ---- contract inventory -------------------------------------------------

    private sealed record SpecOperation(string OperationId, string Method, string Path)
    {
        public string[] Segments { get; } = Path.Trim('/').Split('/');
    }

    private static HashSet<string> PubliclySentOperations(IReadOnlyList<SpecOperation> operations) =>
        WireCorpusReplayTests.PublicCases()
            .SelectMany(recorded => Matching(operations, recorded))
            .Select(op => op.OperationId)
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>The operations whose method and path template a recorded request fits.</summary>
    private static IEnumerable<SpecOperation> Matching(IReadOnlyList<SpecOperation> operations, WireCase recorded)
    {
        var segments = recorded.Target.Split('?')[0].Trim('/').Split('/');
        return operations.Where(op =>
            op.Method.Equals(recorded.Method, StringComparison.OrdinalIgnoreCase)
            && op.Segments.Length == segments.Length
            && op.Segments.Zip(segments, (template, actual) => template.StartsWith('{') || template == actual).All(fits => fits));
    }

    private static IReadOnlyList<SpecOperation> ReadSpecOperations()
    {
        var specPath = Path.Combine(WireCorpus.FindRepoRoot(), "vendor", "spec", "openapi.yaml");
        var yaml = new YamlStream();
        using (var reader = new StreamReader(specPath))
            yaml.Load(reader);

        var root = (YamlMappingNode)yaml.Documents[0].RootNode;
        var paths = (YamlMappingNode)root.Children[new YamlScalarNode("paths")];

        var operations = new List<SpecOperation>();
        foreach (var (pathNode, itemNode) in paths.Children)
        {
            var path = ((YamlScalarNode)pathNode).Value!;
            foreach (var (methodNode, operationNode) in ((YamlMappingNode)itemNode).Children)
            {
                var method = ((YamlScalarNode)methodNode).Value!;
                // A path item also holds non-operation keys (parameters, summary, $ref).
                if (method is not ("get" or "put" or "post" or "delete" or "options" or "head" or "patch" or "trace")) continue;

                var operation = (YamlMappingNode)operationNode;
                Assert.True(operation.Children.TryGetValue(new YamlScalarNode("operationId"), out var idNode),
                    $"{method.ToUpperInvariant()} {path} declares no operationId.");
                var operationId = ((YamlScalarNode)idNode!).Value!;
                Assert.DoesNotContain(operations, op => op.OperationId == operationId);
                operations.Add(new SpecOperation(operationId, method.ToUpperInvariant(), path));
            }
        }

        Assert.True(operations.Count > 0, "Read no operations out of vendor/spec/openapi.yaml.");
        return operations;
    }
}
