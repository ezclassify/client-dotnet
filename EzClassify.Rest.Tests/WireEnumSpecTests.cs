using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;
using YamlDotNet.RepresentationModel;

namespace EzClassify.Tests;

/// <summary>
/// Spec gate for the wire enums' declared values.
///
/// The enum structs are hand-owned template code, and responses are open, so a
/// contract value missing from a struct would not fail loudly: it decodes as an
/// unknown token and reads like something a newer server added. This test pins
/// each struct's declared token set to its component schema in the vendored
/// contract (vendor/spec/openapi.yaml), so a missing, extra, or misspelled token
/// fails here the day the spec changes.
/// </summary>
public class WireEnumSpecTests
{
    /// <summary>Each wire enum and the component schema whose <c>enum</c> it mirrors.</summary>
    private static readonly IReadOnlyDictionary<Type, string> SpecSchema = new Dictionary<Type, string>
    {
        [typeof(AccessOperandSource)] = "AccessOperandSource",
        [typeof(AccessScenarioBasis)] = "AccessScenarioBasis",
        [typeof(AccessScenarioStatus)] = "AccessScenarioStatus",
        [typeof(ArtifactKind)] = "ArtifactKind",
        [typeof(AuditVerdict)] = "DocumentOverallVerdict",
        [typeof(Confidence)] = "AssertionConfidence",
        [typeof(ContextSchemeRole)] = "ContextSchemeRole",
        [typeof(DecisionProposalOrigin)] = "DecisionProposalOrigin",
        [typeof(DocumentResponseProfile)] = "DocumentResponseProfile",
        [typeof(FixAction)] = "FixAction",
        [typeof(FormatHeaderPosition)] = "FormatHeaderPosition",
        [typeof(GuideCompilationStatus)] = "GuideCompilationStatus",
        [typeof(GuideFactKind)] = "GuideFactKind",
        [typeof(GuideFindingDecision)] = "GuideFindingDecision",
        [typeof(RecognitionCorrectionReason)] = "RecognitionCorrectionReason",
        [typeof(RecognitionLimit)] = "RecognitionLimit",
        [typeof(RecognitionStatus)] = "RecognitionStatus",
        [typeof(RenderFormatMode)] = "RenderFormatMode",
        [typeof(ScanStatus)] = "ScanStatus",
        [typeof(FormatSyntaxType)] = "FormatSyntaxType",
        [typeof(Severity)] = "ValidationSeverity",
        [typeof(SourceReproducibility)] = "Reproducibility",
        [typeof(StructuralRole)] = "ProcessingRole",
        [typeof(UnrepresentableFactReason)] = "UnrepresentableFactReason",
        [typeof(UnrepresentableOutputReason)] = "UnrepresentableOutputReason",
        [typeof(WarningKind)] = "WarningKind",
    };

    private static readonly Lazy<YamlMappingNode> Schemas = new(LoadSchemas);

    public static IEnumerable<object[]> EnumNames() => SpecSchema.Keys.Select(type => new object[] { type.Name });

    [Fact]
    public void EveryWireEnumIsPinnedToASpecSchema()
    {
        var swept = WireEnumsTests.ExtensibleEnums.Select(type => type.Name).OrderBy(n => n, StringComparer.Ordinal);
        var pinned = SpecSchema.Keys.Select(type => type.Name).OrderBy(n => n, StringComparer.Ordinal);
        Assert.Equal(pinned, swept);
    }

    [Theory]
    [MemberData(nameof(EnumNames))]
    public void DeclaredTokensMatchTheContractEnum(string name)
    {
        var type = SpecSchema.Keys.Single(t => t.Name == name);
        var declared = WireEnumsTests.KnownValues(type).Select(value => value.ToString()!);

        Assert.Equal(SpecEnum(SpecSchema[type]), declared.OrderBy(token => token, StringComparer.Ordinal));
    }

    [Fact]
    public void RenderPreservationMatchesTheContractEnum()
    {
        var declared = Enum.GetValues(typeof(RenderPreservation)).Cast<RenderPreservation>().Select(value => value.ToWire());
        Assert.Equal(SpecEnum("RenderPreservation"), declared.OrderBy(token => token, StringComparer.Ordinal));
    }

    // A closed enum lists its tokens under `enum`. An open one, which the
    // contract says a newer server may extend, lists each as a `const` branch
    // of an `anyOf`. Either way the declared tokens are the same set.
    private static IEnumerable<string> SpecEnum(string schema)
    {
        Assert.True(
            Schemas.Value.Children.TryGetValue(new YamlScalarNode(schema), out var node),
            $"vendor/spec/openapi.yaml declares no components.schemas.{schema}.");
        var map = (YamlMappingNode)node;
        var tokens = map.Children.TryGetValue(new YamlScalarNode("enum"), out var values)
            ? ((YamlSequenceNode)values).Children.Select(value => ((YamlScalarNode)value).Value!)
            : map.Children.TryGetValue(new YamlScalarNode("anyOf"), out var branches)
                ? ((YamlSequenceNode)branches).Children.OfType<YamlMappingNode>()
                    .Select(branch => ((YamlScalarNode)branch.Children[new YamlScalarNode("const")]).Value!)
                : throw new Xunit.Sdk.XunitException($"components.schemas.{schema} declares no enum.");
        return tokens.OrderBy(token => token, StringComparer.Ordinal).ToList();
    }

    private static YamlMappingNode LoadSchemas()
    {
        var yaml = new YamlStream();
        using (var reader = new StreamReader(Path.Combine(FindRepoRoot(), "vendor", "spec", "openapi.yaml")))
            yaml.Load(reader);
        var root = (YamlMappingNode)yaml.Documents[0].RootNode;
        var components = (YamlMappingNode)root.Children[new YamlScalarNode("components")];
        return (YamlMappingNode)components.Children[new YamlScalarNode("schemas")];
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "EzClassify.Rest.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Cannot locate EzClassify.Rest.sln");
    }
}
