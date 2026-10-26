using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace EzClassify.Tests;

/// <summary>
/// Executes the workflow's shell steps against scratch Git history, passing
/// the first step's GITHUB_OUTPUT to the enforcement step as Actions does.
/// Scenarios cover accepted changes, refusals and unresolved commit ranges.
/// </summary>
public class VendorReadonlyGateBehaviorTests : IClassFixture<VendorGateFixture>
{
    private readonly VendorGateFixture _fixture;

    public VendorReadonlyGateBehaviorTests(VendorGateFixture fixture) => _fixture = fixture;

    public static TheoryData<string> ScenarioNames()
    {
        var data = new TheoryData<string>();
        foreach (var name in VendorGateFixture.ScenarioNames) data.Add(name);
        return data;
    }

    [Theory]
    [MemberData(nameof(ScenarioNames))]
    public void GateBehavesAsSpecified(string scenarioName)
    {
        var scenario = _fixture.Scenario(scenarioName);
        var result = _fixture.Run(scenario);

        var detail = scenario.Why is null ? string.Empty : $"\n({scenario.Why})";
        Assert.True(
            result.ExitCode == scenario.ExpectedExit,
            $"exit = {result.ExitCode}, want {scenario.ExpectedExit}{detail}\n{result.Output}");

        foreach (var expected in scenario.MustSay ?? Array.Empty<string>())
        {
            Assert.True(
                result.Output.Contains(expected, StringComparison.Ordinal),
                $"output is missing \"{expected}\"{detail}\n{result.Output}");
        }

        foreach (var forbidden in scenario.MustNotSay ?? Array.Empty<string>())
        {
            Assert.False(
                result.Output.Contains(forbidden, StringComparison.Ordinal),
                $"output names {forbidden}, which this event did not introduce{detail}\n{result.Output}");
        }
    }
}

/// <summary>
/// One event handed to the gate, and what the gate must do with it.
/// </summary>
/// <param name="Name">Scenario id, shown in the test report.</param>
/// <param name="Event">GitHub event name (<c>push</c> or <c>pull_request</c>).</param>
/// <param name="PushBefore">The push's recorded pre-push commit.</param>
/// <param name="PushAfter">The pushed head commit.</param>
/// <param name="PushForced">Whether GitHub reported the push as forced.</param>
/// <param name="PrBase">Pull-request base commit.</param>
/// <param name="PrHead">Pull-request head commit.</param>
/// <param name="PrAuthor">Login that opened the pull request.</param>
/// <param name="PrLabels">Comma-separated pull-request labels.</param>
/// <param name="ExpectedExit">Required exit status of the gate.</param>
/// <param name="MustSay">Substrings the gate's output must contain.</param>
/// <param name="MustNotSay">Substrings the gate's output must not contain.</param>
/// <param name="Why">Why this case exists, printed on failure.</param>
public sealed record VendorGateScenario(
    string Name,
    string Event,
    string PushBefore = "",
    string PushAfter = "",
    string PushForced = "false",
    string PrBase = "",
    string PrHead = "",
    string PrAuthor = "",
    string PrLabels = "",
    int ExpectedExit = 0,
    string[]? MustSay = null,
    string[]? MustNotSay = null,
    string? Why = null);

/// <summary>
/// Builds the scratch git history the gate is driven against and runs its
/// extracted shell bodies. Shared across the scenario table so the repository is
/// created once.
/// </summary>
public sealed class VendorGateFixture : IDisposable
{
    private const string ZeroSha = "0000000000000000000000000000000000000000";

    private readonly string _workDirectory;
    private readonly string _repositoryDirectory;
    private readonly string _rangeScript;
    private readonly string _enforceScript;
    private readonly IReadOnlyDictionary<string, string> _gateEnvironment;
    private readonly IReadOnlyDictionary<string, VendorGateScenario> _scenarios;

    /// <summary>Ids of every scenario in the table, for the theory's data source.</summary>
    public static IReadOnlyList<string> ScenarioNames { get; } = new[]
    {
        "genuine bot commit, ordinary push",
        "genuine bot commit, branch-creation push (before is all zeros)",
        "forged author email is refused",
        "forged author email is refused inside a bot-opened PR",
        "human commit riding along in a bot-opened PR is refused",
        "human-opened PR is refused by the PR-author layer",
        "unresolvable push whose head touches nothing does not walk to an ancestor",
        "force-push whose base is not an ancestor is refused",
        "bot commit with a foreign subject is refused",
        "push carrying no vendor commits passes with nothing to check",
        "override label cannot bypass identity on the push path",
        "override label bypasses identity on a PR",
        "override label does not waive the subject gate",
        "a fallback range that resolves empty narrows to the head commit",
        "a push whose base equals its head still inspects the head commit",
    };

    public VendorGateFixture()
    {
        // bash and git are hard requirements of this repository already: the
        // Makefile shells out to scripts/regen.sh and CI diffs the work tree
        // with git. A missing one is a broken checkout, not a reason to skip.
        // Skipping is how a gate goes quietly inert, which is the failure this
        // whole test exists to catch.
        RequireOnPath("bash");
        RequireOnPath("git");

        _workDirectory = Path.Combine(Path.GetTempPath(), "ezc-vendor-gate-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workDirectory);

        _rangeScript = Path.Combine(_workDirectory, "range.sh");
        _enforceScript = Path.Combine(_workDirectory, "enforce.sh");
        File.WriteAllText(_rangeScript, VendorReadonlyWorkflow.RangeStep + "\n");
        File.WriteAllText(_enforceScript, VendorReadonlyWorkflow.EnforcementStep + "\n");

        // The gate runs under the constants the workflow itself declares, not
        // under values this test picked. Hardcoding them here would let the
        // workflow's allowlist be swapped for something permissive with the
        // behavior suite still green. VendorReadonlyWorkflowTests pins what
        // those declared values must be.
        _gateEnvironment = ReadStepEnvironment("ALLOWED_BOT_LOGIN", "ALLOWED_BOT_EMAIL", "OVERRIDE_LABEL");
        var botName = _gateEnvironment["ALLOWED_BOT_LOGIN"];
        var botEmail = _gateEnvironment["ALLOWED_BOT_EMAIL"];
        var overrideLabel = _gateEnvironment["OVERRIDE_LABEL"];

        _repositoryDirectory = Path.Combine(_workDirectory, "repo");
        Directory.CreateDirectory(_repositoryDirectory);
        Git("init", "-q", "-b", "main");

        var seed = Commit("Someone", "someone@example.com", "chore: seed", "README.md", "seed");
        var botVendor = Commit(
            botName, botEmail,
            "chore(spec): vendor spec bundle from contracts@aaaaaaaa",
            "vendor/spec/openapi.yaml", "one");
        var nonVendor = Commit(
            "Someone", "someone@example.com",
            "Widen the push gate", "src/Client.cs", "class X { }");

        // The default branch the push fallback resolves against.
        Git("update-ref", "refs/remotes/origin/main", nonVendor);

        // The bot's display NAME over somebody else's address, with a subject
        // that satisfies the other gate.
        var forgedEmail = Commit(
            botName, "mallory@evil.example",
            "chore(spec): vendor spec bundle from contracts@bbbbbbbb",
            "vendor/spec/openapi.yaml", "malicious");
        var humanEdit = Commit(
            "Some Maintainer", "maintainer@example.com",
            "Hand-edit the vendored spec", "vendor/spec/openapi.yaml", "hand edited");
        var botForeignSubject = Commit(
            botName, botEmail,
            "feat: smuggle content through the spec channel",
            "vendor/spec/openapi.yaml", "smuggled");
        var humanSpecSubject = Commit(
            "Some Maintainer", "maintainer@example.com",
            "chore(spec): land the mirror under the maintainer override",
            "vendor/spec/openapi.yaml", "overridden");

        // A bot commit on a branch origin/main does not contain, which is
        // exactly what the spec bot creates and pushes.
        Git("checkout", "-q", "-b", "chore/spec-update", nonVendor);
        var botOnNewBranch = Commit(
            botName, botEmail,
            "chore(spec): vendor spec bundle from contracts@cccccccc",
            "vendor/spec/openapi.yaml", "two");
        Git("checkout", "-q", "main");

        var identityRefusal = $"not authored by\n       '{botName} <{botEmail}>'";

        var scenarios = new[]
        {
            new VendorGateScenario(
                ScenarioNames[0], "push",
                PushBefore: seed, PushAfter: botVendor,
                ExpectedExit: 0,
                MustSay: new[] { botVendor, "pass identity + subject gates" }),

            new VendorGateScenario(
                ScenarioNames[1], "push",
                PushBefore: ZeroSha, PushAfter: botOnNewBranch,
                ExpectedExit: 0,
                MustSay: new[] { "Falling back to", botOnNewBranch, "pass identity + subject gates" },
                Why: "the spec bot creates chore/spec-update-<sha> and pushes it; "
                     + "a gate that hard-failed branch creation would be turned off"),

            new VendorGateScenario(
                ScenarioNames[2], "push",
                PushBefore: nonVendor, PushAfter: forgedEmail,
                ExpectedExit: 1,
                MustSay: new[] { identityRefusal, forgedEmail, "mallory@evil.example" },
                Why: "a commit author name is free text in the object header; on the push "
                     + "path there is no PR-author layer behind it, so matching the name "
                     + "alone would be the entire identity gate"),

            new VendorGateScenario(
                ScenarioNames[3], "pull_request",
                PrBase: nonVendor, PrHead: forgedEmail, PrAuthor: _gateEnvironment["ALLOWED_BOT_LOGIN"],
                ExpectedExit: 1,
                MustSay: new[] { identityRefusal, forgedEmail }),

            new VendorGateScenario(
                ScenarioNames[4], "pull_request",
                PrBase: forgedEmail, PrHead: humanEdit, PrAuthor: _gateEnvironment["ALLOWED_BOT_LOGIN"],
                ExpectedExit: 1,
                MustSay: new[] { identityRefusal, humanEdit },
                Why: "the PR-author layer cannot see a non-bot commit pushed onto a "
                     + "branch the bot opened; the per-commit check is what catches it"),

            new VendorGateScenario(
                ScenarioNames[5], "pull_request",
                PrBase: nonVendor, PrHead: humanEdit, PrAuthor: "alex-maintainer",
                ExpectedExit: 1,
                MustSay: new[] { $"not '{botName}'" }),

            new VendorGateScenario(
                ScenarioNames[6], "push",
                PushBefore: ZeroSha, PushAfter: nonVendor,
                ExpectedExit: 1,
                MustSay: new[] { "Refusing to pass" },
                MustNotSay: new[] { botVendor },
                Why: "`git log -1 <head> -- vendor/` walks and would return the ancestor "
                     + "bot commit here; the gate would then report on and pass a commit "
                     + "that was never in this push"),

            new VendorGateScenario(
                ScenarioNames[7], "push",
                PushBefore: botOnNewBranch, PushAfter: nonVendor, PushForced: "true",
                ExpectedExit: 1,
                MustSay: new[] { "Refusing to pass" },
                MustNotSay: new[] { botVendor },
                Why: "the recorded base is still in this clone but is no longer an "
                     + "ancestor of the new head, so before..after would omit what the "
                     + "push introduced"),

            new VendorGateScenario(
                ScenarioNames[8], "push",
                PushBefore: humanEdit, PushAfter: botForeignSubject,
                ExpectedExit: 1,
                MustSay: new[] { "disallowed subject", botForeignSubject }),

            new VendorGateScenario(
                ScenarioNames[9], "push",
                PushBefore: botVendor, PushAfter: nonVendor,
                ExpectedExit: 0,
                MustSay: new[] { "nothing to check" },
                Why: "a resolved range that genuinely holds no vendor/ commit is the one "
                     + "empty set the gate may pass on"),

            new VendorGateScenario(
                ScenarioNames[10], "push",
                PushBefore: forgedEmail, PushAfter: humanEdit, PrLabels: overrideLabel,
                ExpectedExit: 1,
                MustSay: new[] { identityRefusal, humanEdit },
                Why: "labels exist only on pull_request events, so the maintainer override "
                     + "must be unreachable from a direct push even when the variable is set"),

            new VendorGateScenario(
                ScenarioNames[11], "pull_request",
                PrBase: botForeignSubject, PrHead: humanSpecSubject,
                PrAuthor: "alex-maintainer", PrLabels: overrideLabel,
                ExpectedExit: 0,
                MustSay: new[] { $"bypassed by {overrideLabel}" },
                MustNotSay: new[] { identityRefusal },
                Why: "the label is the documented maintainer override; if it did not "
                     + "work the instruction the gate prints on failure would be false"),

            new VendorGateScenario(
                ScenarioNames[12], "pull_request",
                PrBase: forgedEmail, PrHead: humanEdit,
                PrAuthor: "alex-maintainer", PrLabels: overrideLabel,
                ExpectedExit: 1,
                MustSay: new[] { $"bypassed by {overrideLabel}", "disallowed subject", humanEdit },
                Why: "the label waives identity only. The subject gate still runs, so the "
                     + "label is an identity exemption and not an off switch for the "
                     + "whole workflow"),

            new VendorGateScenario(
                ScenarioNames[13], "push",
                PushBefore: ZeroSha, PushAfter: botVendor,
                ExpectedExit: 0,
                MustSay: new[] { "Inspecting the HEAD commit", botVendor, "pass identity + subject gates" },
                Why: "the head is a strict ancestor of origin/main, so the fallback range "
                     + "origin/main..head is EMPTY. The range step must notice that and "
                     + "narrow to the head commit; if it emitted the empty range instead, "
                     + "the gate would report on no commits at all and pass"),

            new VendorGateScenario(
                ScenarioNames[14], "push",
                PushBefore: forgedEmail, PushAfter: forgedEmail,
                ExpectedExit: 1,
                MustSay: new[] { identityRefusal, forgedEmail },
                MustNotSay: new[] { "nothing to check" },
                Why: "the recorded base is a perfectly good ancestor of the head (it IS "
                     + "the head), so the ancestry guard is satisfied and base..head is "
                     + "still empty. Only the range step's own emptiness re-check catches "
                     + "this one, and without it the gate reports 'no commits touch "
                     + "vendor/' over a forged commit and exits 0"),
        };

        _scenarios = scenarios.ToDictionary(scenario => scenario.Name, StringComparer.Ordinal);
        Assert.Equal(ScenarioNames.Count, _scenarios.Count);
    }

    /// <summary>Looks up a scenario by the id the theory was parameterized with.</summary>
    /// <param name="name">Scenario id from <see cref="ScenarioNames"/>.</param>
    /// <returns>The scenario definition.</returns>
    public VendorGateScenario Scenario(string name) => _scenarios[name];

    /// <summary>
    /// Runs the gate's two steps over the scratch repository the way the Actions
    /// runner would: the range step writes <c>GITHUB_OUTPUT</c>, and those
    /// values are handed to the enforcement step as <c>steps.range.outputs.*</c>.
    /// A non-zero exit from the range step is the gate's result on its own.
    /// </summary>
    /// <param name="scenario">The event to replay.</param>
    /// <returns>Combined output of both steps and the final exit status.</returns>
    public (int ExitCode, string Output) Run(VendorGateScenario scenario)
    {
        var outputFile = Path.Combine(_workDirectory, "github-output-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(outputFile, string.Empty);

        var rangeEnvironment = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["GITHUB_OUTPUT"] = outputFile,
            ["EVENT_NAME"] = scenario.Event,
            ["PR_BASE_SHA"] = scenario.PrBase,
            ["PR_HEAD_SHA"] = scenario.PrHead,
            ["PUSH_BEFORE"] = scenario.PushBefore,
            ["PUSH_AFTER"] = scenario.PushAfter,
            ["PUSH_FORCED"] = scenario.PushForced,
            ["DEFAULT_BRANCH"] = "main",
        };

        var (rangeExit, rangeOutput) = Bash(_rangeScript, rangeEnvironment);
        if (rangeExit != 0) return (rangeExit, rangeOutput);

        var stepOutputs = File.ReadAllLines(outputFile)
            .Where(line => line.Contains('=', StringComparison.Ordinal))
            .ToDictionary(
                line => line[..line.IndexOf('=', StringComparison.Ordinal)],
                line => line[(line.IndexOf('=', StringComparison.Ordinal) + 1)..],
                StringComparer.Ordinal);

        var enforceEnvironment = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["EVENT_NAME"] = scenario.Event,
            ["RANGE_MODE"] = stepOutputs.GetValueOrDefault("mode", string.Empty),
            ["BASE_SHA"] = stepOutputs.GetValueOrDefault("base", string.Empty),
            ["HEAD_SHA"] = stepOutputs.GetValueOrDefault("head", string.Empty),
            ["PR_AUTHOR"] = scenario.PrAuthor,
            ["PR_LABELS"] = scenario.PrLabels,
        };
        foreach (var pair in _gateEnvironment) enforceEnvironment[pair.Key] = pair.Value;

        var (enforceExit, enforceOutput) = Bash(_enforceScript, enforceEnvironment);
        return (enforceExit, rangeOutput + enforceOutput);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_workDirectory, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a green run over.
        }
    }

    /// <summary>
    /// Parses literal values out of the enforcement step's <c>env:</c> block.
    /// Only plain scalars are accepted; an expression (<c>${{ ... }}</c>) has no
    /// value outside a runner and must not be silently read as one.
    /// </summary>
    /// <param name="keys">Environment variable names to read.</param>
    /// <returns>The declared values, keyed by name.</returns>
    private static IReadOnlyDictionary<string, string> ReadStepEnvironment(params string[] keys)
    {
        var found = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var key in keys)
        {
            var match = new Regex($@"^\s+{Regex.Escape(key)}:[ \t]*(?<value>\S.*?)\s*$", RegexOptions.Multiline)
                .Match(VendorReadonlyWorkflow.Text);
            Assert.True(match.Success, $"vendor-readonly.yml no longer declares {key}.");
            var value = match.Groups["value"].Value;
            Assert.DoesNotContain("${{", value, StringComparison.Ordinal);
            found[key] = value;
        }

        return found;
    }

    private static void RequireOnPath(string tool)
    {
        var probe = Process.Start(new ProcessStartInfo("/usr/bin/env", $"which {tool}")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        });
        Assert.NotNull(probe);
        probe!.WaitForExit();
        Assert.True(probe.ExitCode == 0, $"{tool} is required to exercise the vendor gate but is not on PATH.");
    }

    private string Git(params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("git");
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        var (exitCode, output) = RunProcess(startInfo, new Dictionary<string, string>(StringComparer.Ordinal));
        Assert.True(exitCode == 0, $"git {string.Join(' ', arguments)} failed:\n{output}");
        return output.Trim();
    }

    private string Commit(string name, string email, string subject, string relativePath, string content)
    {
        var fullPath = Path.Combine(_repositoryDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content + "\n");
        Git("add", relativePath);
        Git(
            "-c", "user.name=" + name,
            "-c", "user.email=" + email,
            "commit", "-q", "-m", subject);
        return Git("rev-parse", "HEAD");
    }

    private (int ExitCode, string Output) Bash(string script, IReadOnlyDictionary<string, string> environment)
    {
        var startInfo = new ProcessStartInfo("bash");
        startInfo.ArgumentList.Add(script);
        return RunProcess(startInfo, environment);
    }

    private (int ExitCode, string Output) RunProcess(
        ProcessStartInfo startInfo,
        IReadOnlyDictionary<string, string> environment)
    {
        startInfo.WorkingDirectory = _repositoryDirectory;
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;
        startInfo.UseShellExecute = false;
        // The developer's own git config must not decide whether a security
        // gate passes. Ignoring it also keeps commit identities exactly as this
        // fixture set them.
        startInfo.Environment["GIT_CONFIG_GLOBAL"] = "/dev/null";
        startInfo.Environment["GIT_CONFIG_SYSTEM"] = "/dev/null";
        foreach (var pair in environment) startInfo.Environment[pair.Key] = pair.Value;

        using var process = Process.Start(startInfo);
        Assert.NotNull(process);
        var standardOutput = process!.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        return (process.ExitCode, standardOutput.Result + standardError.Result);
    }
}
