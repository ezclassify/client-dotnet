using System;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace EzClassify.Tests;

public class ContextResultParserTests
{
    // Two entries of the formats body the runtime serves for the public ISM demo
    // Context. The first is a sequence format; the second writes XML attributes
    // and has no global separator, which the contract makes optional.
    private const string DemoFormatsBody = """
        [
          {
            "id": "portion",
            "surface": "classification",
            "mode": "inline",
            "view": "portion",
            "separator": "//",
            "categoryOrder": [
              "classification",
              "sci_controls",
              "aem",
              "dissemination",
              "rel_to_countries",
              "display_only_countries"
            ],
            "categorySeparators": {
              "sci_controls": "/",
              "aem": "/",
              "dissemination": "/",
              "rel_to_countries": ", ",
              "display_only_countries": ", "
            },
            "separatorBefore": {
              "sci_controls": "//",
              "aem": "//",
              "dissemination": "//",
              "rel_to_countries": " ",
              "display_only_countries": " "
            },
            "categoryTiers": {
              "sci_controls": [
                {
                  "tier": "compartment",
                  "attachedBy": "-",
                  "view": "default"
                },
                {
                  "tier": "subcompartment",
                  "attachedBy": " ",
                  "view": "default"
                }
              ],
              "aem": [
                {
                  "tier": "compartment",
                  "attachedBy": "-",
                  "view": "default"
                },
                {
                  "tier": "subcompartment",
                  "attachedBy": " ",
                  "view": "default"
                }
              ]
            },
            "headers": [
              {
                "concept": "REL-TO",
                "headerCategory": "dissemination",
                "category": "rel_to_countries",
                "position": "prefix"
              },
              {
                "concept": "DISPLAY-ONLY",
                "headerCategory": "dissemination",
                "category": "display_only_countries",
                "position": "prefix"
              }
            ]
          },
          {
            "id": "ism",
            "surface": "classification",
            "mode": "block",
            "view": "ism",
            "categoryOrder": [
              "classification",
              "sci_controls",
              "aem",
              "dissemination",
              "rel_to_countries",
              "display_only_countries",
              "classified_by",
              "reason_code",
              "derivative_source_type",
              "declass_exempt_code",
              "declass_type",
              "declass_exempt_limit",
              "declass_event_date"
            ],
            "categorySeparators": {
              "sci_controls": " ",
              "aem": " ",
              "dissemination": " ",
              "rel_to_countries": " ",
              "display_only_countries": " ",
              "reason_code": " ",
              "declass_exempt_code": " "
            },
            "syntax": {
              "type": "xml-attributes"
            }
          }
        ]
        """;

    [Fact]
    public void ParseSchemeSummary_ReadsASchemeThatNamesNoDefaultFormat()
    {
        // The contract omits defaultRenderFormat when the scheme names no default.
        var summary = ContextResultParser.ParseSchemeSummary("""
            {"id":"acme","version":"1.0.0","name":"Acme","description":"d",
             "referenceSurfaceLabel":{"singular":"Handbook","plural":"Handbooks"}}
            """);

        Assert.Equal("acme", summary.Id);
        Assert.Null(summary.DefaultRenderFormat);
    }

    [Fact]
    public void ParseSchemeFormats_ReadsAFormatWithoutAGlobalSeparator()
    {
        var formats = ContextResultParser.ParseSchemeFormats(DemoFormatsBody);

        Assert.Equal(new[] { "portion", "ism" }, formats.Select(f => f.Id));
        Assert.Equal("//", formats[0].Separator);
        Assert.Null(formats[1].Separator);

        var sequence = formats[0];
        Assert.Equal("classification", sequence.Surface);
        Assert.Equal(RenderFormatMode.Inline, sequence.Mode);
        Assert.Equal("/", sequence.CategorySeparators!["sci_controls"]);
        Assert.Equal("//", sequence.SeparatorBefore!["dissemination"]);
        var tiers = sequence.CategoryTiers!["sci_controls"];
        Assert.Equal(new[] { "compartment", "subcompartment" }, tiers.Select(t => t.Tier));
        Assert.Equal("-", tiers[0].AttachedBy);
        Assert.Null(tiers[0].HeadView);
        var header = sequence.Headers![0];
        Assert.Equal(("REL-TO", "dissemination", "rel_to_countries", FormatHeaderPosition.Prefix),
            (header.Concept, header.HeaderCategory, header.Category, header.Position));
        Assert.Null(sequence.Syntax);

        var attributes = formats[1];
        Assert.Equal(RenderFormatMode.Block, attributes.Mode);
        Assert.Equal(FormatSyntaxType.XmlAttributes, attributes.Syntax!.Type);
        Assert.Null(attributes.Headers);
        Assert.Null(attributes.CategoryTiers);
    }

    [Fact]
    public void ParseSchemeFormats_ReadsAffixesAndPerCategoryViews()
    {
        var format = Assert.Single(ContextResultParser.ParseSchemeFormats("""
            [{"id":"list","surface":"whole","mode":"inline","categoryOrder":["level","tag"],
              "prefix":"[","suffix":"]","categoryViews":{"tag":"short"},
              "categoryEach":{"tag":{"prefix":"#"}},"syntax":{"type":"text"}}]
            """));

        Assert.Equal(("[", "]"), (format.Prefix, format.Suffix));
        Assert.Equal("short", format.CategoryViews!["tag"]);
        Assert.Equal("#", format.CategoryEach!["tag"].Prefix);
        Assert.Null(format.CategoryEach!["tag"].Suffix);
        Assert.Equal(FormatSyntaxType.Text, format.Syntax!.Type);
    }

    [Fact]
    public void ParseSchemeFormats_RefusesAFormatMissingARequiredField()
    {
        Assert.ThrowsAny<JsonException>(() => ContextResultParser.ParseSchemeFormats("""
            [{"id":"list","mode":"inline","categoryOrder":["level"]}]
            """));
    }

    private const string Artifact = """{"kind":"access","id":"access-1","version":"1.0.0","digest":"sha256:access"}""";
    private const string Rule = """{"artifact":{"kind":"access","id":"access-1","version":"1.0.0"},"ruleId":"rule-1","ruleDigest":"sha256:rule"}""";

    [Fact]
    public void ParseAccessDecision_PreservesNestedOperandsQualifiedRuleAndModernization()
    {
        var json = $$"""
            {"granted":false,"rules":[{"ruleIndex":0,"name":"membership","granted":false,"skipped":false,
              "activation":{"matched":true,"assertions":[{"categoryId":"level","conceptId":"HIGH"}],"contextPaths":["distribution.team"]},
              "comparisons":[{"left":{"source":"subject","path":"membership"},"operator":"contains","right":{"source":"literal","value":"HIGH"},"matched":false,"missing":["HIGH"]}],
              "rule":{{Rule}},"reason":"membership required",
              "modernizedFrom":[{"conceptId":"HIGH","replacedConceptId":"OLD-HIGH","conceptDisplay":"HIGH","replacedConceptDisplay":"OLD HIGH"}]}]}
            """;
        var result = ResultParser.ParseAccessDecision(json);

        Assert.False(result.Granted);
        Assert.Equal("rule-1", result.Rules[0].Rule.RuleId);
        var modernized = Assert.Single(result.Rules[0].ModernizedFrom!);
        Assert.Equal(("HIGH", "OLD-HIGH", "OLD HIGH"), (modernized.ConceptId, modernized.ReplacedConceptId, modernized.ReplacedConceptDisplay));
        Assert.Equal(AccessOperandSource.Subject, result.Rules[0].Comparisons[0].Left.Source);
        Assert.Equal("HIGH", result.Rules[0].Comparisons[0].Missing![0]!.Value.GetString());
    }

    [Fact]
    public void ParseDocumentAuditEnvelope_PreservesProjectionAndOmittedText()
    {
        var result = ContextResultParser.ParseContextDocumentAuditResult("""
          {"resolution":{"contextId":"ctx","requestedVersion":"latest","resolvedVersion":"1.0.0","artifacts":[],"sources":[]},
           "operationLimitations":[],
           "responseProjection":{"profile":"standard","detailsComplete":false,"omittedFields":["/result/unmarkedSpans/0/text"],"counts":{"nominations":1,"evidenceSpans":1,"guideUnits":1,"accessRuleResults":0,"contentRequirements":0,"contextRequirements":0}},
           "result":{"whole":null,"wholes":[],"wholeValidation":null,"parts":[],"wholeCoversParts":null,
             "unmarkedSpans":[{"start":0,"end":4,"offsetEncoding":"utf-8-bytes"}],"proceduralFlags":["missing-whole"],
             "overallVerdict":"procedural-gap","vocabulary":{"whole":{"singular":"banner","plural":"banners"},"part":{"singular":"portion","plural":"portions"}},"documentDigest":"sha256:doc","guideAssessment":null,"markingRequirements":{"whole":true,"part":false},"limitations":[]}}
          """);

        Assert.Equal("ctx", result.Resolution.ContextId);
        Assert.Equal(DocumentResponseProfile.Standard, result.ResponseProjection!.Profile);
        Assert.Null(result.Result.UnmarkedSpans[0].Text);
        Assert.Equal("banner", result.Result.Vocabulary!["whole"].Singular);
        Assert.Equal("portions", result.Result.Vocabulary["part"].Plural);
        Assert.Contains("/result/unmarkedSpans/0/text", result.ResponseProjection.OmittedFields);
        Assert.Equal(new DocumentMarkingRequirements(Whole: true, Part: false), result.Result.MarkingRequirements);
    }

    [Fact]
    public void ParseAccessRequirements_UsesQualifiedReferences()
    {
        var json = $$"""
          {"marking":"H","assertions":[],"assertionDisplays":[],"access":{{Artifact}},"scheme":{"kind":"scheme","id":"scheme-1","version":"1.0.0"},
           "appliedRules":[{{Rule}}],"rules":[],"combinedRequirementText":"membership required","combinedAttributesNeeded":["membership"],"lines":[]}
          """;
        var result = ContextResultParser.ParseAccessRequirementsDescription(json);
        Assert.Equal("rule-1", result.AppliedRules[0].RuleId);
        Assert.Equal(ArtifactKind.Access, result.Access.Kind);
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("2", 1)]
    [InlineData("6", 3)]
    public void Utf8ByteOffset_ConvertsScalarBoundaries(string offsetText, int expected)
    {
        var offset = int.Parse(offsetText);
        Assert.Equal(expected, Utf8Offsets.ByteOffsetToStringIndex("é🙂x", offset));
    }

    [Fact]
    public void Utf8ByteOffset_RejectsMidScalarAndInvalidUtf16()
    {
        Assert.Throws<ArgumentException>(() => Utf8Offsets.ByteOffsetToStringIndex("é", 1));
        Assert.Throws<ArgumentException>(() => Utf8Offsets.ByteOffsetToStringIndex("\ud800", 0));
    }
}
