using System.Linq;
using System.Text.Json;
using Xunit;

namespace EzClassify.Tests;

public class ContextResultParserTolerantParsingTests
{
    [Fact]
    public void SchemeGrammar_ParsesTheBodyTheServerSends()
    {
        // The wire corpus's ctx-scheme-grammar.ok body. The contract declares no
        // separator on the grammar, and the server sends none.
        var grammar = ContextResultParser.ParseSchemeGrammar("""{"caseSensitive":false,"stripWhitespace":true}""");

        Assert.False(grammar.CaseSensitive);
        Assert.True(grammar.StripWhitespace);
    }

    [Fact]
    public void SchemeView_ReadsItsVocabularyOverrides()
    {
        var view = ContextResultParser.ParseSchemeView("""
          {"id":"acme","name":"Acme","description":"d","categories":[],"uncategorized":[],
           "vocabulary":{"whole":{"singular":"banner","plural":"banners"}}}
          """);
        var bare = ContextResultParser.ParseSchemeView("""
          {"id":"acme","name":"Acme","description":"d","categories":[],"uncategorized":[]}
          """);

        Assert.Equal("banners", view.Vocabulary!["whole"].Plural);
        Assert.Null(bare.Vocabulary);
    }

    [Fact]
    public void SchemeView_RefusesAConceptWhosePositionIsNotANumber()
    {
        var refusal = Assert.Throws<JsonException>(() => ContextResultParser.ParseSchemeView("""
          {"id":"acme","name":"Acme","description":"d","categories":[],
           "uncategorized":[{"id":"H","name":"High","views":{"default":"H"},"aliases":[],"categories":[],"parents":[],"position":"first"}]}
          """));
        Assert.Contains("position", refusal.Message);
    }

    [Fact]
    public void GuideView_ReadsEachRuleRow()
    {
        var view = ContextResultParser.ParseGuideView("""
          {"id":"g","name":"Guide","description":"","uncategorized":[],
           "categories":[{"id":"net","label":"Network","items":[{"id":"r2","name":"Topology","outcomeText":"SENSITIVE","isComputed":true,
             "whenText":"r1 is active","keywords":[],"matcherLabels":["CIDR block"],"dependsOn":["r1"],"dependsOnNames":["Addresses"],
             "overrides":[],"overrideNames":[]}]}]}
          """);

        var rule = Assert.Single(Assert.Single(view.Categories).Items);
        Assert.Equal(("r2", "SENSITIVE", true, "r1 is active"), (rule.Id, rule.OutcomeText, rule.IsComputed, rule.WhenText));
        Assert.Equal(new[] { "Addresses" }, rule.DependsOnNames);
        Assert.Equal(new[] { "CIDR block" }, rule.MatcherLabels);
    }

    [Fact]
    public void TraceAndNoticeRecords_CarryTheirOptionalContractFields()
    {
        using var entry = JsonDocument.Parse("""
          {"stage":"infer","action":"added","detail":"d","conceptId":"B","conceptName":"Bravo","categoryId":"share","ruleId":"r1"}
          """);
        using var inference = JsonDocument.Parse("""{"ruleId":"r1","conceptId":"B","conceptName":"Bravo","explanation":"e"}""");
        var notices = ContextResultParser.ParseNotices("""
          [{"conceptId":"B","conceptName":"Bravo","type":"handling","text":"t","required":true,"views":{"ism":"BR"}}]
          """);

        var parsedEntry = ResultParser.ParseTraceEntry(entry.RootElement);
        Assert.Equal("Bravo", parsedEntry.ConceptName);
        Assert.Equal("share", parsedEntry.CategoryId);
        Assert.Equal("Bravo", ResultParser.ParseTraceInference(inference.RootElement).ConceptName);
        Assert.Equal("BR", Assert.Single(notices).Views!["ism"]);
    }

    [Fact]
    public void CoverageReport_ReadsExcessAndRefusalsBesideMissing()
    {
        var report = ContextResultParser.ParseCoverageReport("""
          {"covered":false,
           "missing":[],"missingLabels":[],
           "excess":[{"conceptId":"ALPHA","categoryId":"share","confidence":"certain","source":"parse"},
                     {"conceptId":"CHARLIE","categoryId":"share","confidence":"certain","source":"parse"}],
           "excessLabels":["ALPHA","CHARLIE"],
           "refusals":[{"constraintId":"rollup-refused:share","message":"Share cannot be combined."}],
           "rendered":"LOW//BRAVO",
           "fieldTheServerAddedLater":{"any":"shape"}}
          """);

        Assert.False(report.Covered);
        Assert.Empty(report.Missing);
        Assert.Equal(new[] { "ALPHA", "CHARLIE" }, report.Excess.Select(a => a.ConceptId));
        Assert.All(report.Excess, a => Assert.Equal("share", a.CategoryId));
        Assert.Equal(new[] { "ALPHA", "CHARLIE" }, report.ExcessLabels);
        var refusal = Assert.Single(report.Refusals!);
        Assert.Equal("rollup-refused:share", refusal.ConstraintId);
    }

    [Fact]
    public void CoverageReport_AbsentRefusalsStayNull()
    {
        var report = ContextResultParser.ParseCoverageReport("""
          {"covered":true,"missing":[],"missingLabels":[],"excess":[],"excessLabels":[],"rendered":"LOW"}
          """);

        Assert.True(report.Covered);
        Assert.Empty(report.Excess);
        Assert.Empty(report.ExcessLabels);
        Assert.Null(report.Refusals);
    }

    [Fact]
    public void OptionalProfileFields_OmittedRemainNullRatherThanEmpty()
    {
        var json = """
          {"resolution":{"contextId":"ctx","resolvedVersion":"1.0.0","artifacts":[],"sources":[]},"operationLimitations":[],
           "responseProjection":{"profile":"standard","detailsComplete":false,"omittedFields":[],"counts":{"nominations":0,"evidenceSpans":0,"guideUnits":0,"accessRuleResults":0,"contentRequirements":0,"contextRequirements":0}},
           "result":{"audit":{"whole":null,"wholes":[],"wholeValidation":null,"parts":[],"wholeCoversParts":null,"unmarkedSpans":[],"proceduralFlags":[],"overallVerdict":"pass","vocabulary":null,"documentDigest":"sha256:d","guideAssessment":null,"markingRequirements":{"whole":false,"part":false},"limitations":[]},
             "authored":{"basis":"authored","advisory":false,"status":"not-evaluated","target":null,"limitations":[]},
             "authoritative":{"basis":"authoritative","advisory":false,"status":"not-evaluated","target":null,"limitations":[]},"projected":null}}
          """;
        var result = ContextResultParser.ParseContextDocumentAccessAssessmentResult(json);
        Assert.Equal(DocumentResponseProfile.Standard, result.ResponseProjection.Profile);
        Assert.Null(result.Result.Authored.Decision);
        Assert.Null(result.Result.Authored.ContentRequirements);
        Assert.Null(result.Result.Authored.ContextRequirements);
    }

    [Fact]
    public void DocumentEnvelope_MissingResponseProjectionThrows()
    {
        var audit = """
          {"resolution":{"contextId":"ctx","resolvedVersion":"1.0.0","artifacts":[],"sources":[]},"operationLimitations":[],
           "result":{"whole":null,"wholes":[],"wholeValidation":null,"parts":[],"wholeCoversParts":null,"unmarkedSpans":[],"proceduralFlags":[],"overallVerdict":"pass","vocabulary":null,"documentDigest":"sha256:d","guideAssessment":null,"markingRequirements":{"whole":false,"part":false},"limitations":[]}}
          """;
        var access = """
          {"resolution":{"contextId":"ctx","resolvedVersion":"1.0.0","artifacts":[],"sources":[]},"operationLimitations":[],
           "result":{"audit":{"whole":null,"wholes":[],"wholeValidation":null,"parts":[],"wholeCoversParts":null,"unmarkedSpans":[],"proceduralFlags":[],"overallVerdict":"pass","vocabulary":null,"documentDigest":"sha256:d","guideAssessment":null,"markingRequirements":{"whole":false,"part":false},"limitations":[]},
             "authored":{"basis":"authored","advisory":false,"status":"not-evaluated","target":null,"limitations":[]},
             "authoritative":{"basis":"authoritative","advisory":false,"status":"not-evaluated","target":null,"limitations":[]},"projected":null}}
          """;

        Assert.ThrowsAny<JsonException>(() => ContextResultParser.ParseContextDocumentAuditResult(audit));
        Assert.ThrowsAny<JsonException>(() => ContextResultParser.ParseContextDocumentAccessAssessmentResult(access));
    }

    [Fact]
    public void StandardProfile_OmittedPartTextRemainsNull()
    {
        var json = """
          {"whole":null,"wholes":[],"wholeValidation":null,
           "parts":[{"marking":"LOW","start":0,"end":3,"offsetEncoding":"utf-8-bytes","scopeId":"part-1","validation":{"assertions":[],"recognition":{"status":"complete","unmatched":[]},"valid":true,"inputWasValid":true,"errors":[],"proposedFixes":[],"appliedFixes":[],"rendered":"LOW"}}],
           "wholeCoversParts":null,"unmarkedSpans":[],"proceduralFlags":[],"overallVerdict":"pass","vocabulary":null,"documentDigest":"sha256:d","guideAssessment":null,"markingRequirements":{"whole":false,"part":false},"limitations":[]}
          """;

        var result = ContextResultParser.ParseDocumentAuditReport(json);

        Assert.Null(result.Parts[0].Text);
    }

    [Fact]
    public void WholeValidation_AppliedFixesCarriesAReorderChange_AbsentIsRefused()
    {
        var withAppliedFixes = """
          {"whole":"LOW","wholes":[],
           "wholeValidation":{"assertions":[],"recognition":{"status":"complete","unmatched":[]},"valid":true,"inputWasValid":false,"errors":[],"proposedFixes":[],"rendered":"LOW",
             "appliedFixes":[{"ruleId":"reorder-1","action":"reorder","message":"non-canonical token order"}]},
           "parts":[],"wholeCoversParts":null,"unmarkedSpans":[],"proceduralFlags":[],"overallVerdict":"pass",
           "vocabulary":null,"documentDigest":"sha256:d","guideAssessment":null,
           "markingRequirements":{"whole":false,"part":false},"limitations":[]}
          """;
        var withoutAppliedFixes = """
          {"whole":"LOW","wholes":[],
           "wholeValidation":{"assertions":[],"recognition":{"status":"complete","unmatched":[]},"valid":true,"inputWasValid":true,"errors":[],"proposedFixes":[],"rendered":"LOW"},
           "parts":[],"wholeCoversParts":null,"unmarkedSpans":[],"proceduralFlags":[],"overallVerdict":"pass",
           "vocabulary":null,"documentDigest":"sha256:d","guideAssessment":null,
           "markingRequirements":{"whole":false,"part":false},"limitations":[]}
          """;

        var withFixes = ContextResultParser.ParseDocumentAuditReport(withAppliedFixes).WholeValidation;
        Assert.NotNull(withFixes);
        Assert.False(withFixes!.InputWasValid);
        var change = Assert.Single(withFixes.AppliedFixes);
        Assert.Equal("reorder-1", change.RuleId);
        Assert.Equal(FixAction.Reorder, change.Action);
        Assert.Equal("non-canonical token order", change.Message);
        Assert.Null(change.Add);
        Assert.Null(change.Remove);

        // Required on the wire, and sent empty when the engine changed nothing: an
        // absent list is a malformed answer, not an empty one.
        var refusal = Assert.Throws<JsonException>(() => ContextResultParser.ParseDocumentAuditReport(withoutAppliedFixes));
        Assert.Equal("$.wholeValidation.appliedFixes", refusal.Path);
    }

    [Fact]
    public void MarkingValidation_MissingRecognition_Throws()
    {
        // The facts and the recognition account are what a caller rolls up or
        // checks access from, so an audit entry without them is malformed.
        var json = """
          {"whole":"LOW","wholes":[],
           "wholeValidation":{"assertions":[],"valid":true,"inputWasValid":true,"errors":[],"proposedFixes":[],"appliedFixes":[],"rendered":"LOW"},
           "parts":[],"wholeCoversParts":null,"unmarkedSpans":[],"proceduralFlags":[],"overallVerdict":"pass",
           "vocabulary":null,"documentDigest":"sha256:d","guideAssessment":null,
           "markingRequirements":{"whole":false,"part":false},"limitations":[]}
          """;
        Assert.Throws<JsonException>(() => ContextResultParser.ParseDocumentAuditReport(json));
    }

    [Fact]
    public void MarkingValidation_MissingAssertions_Throws()
    {
        var json = """
          {"whole":"LOW","wholes":[],
           "wholeValidation":{"recognition":{"status":"complete","unmatched":[]},"valid":true,"inputWasValid":true,"errors":[],"proposedFixes":[],"appliedFixes":[],"rendered":"LOW"},
           "parts":[],"wholeCoversParts":null,"unmarkedSpans":[],"proceduralFlags":[],"overallVerdict":"pass",
           "vocabulary":null,"documentDigest":"sha256:d","guideAssessment":null,
           "markingRequirements":{"whole":false,"part":false},"limitations":[]}
          """;
        Assert.Throws<JsonException>(() => ContextResultParser.ParseDocumentAuditReport(json));
    }

    [Fact]
    public void RequiredCorrectnessField_MissingThrows()
    {
        Assert.ThrowsAny<JsonException>(() => ResultParser.ParseAccessDecision("""{"granted":true,"rules":[{"name":"r"}]}"""));
    }

    [Fact]
    public void UnknownScenarioStatus_KeepsTheToken()
    {
        var json = """
          {"resolution":{"contextId":"ctx","resolvedVersion":"1.0.0","artifacts":[],"sources":[]},"operationLimitations":[],
           "responseProjection":{"profile":"standard","detailsComplete":true,"omittedFields":[],"counts":{"nominations":0,"evidenceSpans":0,"guideUnits":0,"accessRuleResults":0,"contentRequirements":0,"contextRequirements":0}},
           "result":{"audit":{"whole":null,"wholes":[],"wholeValidation":null,"parts":[],"wholeCoversParts":null,"unmarkedSpans":[],"proceduralFlags":[],"overallVerdict":"pass","vocabulary":null,"documentDigest":"sha256:d","guideAssessment":null,"markingRequirements":{"whole":false,"part":false},"limitations":[]},
             "authored":{"basis":"authored","advisory":false,"status":"maybe","target":null,"limitations":[]},
             "authoritative":{"basis":"authoritative","advisory":false,"status":"not-evaluated","target":null,"limitations":[]},"projected":null}}
          """;
        var result = ContextResultParser.ParseContextDocumentAccessAssessmentResult(json);
        Assert.Equal(new AccessScenarioStatus("maybe"), result.Result.Authored.Status);
        Assert.False(result.Result.Authored.Status.IsKnown);
        Assert.Equal(AccessScenarioStatus.NotEvaluated, result.Result.Authoritative.Status);
    }
}

// ---------------------------------------------------------------------------
// Required-field failures are reported as JsonException, the documented parse
// failure for this layer, rather than whatever System.Text.Json raises when a
// key is absent or carries the wrong kind.
// ---------------------------------------------------------------------------

public class ContextResultParserRequiredFieldTests
{
    private const string Resolution =
        """{"contextId":"ctx","resolvedVersion":"1.0.0","artifacts":[],"sources":[]}""";

    [Fact]
    public void Envelope_MissingResult_ThrowsJsonException()
    {
        var json = $$"""{"resolution":{{Resolution}},"operationLimitations":[]}""";

        Assert.Throws<JsonException>(() => ContextResultParser.ParseContextSchemeProcessResult(json));
    }

    [Fact]
    public void Envelope_MissingResolution_ThrowsJsonException()
    {
        var json = """{"result":{"rendered":"LOW","valid":true},"operationLimitations":[]}""";

        Assert.Throws<JsonException>(() => ContextResultParser.ParseContextSchemeProcessResult(json));
    }

    [Fact]
    public void AuditReport_PartMissingItsOffsets_ThrowsJsonException()
    {
        var json = """
          {"whole":null,"wholes":[],"wholeValidation":null,
           "parts":[{"marking":"LOW","offsetEncoding":"utf-8-bytes","scopeId":"part-1","validation":{"assertions":[],"recognition":{"status":"complete","unmatched":[]},"valid":true,"inputWasValid":true,"errors":[],"proposedFixes":[],"appliedFixes":[],"rendered":"LOW"}}],
           "wholeCoversParts":null,"unmarkedSpans":[],"proceduralFlags":[],"overallVerdict":"pass","vocabulary":null,"documentDigest":"sha256:d","guideAssessment":null,"markingRequirements":{"whole":false,"part":false},"limitations":[]}
          """;

        Assert.Throws<JsonException>(() => ContextResultParser.ParseDocumentAuditReport(json));
    }

    [Fact]
    public void AuditReport_WrongTypedOffset_ThrowsJsonException()
    {
        var json = """
          {"whole":null,"wholes":[],"wholeValidation":null,
           "parts":[{"marking":"LOW","start":"0","end":3,"offsetEncoding":"utf-8-bytes","scopeId":"part-1","validation":{"assertions":[],"recognition":{"status":"complete","unmatched":[]},"valid":true,"inputWasValid":true,"errors":[],"proposedFixes":[],"appliedFixes":[],"rendered":"LOW"}}],
           "wholeCoversParts":null,"unmarkedSpans":[],"proceduralFlags":[],"overallVerdict":"pass","vocabulary":null,"documentDigest":"sha256:d","guideAssessment":null,"markingRequirements":{"whole":false,"part":false},"limitations":[]}
          """;

        Assert.Throws<JsonException>(() => ContextResultParser.ParseDocumentAuditReport(json));
    }

    [Fact]
    public void AuditReport_MissingOverallVerdict_ThrowsJsonException()
    {
        var json = """
          {"whole":null,"wholes":[],"wholeValidation":null,"parts":[],
           "wholeCoversParts":null,"unmarkedSpans":[],"proceduralFlags":[],"vocabulary":null,"documentDigest":"sha256:d","guideAssessment":null,"markingRequirements":{"whole":false,"part":false},"limitations":[]}
          """;

        Assert.Throws<JsonException>(() => ContextResultParser.ParseDocumentAuditReport(json));
    }
}
