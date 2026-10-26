using System;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace EzClassify.Tests;

/// <summary>
/// Checks workflow triggers and shell constructs that enforce the spec-mirror
/// boundary. Shell assertions exclude comments through
/// <see cref="VendorReadonlyWorkflow.CodeOf"/>. Runtime scenarios are covered
/// by <see cref="VendorReadonlyGateBehaviorTests"/>.
/// </summary>
public class VendorReadonlyWorkflowTests
{
    private static readonly string Workflow = VendorReadonlyWorkflow.Text;
    private static readonly string RangeCode = VendorReadonlyWorkflow.CodeOf(VendorReadonlyWorkflow.RangeStep);
    private static readonly string EnforceCode = VendorReadonlyWorkflow.CodeOf(VendorReadonlyWorkflow.EnforcementStep);

    [Fact]
    public void FiresOnPushAndNotOnlyOnPullRequest()
    {
        // Spec commits usually land by direct push. With `pull_request` alone the
        // gate would never run on that path.
        Assert.Matches(new Regex(@"^  pull_request:$", RegexOptions.Multiline), Workflow);
        Assert.Matches(new Regex(@"^  push:$", RegexOptions.Multiline), Workflow);
    }

    [Fact]
    public void WatchesTheSamePathsOnBothTriggers()
    {
        // The two lists are a cheap prefilter and cannot be shared through a
        // YAML anchor (GitHub does not resolve anchors in workflow files). A
        // trigger that lists fewer paths silently stops gating them.
        var blocks = new Regex(@"^    paths:\n(?:      (?:#.*|- .*)\n)+", RegexOptions.Multiline)
            .Matches(Workflow)
            .Select(m => m.Value)
            .ToList();
        Assert.Equal(2, blocks.Count);

        static string[] GlobsOf(string block) => block
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.StartsWith("- ", StringComparison.Ordinal))
            .Select(line => line.Substring(2).Replace("'", string.Empty, StringComparison.Ordinal))
            .ToArray();

        Assert.Equal(GlobsOf(blocks[0]), GlobsOf(blocks[1]));
        Assert.Contains("vendor/spec/**", GlobsOf(blocks[0]));
        Assert.Contains("vendor/SPEC.md", GlobsOf(blocks[0]));
    }

    [Fact]
    public void ReadsHistoryForEveryPathItTriggersOn()
    {
        // A path watched by the trigger but absent from the pathspec the
        // enforcement step reads with is the inert shape in miniature: the
        // workflow wakes up for the change and then finds no commits touching
        // "vendor/", reports nothing to check, and passes.
        var watched = new Regex(@"^      - '([^']+)'$", RegexOptions.Multiline)
            .Matches(Workflow)
            .Select(m => m.Groups[1].Value)
            // Trailing `**` is trigger-glob syntax; git takes the directory.
            .Select(glob => glob.EndsWith("/**", StringComparison.Ordinal) ? glob[..^2] : glob)
            .Distinct()
            .ToList();

        Assert.NotEmpty(watched);
        foreach (var path in watched)
        {
            Assert.Contains($"'{path}'", EnforceCode, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void RefusalNamesTheSpecBotAndTheContractsSource()
    {
        // A blocked contributor needs to know who writes the mirror and where
        // the spec comes from.
        Assert.Contains("the EzClassify spec bot", Workflow, StringComparison.Ordinal);
        Assert.Contains("ezclassify/contracts", Workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void GuardsBothBaseResolutionsWithIsAncestorRatherThanAStringComparison()
    {
        // Two places where an inequality would hand the gate a range that omits
        // or excludes the commits the event actually introduced.
        //
        // The first: after a force-push the recorded `before` can still be an
        // object this clone holds while no longer being an ancestor of the new
        // head, and `before..after` then omits the commits the push introduced.
        // This one is load-bearing on its own; VendorReadonlyGateBehaviorTests
        // drives it as "force-push whose base is not an ancestor is refused".
        Assert.Contains(
            "git merge-base --is-ancestor \"$PUSH_BEFORE\" \"$HEAD_SHA\"",
            RangeCode,
            StringComparison.Ordinal);
        // The second: `origin/<default> != HEAD` is true even when HEAD is an
        // ancestor of origin/<default>, and `origin/<default>..HEAD` is EMPTY
        // in exactly that case.
        //
        // This second assertion is defense in depth rather than the last line:
        // the range step also re-checks that the resolved range holds at least
        // one commit, so swapping this guard for an inequality changes no
        // observable outcome. It is pinned because that re-check is the only
        // thing making it redundant, and the two protections are one edit from
        // trading places.
        Assert.Contains(
            "! git merge-base --is-ancestor \"$HEAD_SHA\" \"$FALLBACK\"",
            RangeCode,
            StringComparison.Ordinal);
    }

    [Fact]
    public void NeverEmitsARangeItHasNotConfirmedHoldsACommit()
    {
        // Whichever base was resolved, the step emits `mode=range` only after
        // confirming the range holds at least one commit, and otherwise falls
        // through to inspecting the head commit with the warning that says so.
        //
        // This one has a case no ancestry guard can cover: a push whose recorded
        // base equals its head satisfies every ancestry test there is, and
        // base..head is still empty. Without this re-check the gate would print
        // "No commits touch vendor/ paths" over a forged commit and exit 0.
        // VendorReadonlyGateBehaviorTests drives it as "a push whose base
        // equals its head still inspects the head commit".
        var emit = Between(RangeCode, "if [ -n \"$BASE\" ]", "exit 0");
        Assert.Contains("git log \"$BASE..$HEAD_SHA\" --format='%H'", emit, StringComparison.Ordinal);
        Assert.Contains("mode=range", emit, StringComparison.Ordinal);
        Assert.Contains("mode=single", RangeCode, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadsTheSingleCommitFallbackWithDiffTreeNotAWalkingLog()
    {
        // `git log -1 <sha> -- vendor/...` WALKS to the nearest ancestor that
        // touched those paths, so on a HEAD that touches nothing it judges a
        // commit that was never part of the push. diff-tree asks only what THIS
        // commit changed.
        Assert.Contains(
            "git diff-tree --no-commit-id --name-only -r \"$HEAD_SHA\"",
            EnforceCode,
            StringComparison.Ordinal);

        // No `git log -1` anywhere in the gate may carry a pathspec, whatever
        // the revision or paths are spelled as.
        var walking = new Regex(@"git log -1\b[^\n]*\s--\s");
        Assert.False(
            walking.IsMatch(RangeCode) || walking.IsMatch(EnforceCode),
            "`git log -1` with a pathspec walks to an ancestor commit; use diff-tree.");
    }

    [Fact]
    public void RefusesRatherThanPassingWhenThePushResolvedToNoInspectableCommits()
    {
        // The workflow only runs on push because the push touched a watched
        // path. Reaching the enforcement step with an empty commit set therefore
        // means the set that was read is not the set that was pushed. Exiting 0
        // there would report a pass having inspected nothing.
        //
        // Isolate the empty-set branch and require a reachable `exit 1` inside
        // it. Asserting on the whole step would pass on any `exit 1` anywhere.
        var branch = Between(EnforceCode, "if [ -z \"$VENDOR_COMMITS\" ]; then", "\nfi\n");

        Assert.Contains("\"$EVENT_NAME\" != \"pull_request\"", branch, StringComparison.Ordinal);
        Assert.Contains("\"$RANGE_MODE\" = \"single\"", branch, StringComparison.Ordinal);
        Assert.Contains("Refusing to pass.", branch, StringComparison.Ordinal);
        Assert.Contains("exit 1", branch, StringComparison.Ordinal);
    }

    [Fact]
    public void ChecksThePullRequestAuthorAsWellAsEachCommitAuthor()
    {
        // Two different holes, so two layers. The PR-level author is the
        // GitHub-verified one and is what stops a human opening a PR full of
        // hand-edited mirror; the per-commit author is what catches a non-bot
        // commit riding along on a branch the bot opened, which the PR-level
        // check cannot see.
        Assert.Contains("PR_AUTHOR: ${{ github.event.pull_request.user.login }}", Workflow, StringComparison.Ordinal);
        Assert.Contains("[ \"$PR_AUTHOR\" != \"$ALLOWED_BOT_LOGIN\" ]", EnforceCode, StringComparison.Ordinal);
        Assert.Contains("ALLOWED_BOT_LOGIN: ezclassify-spec-bot[bot]", Workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void ComparesTheCommitAuthorsEmailAndNotJustTheDisplayName()
    {
        // A commit's author name is free text in the object header. Comparing
        // it alone admits `ezclassify-spec-bot[bot] <mallory@evil.example>`,
        // and on the push path that name check would be the entire identity
        // gate, with no PR-author layer behind it.
        Assert.Contains(
            "ALLOWED_BOT_EMAIL: ezclassify-spec-bot[bot]@users.noreply.github.com",
            Workflow,
            StringComparison.Ordinal);
        Assert.Contains(
            "ALLOWED_BOT_CANONICAL_EMAIL: 348097708+ezclassify-spec-bot[bot]@users.noreply.github.com",
            Workflow,
            StringComparison.Ordinal);
        Assert.Contains(
            "AUTHOR=$(git log -1 --format='%an <%ae>' \"$SHA\")",
            EnforceCode,
            StringComparison.Ordinal);
        Assert.Contains(
            "[ \"$AUTHOR\" != \"$ALLOWED_BOT_LOGIN <$ALLOWED_BOT_EMAIL>\" ]",
            EnforceCode,
            StringComparison.Ordinal);

        // Structural, so it survives a rewrite that renames the variables: any
        // author name this gate reads must be read together with its address.
        // A bare `%an` is a half-identity wherever it appears.
        foreach (Match match in new Regex(@"%an(?<addr> <%ae>)?").Matches(EnforceCode))
        {
            Assert.True(
                match.Groups["addr"].Success,
                $"`%an` at offset {match.Index} is read without `<%ae>`; the name alone is forgeable.");
        }
    }

    [Fact]
    public void EnforcesTheSpecCommitSubjectOnEveryVendorCommit()
    {
        Assert.Contains("SUBJECT=$(git log -1 --format='%s' \"$SHA\")", EnforceCode, StringComparison.Ordinal);
        Assert.Contains(@"grep -qE '^chore\(spec\):'", EnforceCode, StringComparison.Ordinal);
    }

    [Fact]
    public void KeepsTheMaintainerOverrideOnThePullRequestPathOnly()
    {
        // Labels exist only on pull_request events, so a direct push of a
        // hand-edited mirror has no way through the identity gate at all.
        var region = Between(EnforceCode, "OVERRIDE_APPLIED=0", "if [ \"$OVERRIDE_APPLIED\" -eq 1 ]; then");
        Assert.Contains("[ \"$EVENT_NAME\" = \"pull_request\" ]", region, StringComparison.Ordinal);
        Assert.Contains("OVERRIDE_LABEL: spec-mirror-override", Workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void IsNotDisarmedByContinueOnErrorOrAnAlwaysCondition()
    {
        Assert.DoesNotContain("continue-on-error", Workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("if: always()", Workflow, StringComparison.Ordinal);
    }

    private static string Between(string text, string start, string end)
    {
        var from = text.IndexOf(start, StringComparison.Ordinal);
        Assert.True(from >= 0, $"vendor-readonly.yml no longer contains the anchor: {start}");
        var to = text.IndexOf(end, from, StringComparison.Ordinal);
        Assert.True(to > from, $"vendor-readonly.yml no longer contains the anchor: {end}");
        return text.Substring(from, to - from);
    }
}
