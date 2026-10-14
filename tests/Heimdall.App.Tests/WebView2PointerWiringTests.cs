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
/// The pointer switch reaches every WebView2 environment the application creates.
/// </summary>
/// <remarks>
/// <see cref="WebView2PointerTests"/> pins the text of the switch. It stays green when the switch
/// is composed and then never handed to WebView2: deleting <c>options: options</c> from
/// <c>CreateEnvironmentAsync</c> brings the hidden pointer back to SSH, VNC and the editors, and
/// passing the route's <c>arguments</c> instead of <c>EmbeddedBrowserArguments(arguments)</c> brings
/// it back to the web consoles. Both mutations turn this file red. A new environment created
/// anywhere else would bypass the helper, so that is refused too.
/// </remarks>
public sealed class WebView2PointerWiringTests
{
    private const string CreateCall = "CoreWebView2Environment.CreateAsync(";

    [Fact]
    public void EveryEnvironment_IsCreatedWithTheEmbeddedArguments()
    {
        string source = File.ReadAllText(HelperPath());
        string[] calls = CallArgumentLists(source).ToArray();

        // Two today: the shared environment and the web console's own. Fewer means the sweep
        // stopped seeing a call, and checking nothing reads as a pass.
        Assert.True(calls.Length >= 2, $"Found {calls.Length} environment creation(s) in WebView2Helper.cs.");
        Assert.All(calls, call => Assert.Contains("options: options", call, StringComparison.Ordinal));

        int composed = Regex.Matches(
            source,
            @"additionalBrowserArguments:\s*EmbeddedBrowserArguments\(").Count;
        Assert.Equal(calls.Length, composed);
    }

    [Fact]
    public void NoEnvironment_IsCreatedOutsideTheHelper()
    {
        string sourceRoot = Path.Combine(FindRepositoryRoot(), "src");
        string helper = Path.GetFullPath(HelperPath());

        string[] elsewhere = Directory
            .EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(file => !string.Equals(Path.GetFullPath(file), helper, StringComparison.OrdinalIgnoreCase))
            .Where(file => File.ReadAllText(file).Contains(CreateCall, StringComparison.Ordinal))
            .Select(file => Path.GetRelativePath(sourceRoot, file))
            .ToArray();

        Assert.Empty(elsewhere);
    }

    /// <summary>The text between each creation call's parenthesis and the end of its statement.</summary>
    private static IEnumerable<string> CallArgumentLists(string source)
    {
        int index = source.IndexOf(CreateCall, StringComparison.Ordinal);
        while (index >= 0)
        {
            int end = source.IndexOf(';', index);
            Assert.True(end > index, "An environment creation call has no end.");
            yield return source[index..end];
            index = source.IndexOf(CreateCall, end, StringComparison.Ordinal);
        }
    }

    private static string HelperPath() =>
        Path.Combine(FindRepositoryRoot(), "src", "Heimdall.App", "Services", "WebView2Helper.cs");

    private static string FindRepositoryRoot()
    {
        string? directory = AppContext.BaseDirectory;
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory, "Heimdall.slnx")))
            {
                return directory;
            }

            directory = Path.GetDirectoryName(directory);
        }

        throw new DirectoryNotFoundException(
            $"Cannot find repository root containing Heimdall.slnx from {AppContext.BaseDirectory}.");
    }
}
