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
using System.Reflection;
using System.Text.RegularExpressions;
using Heimdall.App.Tests.Views.EmbeddedRdp;

namespace Heimdall.App.Tests;

/// <summary>
/// Each WebView2 surface's virtual host mapping exposes its asset folder at the
/// access level that was measured for it, and no view decides that on its own.
/// </summary>
/// <remarks>
/// <para><c>Allow</c> serves the mapped folder to any origin; <c>DenyCors</c> serves
/// it only to a same-origin document, which is what each of these pages is. The
/// difference is invisible until it is not, so each surface was driven under the
/// value it is pinned at here, and none of them is pinned on reasoning alone.</para>
/// <para>Changing one of these is changing a value under test, which is the point:
/// it cannot drift back quietly, and it cannot be hardened without someone running
/// the surface first.</para>
/// </remarks>
public sealed class WebViewAssetAccessGuardTests
{
    /// <summary>
    /// A call site spelling the enum itself, instead of naming the decision, would
    /// carry no record of what was measured and would drift from its neighbours.
    /// </summary>
    private static readonly Regex MappingWithALiteralKind = new(
        @"SetVirtualHostNameToFolderMapping\s*\([^;]*CoreWebView2HostResourceAccessKind\s*\.",
        RegexOptions.Singleline | RegexOptions.CultureInvariant);

    /// <remarks>
    /// The access kind is named rather than typed because the test project carries no
    /// reference to WebView2; the enum comes back off the field itself.
    /// </remarks>
    [Theory]
    [InlineData("Diagram", "DenyCors")]
    [InlineData("MarkdownEditor", "DenyCors")]
    [InlineData("Vnc", "DenyCors")]
    public void EachSurface_KeepsTheAccessThatWasMeasuredForIt(string surface, string expected)
    {
        Type decisions = typeof(Heimdall.App.Views.EmbeddedVncView).Assembly
            .GetType("Heimdall.App.Views.WebViewAssetAccess", throwOnError: true)!;

        FieldInfo? field = decisions.GetField(
            surface, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);

        Assert.True(field is not null, $"No access decision named '{surface}'.");
        Assert.True(field!.FieldType.IsEnum,
            $"'{surface}' is not an access kind but a {field.FieldType.Name}.");

        Assert.Equal(expected, Enum.GetName(field.FieldType, field.GetRawConstantValue()!));
    }

    /// <summary>
    /// Every mapping goes through a named decision. A view that passes the enum
    /// straight in has decided alone, with nothing saying whether anyone ran it.
    /// </summary>
    [Fact]
    public void NoViewDecidesItsOwnAccessLevel()
    {
        string viewsRoot = Path.Combine(
            ViewSource.RepoRoot(), "src", "Heimdall.App", "Views");

        Assert.True(Directory.Exists(viewsRoot), $"Views directory not found: {viewsRoot}");

        var mappings = new List<string>();
        var offenders = new List<string>();

        foreach (string file in Directory.EnumerateFiles(viewsRoot, "*.cs", SearchOption.AllDirectories))
        {
            string text = File.ReadAllText(file);
            string relative = Path.GetRelativePath(viewsRoot, file);

            if (!text.Contains("SetVirtualHostNameToFolderMapping", StringComparison.Ordinal))
            {
                continue;
            }

            mappings.Add(relative);

            if (MappingWithALiteralKind.IsMatch(text))
            {
                offenders.Add(relative);
            }
        }

        // The sweep must still find the surfaces it is meant to read. Three today; a
        // fourth is welcome, zero means the search stopped matching and this test has
        // been passing over nothing.
        Assert.True(mappings.Count >= 3,
            $"Only {mappings.Count} virtual host mappings were found under {viewsRoot}, "
                + "so this guard is no longer reading the code it describes.");

        Assert.True(offenders.Count == 0,
            "These views pass an access kind straight to the mapping instead of naming a measured "
                + "decision in WebViewAssetAccess:"
                + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }
}
