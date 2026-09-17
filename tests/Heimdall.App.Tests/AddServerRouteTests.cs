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
using Heimdall.App.Tests.Views.EmbeddedRdp;
using Heimdall.App.Views.Tools;
using Heimdall.Core.Models;

namespace Heimdall.App.Tests;

/// <summary>
/// The route that keeps a discovered host as a saved server: the delegate the tool
/// context carries, and the view that has to pick it up.
/// </summary>
/// <remarks>
/// <para>The cartography menu offered this entry from the start, and clicking it
/// asked for a tool called <c>__ADD_SERVER__</c>. No registry knew the name, so the
/// shell opened a tab reading <c>Tool: __ADD_SERVER__</c> and the host was never
/// saved. It now goes through its own callback.</para>
/// <para><see cref="ToolContext.AddServerAction"/> is typed as
/// <see cref="Delegate"/>, like its two neighbours, because the record lives in the
/// core and the shapes it carries are the shell's. The cost of that is a signature
/// that no compiler checks: a delegate of the wrong shape does not fail, it silently
/// reads as absent and the entry disappears. That case is pinned below.</para>
/// </remarks>
public sealed class AddServerRouteTests
{
    private static readonly SessionLaunchRequest AnyHost = new("10.0.0.5", 22, "SSH");

    [Fact]
    public void GetAddServerAction_IsNull_WhenTheContextCarriesNothing()
    {
        Assert.Null(ToolContextMenuHelper.GetAddServerAction(null));
        Assert.Null(ToolContextMenuHelper.GetAddServerAction(new ToolContext()));
    }

    /// <summary>
    /// A delegate of another shape is not a callback. It must read as absent rather
    /// than throw at the click, because the menu decides what to offer from it.
    /// </summary>
    [Fact]
    public void GetAddServerAction_IsNull_WhenTheDelegateHasAnotherShape()
    {
        var wrongShape = new Func<SessionLaunchRequest, Task>(_ => Task.CompletedTask);

        var resolved = ToolContextMenuHelper.GetAddServerAction(
            new ToolContext(AddServerAction: wrongShape));

        Assert.Null(resolved);
    }

    [Fact]
    public void GetAddServerAction_PassesTheSessionAndTheNameThrough()
    {
        SessionLaunchRequest? seenRequest = null;
        string? seenName = null;

        var callback = new Func<SessionLaunchRequest, string?, Task>((request, name) =>
        {
            seenRequest = request;
            seenName = name;
            return Task.CompletedTask;
        });

        var resolved = ToolContextMenuHelper.GetAddServerAction(
            new ToolContext(AddServerAction: callback));

        Assert.NotNull(resolved);
        resolved!(AnyHost, "printer-01");

        Assert.Same(AnyHost, seenRequest);
        Assert.Equal("printer-01", seenName);
    }

    /// <summary>
    /// The callback reaches the menu only if the view picks it out of the context it
    /// is initialized with. A tool that never resolves it silently loses the entry.
    /// </summary>
    [Fact]
    public void CartographyView_ResolvesTheAddServerCallbackWhenInitialized()
    {
        string logic = ViewSource.HandlerBody(
            ViewSource.WithoutCommentsAndLiterals(ReadAppSource(
                "Views/Tools/NetworkCartographyView.xaml.cs")),
            "public void Initialize(ToolContext? context, LocalizationManager? localizer)");

        Assert.True(
            ViewSource.IsStatementOfTheMethodBody(
                logic, "_addServerAction = ToolContextMenuHelper.GetAddServerAction(context);"),
            "the cartography view does not resolve the add-server callback as a step of its "
                + "initialization, so the menu entry that files a discovered host is never built");
    }

    private static string ReadAppSource(string relativePath)
    {
        string full = Path.Combine(
            ViewSource.RepoRoot(),
            "src",
            "Heimdall.App",
            relativePath.Replace('/', Path.DirectorySeparatorChar));

        Assert.True(File.Exists(full), $"Source not found: {full}");
        return File.ReadAllText(full);
    }
}
