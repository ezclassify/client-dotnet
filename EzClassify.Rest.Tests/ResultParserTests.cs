using Xunit;

namespace EzClassify.Tests;

/// <summary>Happy-path wire payloads through ResultParser: each op's canonical response parses into the expected record values.</summary>
public class ResultParserTests
{
    [Fact]
    public void ParseProcessResult_ValidJson_ReturnsExpected()
    {
        const string json = """
            {
              "valid": true,
              "rendered": "H",
              "renders": {
                "banner": { "rendered": "H", "assessment": { "emitted": [{ "conceptId": "LEVEL_HIGH", "categoryId": "level", "value": "A&B" }], "omitted": [], "collapsed": [], "conflicts": [], "unrepresentable": [] } },
                "part": { "rendered": "(H)", "assessment": { "emitted": [], "omitted": [], "collapsed": [], "conflicts": [], "unrepresentable": [] } }
              },
              "assessment": { "emitted": [{ "conceptId": "LEVEL_HIGH", "categoryId": "level" }], "omitted": [], "collapsed": [], "conflicts": [], "unrepresentable": [] },
              "recognition": {
                "status": "partial",
                "inputFormat": "marking/banner",
                "role": "whole",
                "surface": "marking",
                "unmatched": [{ "start": 3, "end": 4, "text": "?" }],
                "corrections": [{ "start": 0, "end": 2, "original": "hi", "replacement": "H", "reason": "alias" }]
              },
              "inputWasValid": true,
              "assertions": [
                { "conceptId": "LEVEL_HIGH", "categoryId": "level", "confidence": "certain", "source": "parse" }
              ],
              "errors": [],
              "warnings": [],
              "appliedFixes": [],
              "proposedFixes": [],
              "notices": [
                { "conceptId": "LEVEL_HIGH", "conceptName": "Level High", "type": "handling", "text": "Handle per policy", "required": true }
              ]
            }
            """;
        var result = ResultParser.ParseProcessResult(json);
        Assert.True(result.Valid);
        Assert.Equal("H", result.Rendered);
        Assert.Equal("(H)", result.Renders["part"].Rendered);
        Assert.Equal("LEVEL_HIGH", Assert.Single(result.Assessment.Emitted).ConceptId);
        Assert.Equal(RecognitionStatus.Partial, result.Recognition!.Status);
        // For a scheme that declares surfaces, the view the text was read as.
        Assert.Equal("marking/banner", result.Recognition.InputFormat);
        Assert.Equal(StructuralRole.Whole, result.Recognition.Role);
        Assert.Equal("marking", result.Recognition.Surface);
        Assert.Equal("?", Assert.Single(result.Recognition.Unmatched).Text);
        Assert.Equal(RecognitionCorrectionReason.Alias, Assert.Single(result.Recognition.Corrections).Reason);
        Assert.Equal("A&B", result.Renders["banner"].Assessment.Emitted[0].Value);
        Assert.True(result.InputWasValid);
        Assert.Single(result.Assertions);
        Assert.Equal("LEVEL_HIGH", result.Assertions[0].ConceptId);
        Assert.Single(result.Notices);
        Assert.Equal("LEVEL_HIGH", result.Notices[0].ConceptId);
        Assert.Equal("Level High", result.Notices[0].ConceptName);
        Assert.Equal("handling", result.Notices[0].Type);
        Assert.Equal("Handle per policy", result.Notices[0].Text);
        Assert.True(result.Notices[0].Required);
    }

    [Fact]
    public void ParseSuggestions_EnvelopeResponse_ReturnsList()
    {
        const string json = """
            {
              "suggestions": [
                { "conceptId": "LEVEL_HIGH", "label": "H", "categoryId": "level", "description": "Level High", "group": "Level", "name": "Level High", "views": { "default": "H" }, "order": 3, "selected": false }
              ],
              "warnings": []
            }
            """;
        var suggestions = ResultParser.ParseSuggestions(json);
        Assert.Single(suggestions);
        Assert.Equal("LEVEL_HIGH", suggestions[0].ConceptId);
        Assert.Equal("H", suggestions[0].Label);
    }

    [Fact]
    public void ParseAggregateResult_ValidJson_ReturnsExpected()
    {
        const string json = """
            {
              "valid": true,
              "rendered": "H",
              "assertions": [
                { "conceptId": "LEVEL_HIGH", "categoryId": "level", "confidence": "certain", "source": "rollup" }
              ],
              "errors": [],
              "warnings": [],
              "appliedFixes": [],
              "proposedFixes": [],
              "notices": [
                { "conceptId": "LEVEL_HIGH", "conceptName": "Level High", "type": "handling", "text": "Handle per policy", "required": false }
              ]
            }
            """;
        var result = ResultParser.ParseAggregateResult(json);
        Assert.True(result.Valid);
        Assert.Equal("H", result.Rendered);
        Assert.Single(result.Assertions);
        Assert.Single(result.Notices);
        Assert.Equal("LEVEL_HIGH", result.Notices[0].ConceptId);
        Assert.False(result.Notices[0].Required);
    }

    [Fact]
    public void ParseAccessDecision_Granted_ReturnsGranted()
    {
        const string json = """
            {
              "granted": true,
              "rules": [
                { "name": "Level Check", "granted": true, "skipped": false, "ruleIndex": 0,
                  "activation":{"matched":true,"assertions":[],"contextPaths":[]}, "comparisons":[],
                  "rule":{"artifact":{"kind":"access","id":"a","version":"1.0.0"},"ruleId":"rule-1","ruleDigest":"sha256:r"} }
              ]
            }
            """;
        var decision = ResultParser.ParseAccessDecision(json);
        Assert.True(decision.Granted);
        Assert.Single(decision.Rules);
        Assert.True(decision.Rules[0].Granted);
        Assert.Equal("rule-1", decision.Rules[0].Rule.RuleId);
    }

    [Fact]
    public void ParseGuideScanResults_FindsMatches()
    {
        const string json = """
            [
              {
                "rule": { "id": "rule-1", "name": "Sensitivity Rule", "outcome": {} },
                "matchedKeywords": ["confidential"],
                "spans": [{ "start": 5, "end": 16, "keyword": "confidential" }],
                "guideId": "guide-1"
              }
            ]
            """;
        var results = ResultParser.ParseGuideScanResults(json);
        Assert.Single(results);
        Assert.Equal("rule-1", results[0].Rule.Id);
        Assert.Contains("confidential", results[0].MatchedKeywords);
        Assert.Equal("guide-1", results[0].GuideId);
    }
}
