using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Text.Json.Nodes;
using Xunit;

namespace EzClassify.Tests;

public class ConformanceMappingTests
{
    [Theory]
    [InlineData("process", """[[{"conceptId":"A"}],{"context":{"role":"part"},"parts":["P"],"outputs":["full"]}]""", """{"input":"","assertions":[{"conceptId":"A"}],"context":{"role":"part"},"parts":["P"],"outputs":["full"]}""")]
    [InlineData("process", """["T",{"assertions":[{"conceptId":"A"}],"parts":["P"]}]""", """{"input":"T","assertions":[{"conceptId":"A"}],"parts":["P"]}""")]
    [InlineData("validate", """[[{"conceptId":"A"}],{"surface":"s","context":{"role":"part","partOf":{"rendered":"W"}}}]""", """{"assertions":[{"conceptId":"A"}],"surface":"s","context":{"role":"part","partOf":{"rendered":"W"}}}""")]
    [InlineData("validate", "[[]]", """{"assertions":[]}""")]
    [InlineData("validate", """["T",{"inputFormat":"s/view","parts":["P"],"outputFormat":"full","context":{"role":"part"}}]""", """{"input":"T","inputFormat":"s/view","parts":["P"],"outputFormat":"full","context":{"role":"part"}}""")]
    [InlineData("suggest", """[[{"conceptId":"A"}],{"surface":"s","outputFormat":"full"}]""", """{"assertions":[{"conceptId":"A"}],"surface":"s","outputFormat":"full"}""")]
    [InlineData("suggest", "[[]]", """{"assertions":[]}""")]
    [InlineData("suggest", """["T",{"inputFormat":"full","categoryId":"c","context":{"role":"whole"}}]""", """{"input":"T","inputFormat":"full","categoryId":"c","context":{"role":"whole"}}""")]
    [InlineData("aggregate", """[["T"],{"outputFormat":"full","surface":"s","context":{"role":"whole"}}]""", """{"inputs":["T"],"outputFormat":"full","surface":"s","context":{"role":"whole"}}""")]
    [InlineData("aggregateAssertions", """[[[{"conceptId":"A"}]],{"outputFormat":"full","surface":"s"}]""", """{"assertionSets":[[{"conceptId":"A"}]],"outputFormat":"full","surface":"s"}""")]
    [InlineData("render", """[[],"full"]""", """{"assertions":[],"outputFormat":"full"}""")]
    [InlineData("diff", """["A","B",{"inputFormat":"full"}]""", """{"a":"A","b":"B","inputFormat":"full"}""")]
    [InlineData("normalize", """["T",{"inputFormat":"full"}]""", """{"input":"T","inputFormat":"full"}""")]
    [InlineData("scan", """["T",{"inputFormat":"full","outputFormat":"banner"}]""", """{"text":"T","inputFormat":"full","outputFormat":"banner"}""")]
    public void DeclaredOperandsAndOptionsReachTheirWireFields(string method, string args, string expected)
    {
        var result = Map(method, args);
        Assert.Equal($"/v1/ctx/ref-scope-1/1.0.0/scheme/{(method == "aggregateAssertions" ? "aggregate" : method)}", Property<string>(result, "Path"));
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(expected), Property<JsonNode>(result, "Body")));
        Assert.False(Property<bool>(result, "Skip"));
    }

    [Theory]
    [InlineData("""["T",{"input":"other"}]""")]
    public void ConflictingOperandOverridesAreRefused(string args)
    {
        Assert.Throws<InvalidDataException>(() => Map("process", args));
    }

    [Theory]
    [InlineData("""{"valid":true}""", """{"valid":true,"extra":true}""")]
    [InlineData("""{"optional":null}""", "{}")]
    [InlineData("""{"value":true}""", """{"value":"true"}""")]
    [InlineData("""{"items":["a","b"]}""", """{"items":["b","a"]}""")]
    public void UnequalWholeRecordingAnswersCannotOverwrite(string first, string second)
    {
        InTemporaryDirectory(dir =>
        {
            Record(dir, "a", first);
            Record(dir, "b", second);
            Assert.Throws<InvalidDataException>(() => Invoke("LoadAllRecordings", dir));
        });
    }

    [Fact]
    public void EquivalentWholeRecordingAnswersRemainExecutable()
    {
        InTemporaryDirectory(dir =>
        {
            Record(dir, "a", """{"valid":true,"n":1}""");
            Record(dir, "b", """{"n":1.0,"valid":true}""");
            Assert.Single((Dictionary<string, JsonNode>)Invoke("LoadAllRecordings", dir));
        });
    }

    [Fact]
    public void MappedIdentityCannotHideAnUnassertedOutputDifference()
    {
        Assert.Throws<InvalidDataException>(() => MappedPair("""{"valid":true}""", """{"valid":true,"extra":true}"""));
    }

    [Fact]
    public void EqualMappedAnswersCanShareTheirRequestIdentity()
    {
        Assert.Single(MappedPair("""{"valid":true,"n":1}""", """{"n":1e0,"valid":true}"""));
    }

    private static Dictionary<string, ReplayerRecording> MappedPair(string first, string second)
    {
        Dictionary<string, ReplayerRecording>? result = null;
        InTemporaryDirectory(dir =>
        {
            // These two maintained methods intentionally fold onto one single-guide route.
            var plans = """{"slice":"engine","cases":[{"name":"a","method":"evaluateGuide","fixture":"ref-scheme-1","guideFixture":"ref-guide-1","args":[["R"]]},{"name":"b","method":"evaluateMany","fixture":"ref-scheme-1","guideFixture":"ref-guide-1","args":[["R"]]}]}""";
            File.WriteAllText(Path.Combine(dir, "synthetic.json"), plans);
            var args = JsonNode.Parse("""[["R"]]""")?.AsArray() ?? throw new InvalidDataException();
            var recordings = new Dictionary<string, JsonNode>();
            foreach (var (method, answer) in new[] { ("evaluateGuide", first), ("evaluateMany", second) })
            {
                var key = (string)Invoke("ComputeRecordingKey", method, args, JsonValue.Create("ref-scheme-1"), "fromJSON", "", "");
                recordings[key] = JsonNode.Parse(answer) ?? throw new InvalidDataException();
            }
            result = (Dictionary<string, ReplayerRecording>)Invoke("BuildReplayerMap", Invoke("LoadEnginePlans", dir), recordings, FixtureDirectory());
        });
        return result ?? throw new InvalidDataException("No synthetic mapped recordings returned");
    }

    private static object Map(string method, string args) => Invoke("BuildPlanBody", method,
        JsonNode.Parse(args)?.AsArray() ?? throw new InvalidDataException(), JsonValue.Create("ref-scope-1"), "", FixtureDirectory());

    private static string FixtureDirectory() => Path.Combine((string)Invoke("FindRepoRoot"), "vendor", "spec", "fixtures", "v1");

    private static T Property<T>(object result, string name) =>
        (T)(result.GetType().GetProperty(name)?.GetValue(result) ?? throw new MissingMemberException(name));

    private static object Invoke(string name, params object?[] args)
    {
        var method = typeof(ConformanceTests).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)
            ?? throw new MissingMethodException(name);
        var target = method.IsStatic ? null : RuntimeHelpers.GetUninitializedObject(typeof(ConformanceTests));
        try
        {
            return method.Invoke(target, args) ?? throw new InvalidDataException($"No result from {name}");
        }
        catch (TargetInvocationException e) when (e.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(e.InnerException).Throw();
            throw;
        }
    }

    private static void Record(string dir, string name, string answer) => File.WriteAllText(
        Path.Combine(dir, name + ".recordings.json"), "{\"responses\":{\"identity\":{\"response\":" + answer + "}}}");

    private static void InTemporaryDirectory(Action<string> run)
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(path);
        try { run(path); }
        finally { Directory.Delete(path, true); }
    }
}
