using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace EzClassify.Tests;

/// <summary>
/// Extracts the workflow's shell steps for shape assertions and Git-history
/// scenarios. Shape assertions use <see cref="CodeOf"/> to exclude comments
/// that describe a construct without executing it.
/// </summary>
internal static class VendorReadonlyWorkflow
{
    /// <summary>Repository-relative path of the gated workflow.</summary>
    public const string RelativePath = ".github/workflows/vendor-readonly.yml";

    /// <summary>Root of the checkout, located by the solution file.</summary>
    public static string RepositoryRoot { get; } = FindRepositoryRoot();

    /// <summary>Absolute path of the workflow in the checkout under test.</summary>
    public static string FilePath { get; } = Path.Combine(
        RepositoryRoot,
        RelativePath.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>Full workflow text, newline-normalized. Includes comments.</summary>
    public static string Text { get; } = ReadText();

    /// <summary>
    /// The two shell steps in file order: range resolution followed by identity
    /// and subject enforcement. Extraction checks the count because callers
    /// address the steps by index.
    /// </summary>
    public static IReadOnlyList<string> RunBodies { get; } = ExtractRunBodies(Text);

    /// <summary>The range-resolution step (writes mode/base/head).</summary>
    public static string RangeStep => RunBodies[0];

    /// <summary>The enforcement step (identity gate, then subject gate).</summary>
    public static string EnforcementStep => RunBodies[1];

    /// <summary>
    /// Strips whole-line comments from a shell body, leaving the executable
    /// text. Trailing comments on a code line are left alone: they cannot make
    /// an assertion pass on its own documentation, because the code they follow
    /// is still on the line.
    /// </summary>
    /// <param name="body">A shell body from <see cref="RunBodies"/>.</param>
    /// <returns>The body with comment-only lines removed.</returns>
    public static string CodeOf(string body) => string.Join(
        "\n",
        body.Split('\n').Where(line => !line.TrimStart().StartsWith("#", StringComparison.Ordinal)));

    private static string ReadText()
    {
        Assert.True(File.Exists(FilePath), $"vendor-readonly.yml is missing at {FilePath}.");
        return File.ReadAllText(FilePath).Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    /// <summary>
    /// Extracts every <c>run: |</c> block scalar and dedents it by the block's
    /// own minimum indent, which is what the Actions runner hands to bash.
    /// </summary>
    /// <param name="workflow">Newline-normalized workflow text.</param>
    /// <returns>The dedented shell bodies, in file order.</returns>
    private static IReadOnlyList<string> ExtractRunBodies(string workflow)
    {
        var lines = workflow.Split('\n');
        var bodies = new List<string>();

        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].Trim() != "run: |") continue;

            var keyIndent = IndentOf(lines[i]);
            var block = new List<string>();
            for (var j = i + 1; j < lines.Length; j++)
            {
                if (lines[j].Trim().Length == 0)
                {
                    block.Add(string.Empty);
                    continue;
                }

                if (IndentOf(lines[j]) <= keyIndent) break;
                block.Add(lines[j]);
            }

            var minIndent = block
                .Where(line => line.Length > 0)
                .Select(IndentOf)
                .DefaultIfEmpty(0)
                .Min();

            bodies.Add(string.Join(
                "\n",
                block.Select(line => line.Length >= minIndent ? line.Substring(minIndent) : line)));
        }

        Assert.Equal(2, bodies.Count);
        Assert.All(bodies, body => Assert.Contains("set -euo pipefail", body, StringComparison.Ordinal));
        return bodies;
    }

    private static int IndentOf(string line) => line.Length - line.TrimStart(' ').Length;

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "EzClassify.Rest.sln"))) return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Cannot locate EzClassify.Rest.sln");
    }
}
