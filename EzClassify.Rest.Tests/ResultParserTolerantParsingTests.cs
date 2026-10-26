using System.Linq;
using System.Text.Json;
using Xunit;

namespace EzClassify.Tests;

// ---------------------------------------------------------------------------
// ResultParser parsing contract.
//
// A member the contract requires must be present, at any depth, and a body that
// lacks one is refused with a JsonException that names it. A member the
// contract marks optional is tolerated: absent or an explicit JSON null reads
// as its default, and a member present with a real value is carried through
// verbatim. The spec-driven refusal coverage lives in RequiredMemberRefusalTests.
// ---------------------------------------------------------------------------

public class ResultParserAssertionDefaultsTests
{
    [Fact]
    public void ParseAssertion_FullPayload_CarriesEveryCurrentField()
    {
        using var doc = JsonDocument.Parse("""
            {
              "categoryId": "level",
              "conceptId": "U",
              "confidence": "inferred",
              "source": "rule",
              "expiresAt": "2026-12-31",
              "metadata": {"origin": "scanner"},
              "provenance": {"providerId":"guide-1","attribution":{"name":"Guide","license":"internal"},"reproducibility":"reproducible","snapshotId":"snapshot-1"},
              "sourceRule": "rule-1",
              "value": "U"
            }
            """);
        var a = ResultParser.ParseAssertion(doc.RootElement);

        Assert.Equal("level", a.CategoryId);
        Assert.Equal("U", a.ConceptId);
        Assert.Equal(Confidence.Inferred, a.Confidence);
        Assert.Equal("rule", a.Source);
        Assert.Equal("2026-12-31", a.ExpiresAt);
        var metadata = Assert.IsType<JsonElement>(a.Metadata);
        Assert.Equal("scanner", metadata.GetProperty("origin").GetString());
        Assert.NotNull(a.Provenance);
        Assert.Equal("guide-1", a.Provenance!.ProviderId);
        Assert.Equal("rule-1", a.SourceRule);
        Assert.Equal("U", a.Value);
    }

    [Fact]
    public void ParseAssertion_OptionalFieldsAbsent_UsesDefaults()
    {
        using var doc = JsonDocument.Parse("""{"categoryId":"level","conceptId":"U","confidence":"certain","source":"parse"}""");
        var a = ResultParser.ParseAssertion(doc.RootElement);

        Assert.Equal("level", a.CategoryId);
        Assert.Equal("U", a.ConceptId);
        Assert.Equal(Confidence.Certain, a.Confidence);
        Assert.Equal("parse", a.Source);
        Assert.Null(a.ExpiresAt);
        Assert.Null(a.Metadata);
        Assert.Null(a.Provenance);
        Assert.Null(a.SourceRule);
        Assert.Null(a.Value);
    }

    [Fact]
    public void ParseAssertion_OpaqueFieldsSurviveDocumentDisposal()
    {
        Assertion a;
        using (var doc = JsonDocument.Parse(
            """{"categoryId":"level","conceptId":"U","confidence":"certain","source":"parse","metadata":{"origin":"scanner"}}"""))
        {
            a = ResultParser.ParseAssertion(doc.RootElement);
        }

        // Clones must not reference the disposed document's buffer.
        var metadata = Assert.IsType<JsonElement>(a.Metadata);
        Assert.Equal("scanner", metadata.GetProperty("origin").GetString());
    }

    [Fact]
    public void ParseAssertion_OpaqueFieldsExplicitNull_YieldNull()
    {
        using var doc = JsonDocument.Parse(
            """{"categoryId":"level","conceptId":"U","confidence":"certain","source":"parse","metadata":null,"provenance":null}""");
        var a = ResultParser.ParseAssertion(doc.RootElement);

        Assert.Null(a.Metadata);
        Assert.Null(a.Provenance);
    }

    [Theory]
    [InlineData("categoryId")]
    [InlineData("conceptId")]
    [InlineData("source")]
    public void ParseAssertion_RequiredFieldExplicitNull_IsRefusedNamingIt(string member)
    {
        var json = """{"categoryId":"level","conceptId":"U","confidence":"certain","source":"parse"}""";
        using var doc = JsonDocument.Parse(json.Replace($"\"{member}\":\"", $"\"{member}\":null,\"x\":\""));
        var ex = Assert.Throws<JsonException>(() => ResultParser.ParseAssertion(doc.RootElement));
        Assert.Equal($"$.{member}", ex.Path);
    }

    [Fact]
    public void ParseAssertion_OptionalStringFieldsExplicitNull_ReadAsAbsent()
    {
        using var doc = JsonDocument.Parse(
            """{"categoryId":"level","conceptId":"U","confidence":"certain","source":"parse","expiresAt":null,"sourceRule":null,"value":null}""");
        var a = ResultParser.ParseAssertion(doc.RootElement);

        Assert.Equal(Confidence.Certain, a.Confidence);
        Assert.Null(a.ExpiresAt);
        Assert.Null(a.SourceRule);
        Assert.Null(a.Value);
    }

    [Theory]
    [InlineData("""{"categoryId":"level","conceptId":"U","source":"parse"}""")]
    [InlineData("""{"categoryId":"level","conceptId":"U","confidence":null,"source":"parse"}""")]
    public void ParseAssertion_MissingConfidence_IsRefused(string json)
    {
        // confidence is wire-required with no default. A missing value is malformed
        // input and must not be read as the trust-raising "certain".
        using var doc = JsonDocument.Parse(json);
        var ex = Assert.Throws<JsonException>(() => ResultParser.ParseAssertion(doc.RootElement));
        Assert.Equal("$.confidence", ex.Path);
    }

    [Theory]
    [InlineData("""{"categoryId":"level","conceptId":"U","confidence":3,"source":"parse"}""")]
    [InlineData("""{"categoryId":"level","conceptId":"U","confidence":true,"source":"parse"}""")]
    [InlineData("""{"categoryId":"level","conceptId":"U","confidence":{"v":"certain"},"source":"parse"}""")]
    public void ParseAssertion_NonStringConfidence_Throws(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var ex = Assert.Throws<JsonException>(() => ResultParser.ParseAssertion(doc.RootElement));
        Assert.Equal("confidence must be a string", ex.Message);
    }

    [Fact]
    public void ParseAssertion_UndeclaredConfidenceToken_StillKeptNotRefused()
    {
        // Negative control: a string token the spec does not list is an open-response
        // value and keeps decoding, only absence and wrong types are malformed.
        using var doc = JsonDocument.Parse("""{"categoryId":"level","conceptId":"U","confidence":"probable","source":"parse"}""");
        var a = ResultParser.ParseAssertion(doc.RootElement);
        Assert.Equal("probable", a.Confidence.ToWire());
        Assert.False(a.Confidence.IsKnown);
    }
}

public class ResultParserValidationErrorDefaultsTests
{
    [Fact]
    public void ParseValidationError_FullPayload_CarriesEveryField()
    {
        using var doc = JsonDocument.Parse("""
            {
              "constraintId": "C1",
              "message": "too complex",
              "severity": "warning",
              "categoryId": "level",
              "affectedConcepts": ["ALPHA", "CHARLIE"],
              "suggestedFix": "drop to U",
              "didYouMean": ["HIGH", "HOLD"]
            }
            """);
        var e = ResultParser.ParseValidationError(doc.RootElement);

        Assert.Equal(new[] { "ALPHA", "CHARLIE" }, e.AffectedConcepts);
        Assert.Equal(new[] { "HIGH", "HOLD" }, e.DidYouMean);
        Assert.Equal("C1", e.ConstraintId);
        Assert.Equal("too complex", e.Message);
        Assert.Equal(Severity.Warning, e.Severity);
        Assert.Equal("level", e.CategoryId);
        Assert.Equal("drop to U", e.SuggestedFix);
    }

    [Fact]
    public void ParseValidationError_RequiredMembersOnly_DefaultsOptionalFields()
    {
        using var doc = JsonDocument.Parse("""{"constraintId":"C1","message":"something wrong"}""");
        var e = ResultParser.ParseValidationError(doc.RootElement);

        Assert.Equal("C1", e.ConstraintId);
        Assert.Equal("something wrong", e.Message);
        Assert.Null(e.AffectedConcepts);
        Assert.Null(e.Severity);
        Assert.Null(e.CategoryId);
        Assert.Null(e.SuggestedFix);
        // Absent is no evidence the token was stray text, so it stays null.
        Assert.Null(e.DidYouMean);
    }

    [Fact]
    public void ParseValidationError_RequiredMembersExplicitNull_AreRefused()
    {
        using var doc = JsonDocument.Parse("""{"constraintId":null,"message":"m"}""");
        var ex = Assert.Throws<JsonException>(() => ResultParser.ParseValidationError(doc.RootElement));
        Assert.Equal("$.constraintId", ex.Path);
    }
}

public class ResultParserWarningDefaultsTests
{
    [Fact]
    public void ParseWarning_FullPayload_CarriesEveryField()
    {
        using var doc = JsonDocument.Parse("""
            {
              "kind": "deprecation",
              "message": "X is deprecated",
              "conceptId": "RD",
              "categoryId": "ctrl",
              "ruleId": "rule-42",
              "replacement": "SO"
            }
            """);
        var w = ResultParser.ParseWarning(doc.RootElement);

        Assert.Equal(WarningKind.Deprecation, w.Kind);
        Assert.Equal("RD", w.ConceptId);
        Assert.Equal("ctrl", w.CategoryId);
        Assert.Equal("rule-42", w.RuleId);
        Assert.Equal("SO", w.Replacement);
    }

    [Fact]
    public void ParseWarning_KindAndMessageOnly_DefaultsOptionalFields()
    {
        using var doc = JsonDocument.Parse("""{"kind":"rule","message":"heads up"}""");
        var w = ResultParser.ParseWarning(doc.RootElement);

        Assert.Equal(WarningKind.Rule, w.Kind);
        Assert.Equal("heads up", w.Message);
        Assert.Null(w.ConceptId);
        Assert.Null(w.CategoryId);
        Assert.Null(w.RuleId);
        Assert.Null(w.Replacement);
    }

    [Fact]
    public void ParseWarning_MessageExplicitNull_IsRefused()
    {
        using var doc = JsonDocument.Parse("""{"kind":"rule","message":null}""");
        var ex = Assert.Throws<JsonException>(() => ResultParser.ParseWarning(doc.RootElement));
        Assert.Equal("$.message", ex.Path);
    }

    [Fact]
    public void ParseWarning_NullKind_Throws()
    {
        // kind is wire-required; a null value is malformed input and must fail
        // loud rather than read as an unknown token.
        using var doc = JsonDocument.Parse("""{"kind":null,"message":"m"}""");
        var ex = Assert.Throws<JsonException>(() => ResultParser.ParseWarning(doc.RootElement));
        Assert.Equal("$.kind", ex.Path);
    }

    [Fact]
    public void ParseAssertion_UnknownConfidenceToken_KeepsTheToken()
    {
        // Responses are open: a confidence a newer server adds keeps the rest
        // of the assertion readable.
        using var doc = JsonDocument.Parse(
            """{"categoryId":"level","conceptId":"U","confidence":"high","source":"parse"}""");
        var assertion = ResultParser.ParseAssertion(doc.RootElement);
        Assert.Equal("high", assertion.Confidence.ToString());
        Assert.False(assertion.Confidence.IsKnown);
        Assert.Equal("U", assertion.ConceptId);
    }
}

public class ResultParserProcessResultTests
{
    [Fact]
    public void ParseProcessResult_PopulatedErrorsAndWarnings_KeepsEveryEntry()
    {
        const string json = """
            {
              "valid": false,
              "rendered": "H",
              "renders": {},
              "assessment":{"emitted":[],"omitted":[],"collapsed":[],"conflicts":[],"unrepresentable":[]},
              "inputWasValid": false,
              "assertions": [],
              "appliedFixes": [],
              "proposedFixes": [],
              "notices": [],
              "errors": [{"constraintId":"C1","message":"bad constraint"}],
              "warnings": [{"kind":"deprecation","message":"X deprecated"}]
            }
            """;
        var result = ResultParser.ParseProcessResult(json);

        Assert.False(result.Valid);
        Assert.Single(result.Errors);
        Assert.Equal("C1", result.Errors[0].ConstraintId);
        Assert.Single(result.Warnings);
        Assert.Equal(WarningKind.Deprecation, result.Warnings[0].Kind);
    }

    [Fact]
    public void ParseProcessResult_CarriesWhatWasReadBeforeRepairAndTheDisplayColor()
    {
        // The wire sends the reading only when repair changed it, and a fix for a rule repair
        // enforced is applied to it, so the parser must not drop it. The display color travels
        // the same hand-written path and must survive it too.
        const string json = """
            {
              "valid": false,
              "rendered": "SENSITIVE",
              "renders": {},
              "assessment":{"emitted":[],"omitted":[],"collapsed":[],"conflicts":[],"unrepresentable":[]},
              "inputWasValid": false,
              "assertions": [{"conceptId":"SENSITIVE","categoryId":"level","confidence":"certain","source":"user"}],
              "assertionsBeforeRepair": [
                {"conceptId":"OPEN","categoryId":"level","confidence":"certain","source":"user"},
                {"conceptId":"SENSITIVE","categoryId":"level","confidence":"certain","source":"user"}
              ],
              "errors": [],
              "warnings": [],
              "appliedFixes": [],
              "proposedFixes": [],
              "notices": [],
              "displayColor": {"background":"#c62828","text":"#ffffff"}
            }
            """;
        var result = ResultParser.ParseProcessResult(json);

        Assert.NotNull(result.AssertionsBeforeRepair);
        Assert.Equal(
            new[] { "OPEN", "SENSITIVE" },
            result.AssertionsBeforeRepair!.Select(a => a.ConceptId));
        Assert.Equal(new ConceptColor("#c62828", "#ffffff"), result.DisplayColor);
    }

    [Fact]
    public void ParseProcessResult_RequiredMembersOnly_LeavesEveryOptionalMemberNull()
    {
        var result = ResultParser.ParseProcessResult("""{"valid":false,"inputWasValid":false,"rendered":"","assertions":[],"errors":[],"warnings":[],"appliedFixes":[],"proposedFixes":[],"notices":[],"renders":{},"assessment":{"emitted":[],"omitted":[],"collapsed":[],"conflicts":[],"unrepresentable":[]}}""");

        Assert.Empty(result.Assertions);
        Assert.Empty(result.Errors);
        Assert.Empty(result.Warnings);
        Assert.Empty(result.AppliedFixes);
        Assert.Empty(result.ProposedFixes);
        Assert.Empty(result.Notices);
        Assert.False(result.Valid);
        Assert.Equal("", result.Rendered);
        Assert.False(result.InputWasValid);
        Assert.Null(result.AssertionsBeforeRepair);
        Assert.Null(result.DisplayColor);
        Assert.Null(result.Recognition);
        // Nothing beyond the one rendering was asked for.
        Assert.Empty(result.Renders);
        Assert.Null(result.SuggestionsFor);
        Assert.Empty(result.Assessment.Emitted);
    }

    [Fact]
    public void ParseProcessResult_MissingAssessment_Throws()
    {
        // The assessment of the one rendering is required on every marking
        // result; without it a caller cannot tell a lossless render from a lossy one.
        Assert.Throws<JsonException>(() => ResultParser.ParseProcessResult("""{"valid":true,"rendered":"H","renders":{}}"""));
    }

    [Fact]
    public void ParseProcessResult_SuggestionsForEntryThatIsNotAnObject_Throws()
    {
        Assert.Throws<JsonException>(() => ResultParser.ParseProcessResult(
            """{"valid":true,"rendered":"H","renders":{},"assessment":{"emitted":[],"omitted":[],"collapsed":[],"conflicts":[],"unrepresentable":[]},"suggestionsFor":{"audience":null}}"""));
    }

    [Fact]
    public void ParseProcessResult_UndeclaredRecognitionRole_KeepsTheToken()
    {
        // Responses are open: a role a newer server adds keeps the rest of the
        // recognition account readable.
        var result = ResultParser.ParseProcessResult(
            """{"valid":true,"inputWasValid":true,"rendered":"H","renders":{},"assessment":{"emitted":[],"omitted":[],"collapsed":[],"conflicts":[],"unrepresentable":[]},"recognition":{"status":"complete","role":"segment","unmatched":[],"corrections":[]},"assertions":[],"errors":[],"warnings":[],"appliedFixes":[],"proposedFixes":[],"notices":[]}""");
        Assert.Equal("segment", result.Recognition!.Role.ToString());
        Assert.False(result.Recognition.Role!.Value.IsKnown);
    }

    [Fact]
    public void ParseValidateResult_CarriesTheReaderAnswerAndNoAuthoringMembers()
    {
        var result = ResultParser.ParseValidateResult("""
            { "assertions": [ { "categoryId": "level", "conceptId": "CONFIDENTIAL", "confidence": "certain", "source": "parse" } ],
              "valid": true, "inputWasValid": true, "errors": [], "warnings": [], "appliedFixes": [],
              "rendered": "CONF // PARTNERS",
              "assessment": { "emitted": [ { "conceptId": "CONFIDENTIAL", "categoryId": "level" } ], "omitted": [], "collapsed": [], "conflicts": [], "unrepresentable": [] },
              "recognition": { "status": "complete", "inputFormat": "sensitivity/short", "role": "part", "surface": "sensitivity", "unmatched": [] },
              "notices": [] }
            """);

        Assert.True(result.Valid);
        Assert.True(result.InputWasValid);
        Assert.Equal("CONF // PARTNERS", result.Rendered);
        Assert.Equal("CONFIDENTIAL", Assert.Single(result.Assessment.Emitted).ConceptId);
        Assert.Equal("sensitivity/short", result.Recognition!.InputFormat);
        Assert.Equal(StructuralRole.Part, result.Recognition.Role);
        Assert.Empty(result.Recognition.Unmatched);
    }

    [Fact]
    public void ParseValidateResult_NonObjectRoot_Throws()
    {
        Assert.Throws<JsonException>(() => ResultParser.ParseValidateResult("[]"));
    }

    [Fact]
    public void ParseProcessResult_WithChangesAndFixes_ConstructsTypedRecords()
    {
        const string json = """
            {
              "valid": true,
              "rendered": "RS/AR",
              "renders": {},
              "assessment":{"emitted":[],"omitted":[],"collapsed":[],"conflicts":[],"unrepresentable":[]},
              "inputWasValid": true,
              "assertions": [],
              "errors": [],
              "warnings": [],
              "notices": [],
              "appliedFixes": [{
                "ruleId": "supersede-1",
                "action": "replace",
                "add": [{"conceptId": "RS", "categoryId": "level"}],
                "remove": [{"conceptId": "S", "categoryId": "level"}],
                "triggerConceptIds": ["AR"],
                "message": "RS supersedes S"
              }],
              "proposedFixes": [{
                "message": "Add AR to satisfy the SCI rule",
                "rendered": "RS/AR",
                "keeps": [{"conceptId": "RS", "categoryId": "level"}, {"conceptId": "AR", "categoryId": "sci"}],
                "adds": [{"conceptId": "AR", "categoryId": "sci"}],
                "removes": [{"conceptId": "S", "categoryId": "level"}]
              }]
            }
            """;
        var r = ResultParser.ParseProcessResult(json);

        Assert.Single(r.AppliedFixes);
        var applied = r.AppliedFixes[0];
        Assert.Equal("supersede-1", applied.RuleId);
        Assert.Equal(FixAction.Replace, applied.Action);
        Assert.NotNull(applied.Add);
        Assert.Equal(new ConceptRef("RS", "level"), Assert.Single(applied.Add!));
        Assert.NotNull(applied.Remove);
        Assert.Equal(new ConceptRef("S", "level"), Assert.Single(applied.Remove!));
        Assert.Equal(new[] { "AR" }, applied.TriggerConceptIds);
        Assert.Equal("RS supersedes S", applied.Message);

        Assert.Single(r.ProposedFixes);
        var proposed = r.ProposedFixes[0];
        Assert.Equal("Add AR to satisfy the SCI rule", proposed.Message);
        Assert.Equal("RS/AR", proposed.Rendered);
        Assert.Equal(2, proposed.Keeps.Count);
        Assert.Equal(new ConceptRef("RS", "level"), proposed.Keeps[0]);
        Assert.Equal(new ConceptRef("AR", "sci"), proposed.Keeps[1]);
        Assert.Equal(new[] { new ConceptRef("AR", "sci") }, proposed.Adds);
        Assert.Equal(new[] { new ConceptRef("S", "level") }, proposed.Removes);
    }

    [Fact]
    public void ParseChange_AddedConceptThatIsNotAnObject_IsRefusedNamingTheElement()
    {
        using var doc = JsonDocument.Parse(
            """{"ruleId":"r","action":"add","message":"m","add":[{"conceptId":"RS","categoryId":"level"},7]}""");
        var ex = Assert.Throws<JsonException>(() => ResultParser.ParseChange(doc.RootElement));
        Assert.Equal("$.add[1]", ex.Path);
    }

    [Fact]
    public void ParseExplanationTrace_ConstructsTypedRecords()
    {
        // What explain answers. No other result carries the trace.
        using var doc = JsonDocument.Parse("""
            {
              "input": "s//si",
              "schemeId": "cedar-valley-protection",
              "rendered": "RS/AR",
              "entries": [{"stage": "repair", "action": "uppercase", "detail": "S -> uppercase", "conceptId": "S", "ruleId": "case-1", "triggerConceptIds": ["S"]}],
              "repairs": [{"ruleId": "case-1", "description": "normalize case", "confidence": "safe", "before": "s", "after": "S", "cosmetic": true}],
              "inferences": [{"ruleId": "sci-1", "conceptId": "AR", "explanation": "AR from context"}],
              "validationErrors": [{"constraintId": "need-caveat", "message": "caveat required"}],
              "finalAssertions": [{"categoryId": "level", "conceptId": "RS", "confidence": "certain", "source": "parse"}]
            }
            """);
        var ex = ResultParser.ParseExplanationTrace(doc.RootElement);
        Assert.Equal("s//si", ex.Input);
        Assert.Equal("cedar-valley-protection", ex.SchemeId);
        Assert.Equal("RS/AR", ex.Rendered);
        Assert.Single(ex.Entries);
        Assert.Equal("repair", ex.Entries[0].Stage);
        Assert.Equal("uppercase", ex.Entries[0].Action);
        Assert.Equal(new[] { "S" }, ex.Entries[0].TriggerConceptIds);
        Assert.Single(ex.Repairs);
        Assert.Equal("safe", ex.Repairs[0].Confidence);
        Assert.True(ex.Repairs[0].Cosmetic);
        Assert.Single(ex.Inferences);
        Assert.Equal("AR", ex.Inferences[0].ConceptId);
        Assert.Single(ex.ValidationErrors);
        Assert.Equal("need-caveat", ex.ValidationErrors[0].ConstraintId);
        Assert.Single(ex.FinalAssertions);
        Assert.Equal(Confidence.Certain, ex.FinalAssertions[0].Confidence);
    }

    [Fact]
    public void ParseChange_UnknownAction_KeepsTheToken()
    {
        using var doc = JsonDocument.Parse("""
            {"ruleId":"r","action":"obliterate","message":"m"}
            """);
        var change = ResultParser.ParseChange(doc.RootElement);
        Assert.Equal(new FixAction("obliterate"), change.Action);
        Assert.False(change.Action.IsKnown);
        Assert.Equal("r", change.RuleId);
    }

    [Theory]
    [InlineData(
        "add", """{"ruleId":"r1","action":"add","add":[{"conceptId":"AR","categoryId":"sci"}],"message":"m"}""",
        true, false)]
    [InlineData(
        "remove", """{"ruleId":"r1","action":"remove","remove":[{"conceptId":"S","categoryId":"level"}],"message":"m"}""",
        false, true)]
    [InlineData(
        "replace",
        """{"ruleId":"r1","action":"replace","add":[{"conceptId":"RS","categoryId":"level"}],"remove":[{"conceptId":"S","categoryId":"level"}],"message":"m"}""",
        true, true)]
    [InlineData("reorder", """{"ruleId":"r1","action":"reorder","message":"m"}""", false, false)]
    public void ParseChange_EachAction_SetsOnlyTheFieldsThatActionUses(
        string action, string json, bool expectAdd, bool expectRemove)
    {
        using var doc = JsonDocument.Parse(json);
        var change = ResultParser.ParseChange(doc.RootElement);

        Assert.Equal("r1", change.RuleId);
        Assert.Equal("m", change.Message);
        Assert.Equal(FixAction.Parse(action), change.Action);
        Assert.Equal(expectAdd, change.Add is not null);
        Assert.Equal(expectRemove, change.Remove is not null);
    }

    [Fact]
    public void ParseProcessResult_RenderedExplicitNull_IsRefused()
    {
        var ex = Assert.Throws<JsonException>(() => ResultParser.ParseProcessResult(
            """{"valid":true,"inputWasValid":true,"rendered":null,"assertions":[],"errors":[],"warnings":[],"appliedFixes":[],"proposedFixes":[],"notices":[],"renders":{},"assessment":{"emitted":[],"omitted":[],"collapsed":[],"conflicts":[],"unrepresentable":[]}}"""));
        Assert.Equal("$.rendered", ex.Path);
    }
}

public class ResultParserSuggestionsTests
{
    [Fact]
    public void ParseSuggestions_ObjectWithoutSuggestionsKey_IsRefused()
    {
        var ex = Assert.Throws<JsonException>(() => ResultParser.ParseSuggestions("""{"unrelated":"value","warnings":[]}"""));
        Assert.Equal("$.suggestions", ex.Path);
    }

    [Fact]
    public void ParseSuggestions_ViewsGatedRequiresAndTheirDisplay_CarriedThrough()
    {
        const string json = """
            {
              "warnings": [],
              "suggestions": [{
                "conceptId": "SO",
                "categoryId": "share",
                "label": "SO",
                "name": "Staff only",
                "description": "",
                "group": "Share",
                "order": 0,
                "selected": false,
                "views": {"default": "SO", "long": "STAFF ACCESS ONLY"},
                "gated": true,
                "requires": ["REL-blocker"],
                "requiresDisplay": ["REL BLOCKER"]
              }]
            }
            """;
        var result = ResultParser.ParseSuggestions(json);

        Assert.Single(result);
        Assert.Equal("STAFF ACCESS ONLY", result[0].Views["long"]);
        Assert.True(result[0].Gated);
        Assert.Equal(new[] { "REL-blocker" }, result[0].Requires);
        Assert.Equal(new[] { "REL BLOCKER" }, result[0].RequiresDisplay);
    }

    [Fact]
    public void ParseSuggestions_GatedRequiresAbsent_YieldNull()
    {
        var result = ResultParser.ParseSuggestions(
            """{"warnings":[],"suggestions":[{"conceptId":"U","categoryId":"c","label":"U","name":"U","description":"","group":"G","order":0,"selected":false,"views":{"default":"U"}}]}""");

        Assert.Single(result);
        Assert.Null(result[0].Gated);
        Assert.Null(result[0].Requires);
    }
}

public class ResultParserAggregateResultTests
{
    [Fact]
    public void ParseAggregateResult_PopulatedErrorsAndWarnings_KeepsEveryEntry()
    {
        const string json = """
            {
              "valid": false,
              "rendered": "S",
              "assertions": [],
              "appliedFixes": [],
              "proposedFixes": [],
              "notices": [],
              "errors": [{"constraintId":"C2","message":"constraint fail"}],
              "warnings": [{"kind":"rule","message":"note"}]
            }
            """;
        var result = ResultParser.ParseAggregateResult(json);

        Assert.False(result.Valid);
        Assert.Single(result.Errors);
        Assert.Equal("C2", result.Errors[0].ConstraintId);
        Assert.Single(result.Warnings);
        Assert.Equal(WarningKind.Rule, result.Warnings[0].Kind);
    }

    [Fact]
    public void ParseAggregateResult_EmptyCollections_ReadAsEmpty()
    {
        var result = ResultParser.ParseAggregateResult("""{"valid":true,"rendered":"H","assertions":[],"errors":[],"warnings":[],"appliedFixes":[],"proposedFixes":[],"notices":[]}""");

        Assert.True(result.Valid);
        Assert.Equal("H", result.Rendered);
        Assert.Empty(result.Assertions);
        Assert.Empty(result.Errors);
        Assert.Empty(result.Warnings);
        Assert.Empty(result.AppliedFixes);
        Assert.Empty(result.ProposedFixes);
        Assert.Empty(result.Notices);
        Assert.Null(result.DisplayColor);
    }

    [Fact]
    public void ParseAggregateResult_CarriesTheDisplayColor()
    {
        var result = ResultParser.ParseAggregateResult(
            """{"valid":true,"rendered":"H","assertions":[],"errors":[],"warnings":[],"appliedFixes":[],"proposedFixes":[],"notices":[],"displayColor":{"background":"#c62828","text":"#ffffff"}}""");

        Assert.Equal(new ConceptColor("#c62828", "#ffffff"), result.DisplayColor);
    }

    [Fact]
    public void ParseAggregateResult_WithFixes_ConstructsTypedRecords()
    {
        const string json = """
            {
              "valid": true,
              "rendered": "H",
              "assertions": [],
              "errors": [],
              "warnings": [],
              "notices": [],
              "appliedFixes": [{"ruleId":"r","action":"reorder","message":"reordered"}],
              "proposedFixes": [{"message":"drop B","rendered":"H","keeps":[],"adds":[],"removes":[{"conceptId":"B","categoryId":"cat"}]}]
            }
            """;
        var r = ResultParser.ParseAggregateResult(json);

        Assert.Equal(FixAction.Reorder, r.AppliedFixes[0].Action);
        Assert.Equal("drop B", r.ProposedFixes[0].Message);
        Assert.Equal(new[] { new ConceptRef("B", "cat") }, r.ProposedFixes[0].Removes);
    }
}

public class ResultParserScanMatchesTests
{
    [Fact]
    public void ParseScanMatches_RegionWithoutItsOffsets_IsRefused()
    {
        var ex = Assert.Throws<JsonException>(() => ResultParser.ParseScanMatches(
            """[{"marking":"H","type":"marking","wrapped":false,"role":"part","result":""" + ValidateBody + "}]"));
        Assert.Equal("$[0].start", ex.Path);
    }

    [Fact]
    public void ParseScanMatches_NullMarkingAndType_AreRefused()
    {
        var ex = Assert.Throws<JsonException>(() => ResultParser.ParseScanMatches(
            """[{"marking":null,"type":null,"start":0,"end":1,"wrapped":false,"role":"part","result":""" + ValidateBody + "}]"));
        Assert.Equal("$[0].marking", ex.Path);
    }

    private const string ValidateBody =
        """{"assertions":[],"valid":true,"inputWasValid":true,"errors":[],"warnings":[],"appliedFixes":[],"rendered":"","notices":[],"assessment":{"emitted":[],"omitted":[],"collapsed":[],"conflicts":[],"unrepresentable":[]}}""";

    [Fact]
    public void ParseScanMatches_NoResultField_Throws()
    {
        // Each region carries what validate answers for it. Standing in an empty
        // valid result for a missing one would report a marking nobody judged
        // as valid, so a region without one is malformed.
        Assert.Throws<JsonException>(() => ResultParser.ParseScanMatches(
            """[{"marking":"H","start":0,"end":1,"type":"marking","wrapped":false,"role":"whole"}]"""));
    }

    [Fact]
    public void ParseScanMatches_WrappedPart_CarriesInnerTypeAndRecognition()
    {
        // The scan instance the contract publishes: a part found in a wrapper,
        // read as the part view and written in it.
        var matches = ResultParser.ParseScanMatches("""
            [ { "marking": "(CONF // PARTNERS)", "start": 0, "end": 18, "type": "sensitivity/short", "wrapped": true,
                "inner": "CONF // PARTNERS", "role": "part",
                "result": { "assertions": [], "valid": true, "inputWasValid": true, "errors": [], "warnings": [], "appliedFixes": [],
                            "rendered": "CONF // PARTNERS", "assessment":{"emitted":[],"omitted":[],"collapsed":[],"conflicts":[],"unrepresentable":[]},
                            "recognition": { "status": "complete", "inputFormat": "sensitivity/short", "role": "part", "surface": "sensitivity", "unmatched": [] },
                            "notices": [] } } ]
            """);

        var match = Assert.Single(matches);
        Assert.Equal("CONF // PARTNERS", match.Inner);
        Assert.Equal("sensitivity/short", match.Type);
        Assert.Equal(StructuralRole.Part, match.Role);
        Assert.Equal("sensitivity/short", match.Result.Recognition!.InputFormat);
        Assert.Equal(StructuralRole.Part, match.Result.Recognition.Role);
    }

    [Fact]
    public void ParseScanMatches_NoInner_StaysNull()
    {
        var matches = ResultParser.ParseScanMatches(
            """[{"marking":"H","start":0,"end":1,"type":"marking","wrapped":false,"role":"whole","result":""" + ValidateBody + "}]");
        Assert.Null(Assert.Single(matches).Inner);
    }

    [Fact]
    public void ParseScanMatches_MissingRole_ThrowsOnClosedRequiredEnum()
    {
        // role is wire-required and closed; its absence is malformed
        // input, not a value to default.
        Assert.ThrowsAny<Exception>(() => ResultParser.ParseScanMatches(
            """[{"marking":"H","start":0,"end":1,"type":"marking","wrapped":false}]"""));
    }
}

public class ResultParserGuideRuleTests
{
    [Fact]
    public void ParseGuideRule_WithCategoryAndDescription_CarriesBoth()
    {
        using var doc = JsonDocument.Parse("""
            {"id":"r1","name":"Rule","outcome":{},"category":"level","description":"The level rule"}
            """);
        var rule = ResultParser.ParseGuideRule(doc.RootElement);

        Assert.Equal("level", rule.Category);
        Assert.Equal("The level rule", rule.Description);
    }

    [Fact]
    public void ParseGuideRule_AKeywordThatIsNotAString_IsRefused()
    {
        using var nullKeyword = JsonDocument.Parse("""{"id":"r1","name":"R","outcome":{},"keywords":[null,"valid"]}""");
        using var numberKeyword = JsonDocument.Parse("""{"id":"r1","name":"R","outcome":{},"keywords":[7]}""");

        Assert.Throws<JsonException>(() => ResultParser.ParseGuideRule(nullKeyword.RootElement));
        Assert.Throws<JsonException>(() => ResultParser.ParseGuideRule(numberKeyword.RootElement));
    }

    [Fact]
    public void ParseGuideRule_NoKeywords_ReturnsNullKeywords()
    {
        using var doc = JsonDocument.Parse("""{"id":"r1","name":"Rule 1","outcome":{}}""");
        var rule = ResultParser.ParseGuideRule(doc.RootElement);

        Assert.Equal("r1", rule.Id);
        Assert.Equal("Rule 1", rule.Name);
        Assert.Null(rule.Keywords);
        Assert.Null(rule.Category);
        Assert.Null(rule.Description);
    }

    [Fact]
    public void ParseGuideRule_NullIdAndName_AreRefused()
    {
        using var doc = JsonDocument.Parse("""{"id":null,"name":null,"outcome":{}}""");
        var ex = Assert.Throws<JsonException>(() => ResultParser.ParseGuideRule(doc.RootElement));
        Assert.Equal("$.id", ex.Path);
    }

    [Fact]
    public void ParseGuideRule_FullPayload_CarriesOutcomeMatchOverridesRationaleWhen()
    {
        using var doc = JsonDocument.Parse("""
            {
              "id": "r1",
              "name": "IP addresses",
              "outcome": {"rendered": "H//NETINT"},
              "match": [{"type": "ipv4"}, {"type": "regex", "pattern": "10\\.0\\..*"}],
              "overrides": ["r0"],
              "rationale": "Internal addressing reveals network topology.",
              "when": {"allOf": ["r2", "r3"]}
            }
            """);
        var rule = ResultParser.ParseGuideRule(doc.RootElement);

        var outcome = Assert.IsType<JsonElement>(rule.Outcome);
        Assert.Equal("H//NETINT", outcome.GetProperty("rendered").GetString());
        Assert.NotNull(rule.Match);
        Assert.Equal(2, rule.Match!.Count);
        var firstMatch = Assert.IsType<JsonElement>(rule.Match[0]);
        Assert.Equal("ipv4", firstMatch.GetProperty("type").GetString());
        Assert.Equal(new[] { "r0" }, rule.Overrides);
        Assert.Equal("Internal addressing reveals network topology.", rule.Rationale);
        var when = Assert.IsType<JsonElement>(rule.When);
        Assert.Equal(JsonValueKind.Array, when.GetProperty("allOf").ValueKind);
    }

    [Fact]
    public void ParseGuideRule_OptionalRuleFieldsAbsent_YieldNull()
    {
        using var doc = JsonDocument.Parse("""{"id":"r1","name":"Rule 1","outcome":{}}""");
        var rule = ResultParser.ParseGuideRule(doc.RootElement);

        Assert.Equal(JsonValueKind.Object, rule.Outcome.ValueKind);
        Assert.Null(rule.Match);
        Assert.Null(rule.Overrides);
        Assert.Null(rule.Rationale);
        Assert.Null(rule.When);
    }
}

public class ResultParserGuideScanResultsTests
{
    [Fact]
    public void ParseGuideScanResults_SpanWithAllFields_CarriesEachField()
    {
        const string json = """
            [{
              "rule": {"id":"r1","name":"Confidential","outcome":{}},
              "matchedKeywords": ["confidential"],
              "spans": [{"end":12,"keyword":"confidential","start":0,"confidence":0.85}],
              "guideId": "guide-1",
              "provenance": {"providerId":"keyword","attribution":{"name":"Scanner","license":"internal"},"reproducibility":"best-effort"}
            }]
            """;
        var results = ResultParser.ParseGuideScanResults(json);

        Assert.Single(results);
        Assert.Single(results[0].Spans);
        Assert.Equal(12, results[0].Spans[0].End);
        Assert.Equal("confidential", results[0].Spans[0].Keyword);
        Assert.Equal(0, results[0].Spans[0].Start);
        Assert.Equal(0.85, results[0].Spans[0].Confidence);
        Assert.NotNull(results[0].Provenance);
        Assert.Equal("keyword", results[0].Provenance!.ProviderId);
    }

    [Fact]
    public void ParseGuideScanResults_SpanConfidenceAbsent_YieldsNull()
    {
        var results = ResultParser.ParseGuideScanResults(
            """[{"rule":{"id":"r1","name":"Rule","outcome":{}},"matchedKeywords":[],"spans":[{"end":5,"keyword":"k","start":1}]}]""");

        Assert.Null(results[0].Spans[0].Confidence);
        Assert.Null(results[0].Provenance);
    }

    [Fact]
    public void ParseGuideScanResults_SpanKeywordNull_IsRefused()
    {
        var ex = Assert.Throws<JsonException>(() => ResultParser.ParseGuideScanResults(
            """[{"rule":{"id":"r1","name":"Rule","outcome":{}},"matchedKeywords":[],"spans":[{"end":5,"keyword":null,"start":1}]}]"""));
        Assert.Equal("$[0].spans[0].keyword", ex.Path);
    }

    [Fact]
    public void ParseGuideScanResults_GuideIdNull_ReturnsNull()
    {
        var results = ResultParser.ParseGuideScanResults(
            """[{"rule":{"id":"r1","name":"Rule","outcome":{}},"matchedKeywords":[],"guideId":null,"spans":[]}]""");

        Assert.Null(results[0].GuideId);
    }

    [Fact]
    public void ParseGuideScanResults_NullMatchedKeyword_IsRefused()
    {
        var ex = Assert.Throws<JsonException>(() => ResultParser.ParseGuideScanResults(
            """[{"rule":{"id":"r1","name":"Rule","outcome":{}},"matchedKeywords":[null,"visible"],"spans":[]}]"""));
        Assert.Equal("matchedKeywords must contain strings", ex.Message);
    }

    [Fact]
    public void ParseGuideScanResults_NoRuleField_IsRefused()
    {
        var ex = Assert.Throws<JsonException>(() => ResultParser.ParseGuideScanResults(
            """[{"guideId":"g1","matchedKeywords":[],"spans":[]}]"""));
        Assert.Equal("$[0].rule", ex.Path);
    }
}

public class ResultParserEvaluationResultTests
{
    [Fact]
    public void ParseEvaluationResult_WithActiveComputedRules_KeepsEveryRule()
    {
        const string json = """
            {
              "aggregatedMarking": "H",
              "selectedRules": [],
              "activeComputedRules": [{"id":"cr1","name":"Computed","outcome":{}}],
              "effectiveMarkings": ["H"],
              "breakdown": [],
              "contributingBreakdown": []
            }
            """;
        var result = ResultParser.ParseEvaluationResult(json);

        Assert.Single(result.ActiveComputedRules);
        Assert.Equal("cr1", result.ActiveComputedRules[0].Id);
        Assert.Equal("Computed", result.ActiveComputedRules[0].Name);
    }

    [Fact]
    public void ParseEvaluationResult_SparseJson_IsRefused()
    {
        var ex = Assert.Throws<JsonException>(() => ResultParser.ParseEvaluationResult("""{"aggregatedMarking":"M"}"""));
        Assert.Equal("$.selectedRules", ex.Path);
    }

    [Fact]
    public void ParseEvaluationResult_EmptyCollections_ReadAsEmpty()
    {
        var result = ResultParser.ParseEvaluationResult(
            """{"aggregatedMarking":"M","selectedRules":[],"activeComputedRules":[],"effectiveMarkings":[],"breakdown":[],"contributingBreakdown":[]}""");

        Assert.Equal("M", result.AggregatedMarking);
        Assert.Empty(result.SelectedRules);
        Assert.Empty(result.ActiveComputedRules);
        Assert.Empty(result.EffectiveMarkings);
        Assert.Empty(result.Breakdown);
        Assert.Empty(result.ContributingBreakdown);
        Assert.Null(result.GuideId);
    }

    [Fact]
    public void ParseEvaluationResult_WithBreakdown_ReadsQualifiedRuleReferences()
    {
        const string json = """
            {
              "aggregatedMarking": "H",
              "aggregatedMarkings": {"scheme-a": "H", "scheme-b": "HIGH"},
              "selectedRules": [],
              "activeComputedRules": [],
              "effectiveMarkings": ["H"],
              "breakdown": [{
                "isComputed": true,
                "wasOverridden": true,
                "overriddenBy": {"artifact": {"kind": "guide", "id": "g", "version": "1.0.0"}, "ruleId": "rule-x", "ruleDigest": "sha256:rx"},
                "rule": {"artifact": {"kind": "guide", "id": "g", "version": "1.0.0"}, "ruleId": "r1", "ruleDigest": "sha256:r1"},
                "outcome": {"rendered": "H"}
              }],
              "contributingBreakdown": [{
                "isComputed": false,
                "wasOverridden": false,
                "rule": {"artifact": {"kind": "guide", "id": "g", "version": "1.0.0"}, "ruleId": "r2", "ruleDigest": "sha256:r2"},
                "outcome": {"rendered": "L"}
              }]
            }
            """;
        var r = ResultParser.ParseEvaluationResult(json);

        Assert.Equal("HIGH", r.AggregatedMarkings!["scheme-b"]);
        Assert.Single(r.Breakdown);
        var b = r.Breakdown[0];
        Assert.True(b.IsComputed);
        Assert.True(b.WasOverridden);
        Assert.Equal(("rule-x", "sha256:rx", "g"), (b.OverriddenBy!.RuleId, b.OverriddenBy.RuleDigest, b.OverriddenBy.Artifact.Id));
        Assert.Equal(("r1", "sha256:r1"), (b.Rule.RuleId, b.Rule.RuleDigest));
        // The outcome stays opaque JSON, as it does on a guide rule.
        Assert.Equal("H", b.Outcome.GetProperty("rendered").GetString());

        Assert.Single(r.ContributingBreakdown);
        Assert.False(r.ContributingBreakdown[0].IsComputed);
        Assert.Null(r.ContributingBreakdown[0].OverriddenBy);
    }

    [Fact]
    public void ParseEvaluationResults_MultipleItems_ParsesEachInOrder()
    {
        const string json = """
            [
              {
                "aggregatedMarking": "H",
                "selectedRules": [{"id":"r1","name":"R1","outcome":{}}],
                "activeComputedRules": [],
                "effectiveMarkings": ["H"],
                "breakdown": [],
                "contributingBreakdown": [],
                "guideId": "guide-1"
              },
              {
                "aggregatedMarking": "L",
                "selectedRules": [],
                "activeComputedRules": [],
                "effectiveMarkings": ["L"],
                "breakdown": [],
                "contributingBreakdown": []
              }
            ]
            """;
        var results = ResultParser.ParseEvaluationResults(json);

        Assert.Equal(2, results.Count);
        Assert.Equal("H", results[0].AggregatedMarking);
        Assert.Equal("guide-1", results[0].GuideId);
        Assert.Equal("L", results[1].AggregatedMarking);
        Assert.Null(results[1].GuideId);
    }

    [Fact]
    public void ParseEvaluationResults_EmptyArray_ReturnsEmpty()
    {
        var results = ResultParser.ParseEvaluationResults("[]");

        Assert.Empty(results);
    }
}

public class ResultParserAccessDecisionTests
{
    [Fact]
    public void ParseAccessDecision_RuleMissingRequiredFields_Throws()
    {
        Assert.ThrowsAny<JsonException>(() => ResultParser.ParseAccessDecision(
            """{"granted":false,"rules":[{"granted":false}]}"""));
    }

    [Fact]
    public void ParseAccessDecision_NoRulesProperty_Throws()
    {
        Assert.ThrowsAny<JsonException>(() => ResultParser.ParseAccessDecision("""{"granted":true}"""));
    }

    [Fact]
    public void ParseAccessDecision_Provenance_CarriedThrough()
    {
        var decision = ResultParser.ParseAccessDecision(
            """{"granted":true,"rules":[],"provenance":{"providerId":"policy","attribution":{"name":"Policy","license":"internal"},"reproducibility":"reproducible","snapshotId":"3"}}""");

        Assert.NotNull(decision.Provenance);
        Assert.Equal("3", decision.Provenance!.SnapshotId);
    }
}

// ---------------------------------------------------------------------------
// A required scalar sent as an explicit null is a missing member, not a zero.
// GetDouble/GetInt32/GetBoolean would throw on the null; the refusal names it.
// ---------------------------------------------------------------------------

public class ResultParserExplicitNullScalarTests
{
    [Fact]
    public void ParseSuggestions_NullSelectedAndOrder_AreRefused()
    {
        var ex = Assert.Throws<JsonException>(() => ResultParser.ParseSuggestions(
            """{"warnings":[],"suggestions":[{"conceptId":"x","categoryId":"c","label":"L","name":"X","description":"","group":"G","views":{"default":"X"},"selected":null,"order":null}]}"""));
        Assert.Equal("$.suggestions[0].selected", ex.Path);
    }
}

// ---------------------------------------------------------------------------
// Malformed shapes. A required list that arrives as JSON null or as the wrong
// kind entirely is refused, and a root that is not an array is reported as a
// JsonException rather than an InvalidOperationException from the enumerator.
// Optional members of the wrong kind are the parser's to tolerate.
// ---------------------------------------------------------------------------

public class ResultParserMalformedShapeTests
{
    [Theory]
    [InlineData("warnings", "null")]
    [InlineData("assertions", "null")]
    [InlineData("errors", "null")]
    [InlineData("appliedFixes", "null")]
    [InlineData("proposedFixes", "null")]
    [InlineData("notices", "null")]
    [InlineData("warnings", "\"none\"")]
    [InlineData("notices", "\"none\"")]
    public void ParseProcessResult_RequiredListThatIsNullOrNotAnArray_IsRefused(string member, string value)
    {
        var lists = new[] { "warnings", "assertions", "errors", "appliedFixes", "proposedFixes", "notices" }
            .Select(name => $"\"{name}\":{(name == member ? value : "[]")}");
        var json = """{"valid":true,"inputWasValid":true,"rendered":"LOW","renders":{},"assessment":{"emitted":[],"omitted":[],"collapsed":[],"conflicts":[],"unrepresentable":[]},""" + string.Join(",", lists) + "}";

        var ex = Assert.Throws<JsonException>(() => ResultParser.ParseProcessResult(json));
        Assert.Equal($"$.{member}", ex.Path);
    }

    [Fact]
    public void ParseProcessResult_WrongTypedVerdicts_AreRefused()
    {
        // A verdict read as false because it was malformed would be a wrong
        // answer, so neither a wrong type nor a null is defaulted.
        const string tail = ""","renders":{},"assessment":{"emitted":[],"omitted":[],"collapsed":[],"conflicts":[],"unrepresentable":[]}}""";
        Assert.Throws<JsonException>(() => ResultParser.ParseProcessResult("""{"valid":"yes","inputWasValid":true""" + tail));
        Assert.Throws<JsonException>(() => ResultParser.ParseProcessResult("""{"valid":true,"inputWasValid":null""" + tail));
    }

    [Theory]
    [InlineData("""{"valid":true,"renders":{},"assessment":{"emitted":[],"omitted":[],"collapsed":[],"conflicts":[],"unrepresentable":[]}}""")]
    [InlineData("""{"inputWasValid":true,"renders":{},"assessment":{"emitted":[],"omitted":[],"collapsed":[],"conflicts":[],"unrepresentable":[]}}""")]
    public void ParseProcessResult_MissingVerdict_IsRefused(string json) =>
        Assert.Throws<JsonException>(() => ResultParser.ParseProcessResult(json));

    [Fact]
    public void ParseAggregateResult_NullListFields_AreRefused()
    {
        var ex = Assert.Throws<JsonException>(() => ResultParser.ParseAggregateResult(
            """{"valid":true,"rendered":"LOW","assertions":null,"errors":[],"warnings":[],"appliedFixes":[],"proposedFixes":[],"notices":[]}"""));
        Assert.Equal("$.assertions", ex.Path);
    }

    [Fact]
    public void ParseProcessResult_NoticeMissingItsRequiredFields_IsRefusedAtDepth()
    {
        var ex = Assert.Throws<JsonException>(() => ResultParser.ParseProcessResult(
            """{"valid":true,"inputWasValid":true,"rendered":"LOW","renders":{},"assessment":{"emitted":[],"omitted":[],"collapsed":[],"conflicts":[],"unrepresentable":[]},"assertions":[],"errors":[],"warnings":[],"appliedFixes":[],"proposedFixes":[],"notices":[{"conceptId":"LEVEL_HIGH"}]}"""));
        Assert.Equal("$.notices[0].conceptName", ex.Path);
    }

    [Fact]
    public void ParseGuideRule_NullKeywords_StayNull()
    {
        using var doc = JsonDocument.Parse("""{"id":"r1","name":"Rule","outcome":{},"keywords":null}""");
        var rule = ResultParser.ParseGuideRule(doc.RootElement);

        Assert.Null(rule.Keywords);
    }

    [Fact]
    public void ParseEvaluationResult_NullRuleLists_AreRefused()
    {
        var ex = Assert.Throws<JsonException>(() => ResultParser.ParseEvaluationResult(
            """{"aggregatedMarking":"LOW","selectedRules":null,"activeComputedRules":null,"effectiveMarkings":null,"breakdown":null}"""));
        Assert.Equal("$.selectedRules", ex.Path);
    }

    [Fact]
    public void ParseGuideScanResults_NullSpansAndKeywords_AreRefused()
    {
        var ex = Assert.Throws<JsonException>(() => ResultParser.ParseGuideScanResults(
            """[{"rule":{"id":"r1","name":"Rule","outcome":{}},"matchedKeywords":null,"spans":null}]"""));
        Assert.Equal("$[0].matchedKeywords", ex.Path);
    }

    [Fact]
    public void ParseScanMatches_NonArrayRoot_ThrowsJsonException()
    {
        Assert.Throws<JsonException>(() => ResultParser.ParseScanMatches("""{"matches":[]}"""));
    }

    [Fact]
    public void ParseGuideScanResults_NonArrayRoot_ThrowsJsonException()
    {
        Assert.Throws<JsonException>(() => ResultParser.ParseGuideScanResults("""{"results":[]}"""));
    }

    [Fact]
    public void ParseEvaluationResults_NonArrayRoot_ThrowsJsonException()
    {
        Assert.Throws<JsonException>(() => ResultParser.ParseEvaluationResults("""{"results":[]}"""));
    }

    [Fact]
    public void ParseScanMatches_NestedResult_IsCarriedThrough()
    {
        var matches = ResultParser.ParseScanMatches(
            """[{"role":"whole","marking":"LOW","start":0,"end":3,"type":"banner","wrapped":false,"result":{"assertions":[],"errors":[],"warnings":[],"appliedFixes":[],"notices":[],"rendered":"LOW","assessment":{"emitted":[],"omitted":[],"collapsed":[],"conflicts":[],"unrepresentable":[]},"valid":true,"inputWasValid":true}}]""");

        Assert.Single(matches);
        Assert.Equal("LOW", matches[0].Result.Rendered);
        Assert.True(matches[0].Result.Valid);
        Assert.Empty(matches[0].Result.Warnings);
    }

    [Fact]
    public void ParseSuggestions_NonStringRequiresElement_DegradesToEmptyString()
    {
        var result = ResultParser.ParseSuggestions(
            """{"warnings":[],"suggestions":[{"conceptId":"c1","categoryId":"c","label":"C1","name":"C1","description":"","group":"G","order":0,"selected":false,"views":{"default":"C1"},"requires":[1,"c2",null,{}]}]}""");

        Assert.Single(result);
        Assert.Equal(new[] { "", "c2", "", "" }, result[0].Requires);
    }

    [Fact]
    public void ParseChange_NonStringTriggerConceptIdsElement_DegradesToEmptyString()
    {
        using var doc = JsonDocument.Parse(
            """{"ruleId":"r1","action":"add","message":"m","triggerConceptIds":[{},"c2"]}""");
        var change = ResultParser.ParseChange(doc.RootElement);

        Assert.Equal(new[] { "", "c2" }, change.TriggerConceptIds);
    }

    [Fact]
    public void ParseTraceEntry_NonStringTriggerConceptIdsElement_DegradesToEmptyString()
    {
        using var doc = JsonDocument.Parse(
            """{"stage":"evaluate","action":"match","detail":"d","triggerConceptIds":[["nested"],"c2"]}""");
        var entry = ResultParser.ParseTraceEntry(doc.RootElement);

        Assert.Equal(new[] { "", "c2" }, entry.TriggerConceptIds);
    }

    [Fact]
    public void ParseGuideRule_NonStringOverridesElement_DegradesToEmptyString()
    {
        using var doc = JsonDocument.Parse("""{"id":"r1","name":"Rule","outcome":{},"overrides":[7,"r2"]}""");
        var rule = ResultParser.ParseGuideRule(doc.RootElement);

        Assert.Equal(new[] { "", "r2" }, rule.Overrides);
    }
}
