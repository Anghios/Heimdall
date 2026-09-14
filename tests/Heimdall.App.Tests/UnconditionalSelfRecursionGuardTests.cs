/*
 * Copyright 2026 Julien Bombled
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using System.IO;
using System.Text.RegularExpressions;

namespace Heimdall.App.Tests;

/// <summary>
/// Refuses an expression-bodied method whose whole body is a call to itself with its own
/// parameters.
/// </summary>
/// <remarks>
/// <para>That shape has exactly one behaviour: it consumes the stack until the process is killed.
/// No handler runs, nothing is logged, and on a workstation whose error reporting is disabled by
/// policy nothing is saved either, so it is reported as an application that vanished.</para>
/// <para>It shipped. <c>EmbeddedSftpView.L(string key) =&gt; L(key)</c> was introduced on
/// 2026-09-07 by a refactor that replaced a hundred <c>_localizer?[key] ?? "fallback"</c>
/// expressions with a helper, and wrote the helper wrong. Every release from v2026.090701 to
/// v2026.091402 carried it, and it crashed the application every time the file browser reported a
/// disconnection - which is what a burst of dropped transports produces.</para>
/// <para>The refactor came with its own guard, and that guard passed: it asserted the English
/// fallbacks had been removed from the source. Nothing asserted that what replaced them worked.
/// This one measures the replacement.</para>
/// <para>The rule is narrow on purpose. Ordinary recursion terminates on a condition, and an
/// expression body has nowhere to put one: a method whose entire body is <c>Name(sameArguments)</c>
/// cannot terminate. Overload resolution is the one escape - a call that reaches a different
/// overload is not this shape - so the arguments must match the parameters by name for the match
/// to count.</para>
/// </remarks>
public sealed class UnconditionalSelfRecursionGuardTests
{
    // Signature, then an expression body that calls the same name. The parameter list and the
    // argument list are captured separately so they can be compared rather than assumed equal.
    //
    // The name must not be preceded by a dot. An explicit interface implementation is written
    // `void IFoo.Bar() => Bar();` and delegates to the class's own Bar, which is a different
    // method: the two sites in this repository that look exactly like this defect - MainWindow's
    // ToggleFullscreen and ServerListViewModel's RestoreServerAsync - are both that shape and both
    // correct. Matching them would make this guard cry wolf, and a guard that cries wolf is
    // deleted rather than heeded.
    private static readonly Regex Shape = new(
        @"(?<!\.)(?<name>\b[A-Z_a-z]\w*)\s*\((?<parameters>[^()]*)\)\s*=>\s*\k<name>\s*\((?<arguments>[^()]*)\)\s*;",
        RegexOptions.Compiled);

    [Fact]
    public void NoMethodInTheApplicationIsUnconditionallySelfRecursive()
    {
        string root = Path.Combine(RepoRoot(), "src");
        List<string> offenders = [];
        int scanned = 0;

        foreach (string file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            if (IsGenerated(file))
            {
                continue;
            }

            scanned++;
            foreach (string site in SelfRecursiveSitesIn(File.ReadAllText(file)))
            {
                offenders.Add($"{Path.GetRelativePath(root, file)}: {site}");
            }
        }

        Assert.True(scanned > 200, $"only {scanned} source files were scanned; the sweep is not reaching the tree");
        Assert.Empty(offenders);
    }

    /// <remarks>
    /// The sweep walks subdirectories, and a guard that silently stopped at the top level would
    /// pass while saying nothing about the views, which is where the shape shipped. Asserted
    /// separately because a file count alone does not prove the walk went down.
    /// </remarks>
    [Fact]
    public void TheSweepReachesTheViews()
    {
        string root = Path.Combine(RepoRoot(), "src");
        bool reached = Directory
            .EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Any(f => f.EndsWith(Path.Combine("Views", "EmbeddedSftpView.xaml.cs"), StringComparison.Ordinal));

        Assert.True(reached, "the sweep never reaches src/Heimdall.App/Views");
    }

    /// <remarks>
    /// The positive control: the exact line that shipped. Without it, an empty result cannot be
    /// told from a detector that matches nothing.
    /// </remarks>
    [Fact]
    public void TheDetectorRecognizesTheLineThatShipped()
    {
        string[] sites = [.. SelfRecursiveSitesIn("    private string L(string key) => L(key);")];

        Assert.Single(sites);
        Assert.Contains("L(key)", sites[0], StringComparison.Ordinal);
    }

    /// <remarks>
    /// The negative controls. A correct helper, a recursion that changes its argument, and a call
    /// that reaches a different overload must all be left alone, or the guard would forbid code
    /// that works and be deleted the first time it cried wolf.
    /// </remarks>
    [Theory]
    [InlineData("private string L(string key) => _localizer?[key] ?? key;")]
    [InlineData("private int Countdown(int n) => Countdown(n - 1);")]
    [InlineData("private string L(string key) => L(key, null);")]
    [InlineData("private string LF(string key, params object[] args) => _localizer?.Format(key, args) ?? key;")]
    [InlineData("void ISessionTabContextCallbacks.ToggleFullscreen()\n        => ToggleFullscreen();")]
    [InlineData("Task<bool> ISessionRestoreHost.RestoreServerAsync(string id, CancellationToken ct)\n        => RestoreServerAsync(id, ct);")]
    public void TheDetectorLeavesWorkingCodeAlone(string line)
        => Assert.Empty(SelfRecursiveSitesIn(line));

    /// <remarks>
    /// A parameterless method calling itself cannot terminate either, and the first version of
    /// this guard let it through: it required the parameter list to be non-empty, so
    /// <c>Toggle() =&gt; Toggle();</c> read as working code. Pinned so the exemption cannot come
    /// back as a simplification.
    /// </remarks>
    [Fact]
    public void TheDetectorRecognizesAParameterlessSelfCall()
        => Assert.Single(SelfRecursiveSitesIn("private void Toggle() => Toggle();"));

    private static IEnumerable<string> SelfRecursiveSitesIn(string source)
    {
        foreach (Match match in Shape.Matches(source))
        {
            string[] parameters = NamesOf(match.Groups["parameters"].Value);
            string[] arguments = Split(match.Groups["arguments"].Value);

            // A parameterless method calling itself is the purest form of the shape, not an
            // exception to it: an earlier version of this guard required at least one parameter
            // and would have passed `Toggle() => Toggle();` without a word.
            if (parameters.SequenceEqual(arguments, StringComparer.Ordinal))
            {
                yield return match.Value.Trim();
            }
        }
    }

    /// <summary>The declared name of each parameter, which is its last word.</summary>
    private static string[] NamesOf(string parameterList) =>
        [.. Split(parameterList).Select(p => p.Split(' ', StringSplitOptions.RemoveEmptyEntries) is { Length: > 0 } w
            ? w[^1]
            : string.Empty)];

    private static string[] Split(string list) =>
        [.. list.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

    private static bool IsGenerated(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
        || path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
        || path.EndsWith(".g.cs", StringComparison.Ordinal)
        || path.EndsWith(".g.i.cs", StringComparison.Ordinal);

    private static string RepoRoot()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir, "Heimdall.slnx")))
            {
                return dir;
            }

            dir = Path.GetDirectoryName(dir);
        }

        throw new DirectoryNotFoundException($"Cannot find repository root from {AppContext.BaseDirectory}");
    }
}
