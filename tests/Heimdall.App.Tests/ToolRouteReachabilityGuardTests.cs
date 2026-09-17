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
using Heimdall.App.Services;
using Heimdall.App.Tests.Views.EmbeddedRdp;

namespace Heimdall.App.Tests;

/// <summary>
/// Every tool identifier written as a literal at a call site must name a tool the
/// registry can build.
/// </summary>
/// <remarks>
/// <para>An unknown identifier does not throw and does not fail a build. It is
/// logged as a warning and then <c>CreateToolControl</c> puts a tab on screen
/// carrying the untranslated text <c>Tool: &lt;id&gt;</c>. So the menu entry looks
/// ordinary, the click looks like it worked, and nothing except a person using the
/// application ever notices. That is exactly what <c>__ADD_SERVER__</c> did in the
/// cartography context menu, for as long as the entry existed.</para>
/// <para>Three diagram features shipped in the same shape during September 2026:
/// the code was right and the entry point was wrong. Unit tests prove the unit and
/// source guards prove the line is written; neither asks whether a gesture reaches
/// it. This guard asks the one version of that question a test can answer, which is
/// whether the destination exists at all.</para>
/// <para>What it cannot say: that the route is offered, that the callback behind it
/// is wired, or that a click arrives. A menu entry built on a null callback is
/// invisible to it, and only using the application settles that.</para>
/// </remarks>
public sealed class ToolRouteReachabilityGuardTests
{
    /// <summary>
    /// The ways a tool tab is asked for by identifier, followed by a literal id.
    /// Sites that pass a variable or a named constant are out of reach here and are
    /// left to the compiler.
    /// </summary>
    private static readonly Regex LiteralToolRoute = new(
        @"(?<caller>OpenToolTabAsync|openToolCallback|_openToolAction|openToolAction)\s*\(\s*""(?<id>[^""]+)""",
        RegexOptions.CultureInvariant);

    /// <summary>
    /// The census found 13 sites in six files when this was written. The floor is
    /// deliberately below that and above zero: a regex that stops matching would
    /// otherwise turn this file into a test that asserts nothing.
    /// </summary>
    private const int MinimumExpectedSites = 10;

    [Fact]
    public void EveryLiteralToolRoute_NamesAToolTheRegistryCanBuild()
    {
        var sites = CollectLiteralToolRoutes();

        Assert.True(sites.Count >= MinimumExpectedSites,
            $"Only {sites.Count} literal tool routes were found, expected at least "
                + $"{MinimumExpectedSites}. The pattern no longer matches the code it reads, so "
                + "this guard is passing without looking at anything.");

        var registry = new ToolRegistry();
        var dead = sites
            .Where(site => registry.GetById(site.ToolId) is null)
            .Select(site => $"{site.RelativePath}:{site.Line}: {site.Caller}(\"{site.ToolId}\")")
            .ToList();

        Assert.True(dead.Count == 0,
            "These call sites open a tool the registry does not know. Opening one puts a tab "
                + "reading \"Tool: <id>\" on screen instead of the tool:"
                + Environment.NewLine + string.Join(Environment.NewLine, dead));
    }

    /// <summary>
    /// The sweep must reach beyond the directory that happens to hold most of the
    /// call sites. A guard that only ever opened <c>Views/Tools</c> would have
    /// missed the Notes route in the main window, and said so in green.
    /// </summary>
    [Fact]
    public void TheSweep_ReachesBeyondTheToolViewsDirectory()
    {
        var sites = CollectLiteralToolRoutes();

        var directories = sites
            .Select(site => Path.GetDirectoryName(site.RelativePath) ?? string.Empty)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        Assert.True(directories.Count > 1,
            "Every literal tool route was found in one directory: "
                + string.Join(", ", directories)
                + ". The sweep is not recursing, so a dead route outside it would not be seen.");
    }

    private sealed record ToolRouteSite(string RelativePath, int Line, string Caller, string ToolId);

    private static List<ToolRouteSite> CollectLiteralToolRoutes()
    {
        string appRoot = Path.Combine(ViewSource.RepoRoot(), "src", "Heimdall.App");

        Assert.True(Directory.Exists(appRoot), $"Application source not found: {appRoot}");

        var sites = new List<ToolRouteSite>();

        foreach (string file in Directory.EnumerateFiles(appRoot, "*.cs", SearchOption.AllDirectories))
        {
            if (IsBuildOutput(file, appRoot))
            {
                continue;
            }

            string text = File.ReadAllText(file);
            foreach (Match match in LiteralToolRoute.Matches(text))
            {
                sites.Add(new ToolRouteSite(
                    Path.GetRelativePath(appRoot, file),
                    text.Take(match.Index).Count(c => c == '\n') + 1,
                    match.Groups["caller"].Value,
                    match.Groups["id"].Value));
            }
        }

        return sites;
    }

    private static bool IsBuildOutput(string file, string appRoot)
    {
        string relative = Path.GetRelativePath(appRoot, file);
        string[] segments = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return segments.Any(segment =>
            string.Equals(segment, "bin", StringComparison.OrdinalIgnoreCase)
                || string.Equals(segment, "obj", StringComparison.OrdinalIgnoreCase));
    }
}
