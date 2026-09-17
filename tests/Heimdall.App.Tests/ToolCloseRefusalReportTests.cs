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
using Heimdall.App.Services;
using Heimdall.Core.Localization;
using Heimdall.Core.Models;

namespace Heimdall.App.Tests;

/// <summary>
/// What the shell says when a tool refuses to close, and when it says nothing.
/// </summary>
/// <remarks>
/// <para>Every refusal used to read the same: "the tool X is busy and cannot be
/// closed right now". True of the tools that refuse while a scan runs. Not true of
/// the diagram editor, which refuses when the user cancels its save prompt, and was
/// telling them a tool was busy a moment after they were the reason. Not true of
/// the notes tool either, which refuses when the note could not be written, and was
/// hiding the failure behind the word busy.</para>
/// <para>The silent case is a decision, not an omission. The user answered a
/// question one gesture ago; a message is not information, it is a contradiction.
/// </para>
/// </remarks>
public sealed class ToolCloseRefusalReportTests
{
    private const string MergeBusyKey = "SplitMergeBlockedByTool";

    [Fact]
    public void ABusyTool_KeepsTheSurfaceOwnWording()
    {
        Assert.Equal(
            CloseGuardLocaleKeys.BlockedTool,
            ToolCloseRefusalReport.ReasonKeyFor(
                ToolCloseRefusal.Busy, CloseGuardLocaleKeys.BlockedTool));

        // A blocked merge is not a blocked close and says so, which is why the busy
        // wording is the caller's and not this method's.
        Assert.Equal(
            MergeBusyKey,
            ToolCloseRefusalReport.ReasonKeyFor(ToolCloseRefusal.Busy, MergeBusyKey));
    }

    [Fact]
    public void AUserWhoDeclined_IsToldNothing()
    {
        Assert.Null(ToolCloseRefusalReport.ReasonKeyFor(
            ToolCloseRefusal.UserDeclined, CloseGuardLocaleKeys.BlockedTool));
        Assert.Null(ToolCloseRefusalReport.ReasonKeyFor(
            ToolCloseRefusal.UserDeclined, MergeBusyKey));

        PaneCloseResult refused = ToolCloseRefusalReport.Refused(ToolCloseRefusal.UserDeclined);

        Assert.Equal(PaneCloseOutcome.Blocked, refused.Outcome);
        Assert.Null(refused.ReasonKey);
    }

    /// <summary>
    /// A failed save is neither expected nor already on screen, so it is the one
    /// refusal that replaces the surface's own wording rather than keeping it.
    /// </summary>
    [Fact]
    public void AFailedSave_SaysSoOnEverySurface()
    {
        Assert.Equal(
            CloseGuardLocaleKeys.BlockedSaveFailed,
            ToolCloseRefusalReport.ReasonKeyFor(
                ToolCloseRefusal.SaveFailed, CloseGuardLocaleKeys.BlockedTool));
        Assert.Equal(
            CloseGuardLocaleKeys.BlockedSaveFailed,
            ToolCloseRefusalReport.ReasonKeyFor(ToolCloseRefusal.SaveFailed, MergeBusyKey));

        PaneCloseResult refused = ToolCloseRefusalReport.Refused(ToolCloseRefusal.SaveFailed);

        Assert.Equal(CloseGuardLocaleKeys.BlockedSaveFailed, refused.ReasonKey);
    }

    /// <summary>
    /// The default of <see cref="IToolView.CloseRefusal"/> is what the forty-odd
    /// tools that never think about it get, and it must be the message they had.
    /// </summary>
    [Fact]
    public void AToolThatSaysNothingAboutItsRefusal_IsStillReportedAsBusy()
    {
        IToolView tool = new SilentAboutItView();

        Assert.Equal(ToolCloseRefusal.Busy, tool.CloseRefusal);
        Assert.Equal(
            CloseGuardLocaleKeys.BlockedTool,
            ToolCloseRefusalReport.Refused(tool.CloseRefusal).ReasonKey);
    }

    /// <summary>
    /// Both messages must exist in both catalogues and both must take the one
    /// placeholder the reporting sites fill with the pane title.
    /// </summary>
    [Theory]
    [InlineData("en")]
    [InlineData("fr")]
    public async Task BothRefusalMessages_AreTranslatedAndNameTheTool(string locale)
    {
        LocalizationManager localizer = new();
        await localizer.LoadAsync(Path.Combine(AppContext.BaseDirectory, "locales"), locale);

        foreach (string key in new[] { CloseGuardLocaleKeys.BlockedTool, CloseGuardLocaleKeys.BlockedSaveFailed })
        {
            string message = localizer.Format(key, "my-diagram");

            Assert.NotEqual(key, message);
            Assert.Contains("my-diagram", message, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The two tools that refuse for a reason other than being busy must say so.
    /// The mapping above is unreachable from the application otherwise: a tool that
    /// never sets its refusal keeps the default, and the shell keeps the old message.
    /// </summary>
    [Theory]
    [InlineData(
        "Views/Tools/DiagramEditorView.xaml.cs",
        "public bool CanClose()",
        "CloseRefusal = allowed ? ToolCloseRefusal.Busy : ToolCloseRefusal.UserDeclined;")]
    [InlineData(
        "Views/Tools/NotesToolView.xaml.cs",
        "public bool CanClose()",
        "CloseRefusal = saved ? ToolCloseRefusal.Busy : ToolCloseRefusal.SaveFailed;")]
    public void AToolThatRefusesForItsOwnReason_RecordsItWhereItRefuses(
        string relativePath, string signature, string statement)
    {
        string logic = Views.EmbeddedRdp.ViewSource.HandlerBody(
            Views.EmbeddedRdp.ViewSource.WithoutCommentsAndLiterals(ReadAppSource(relativePath)),
            signature);

        Assert.True(
            Views.EmbeddedRdp.ViewSource.IsStatementOfTheMethodBody(logic, statement),
            $"{relativePath} does not record why it refused as a step of CanClose(), so the shell "
                + "falls back to telling the user the tool is busy");
    }

    private static string ReadAppSource(string relativePath)
    {
        string full = Path.Combine(
            Views.EmbeddedRdp.ViewSource.RepoRoot(),
            "src",
            "Heimdall.App",
            relativePath.Replace('/', Path.DirectorySeparatorChar));

        Assert.True(File.Exists(full), $"Source not found: {full}");
        return File.ReadAllText(full);
    }

    private sealed class SilentAboutItView : IToolView
    {
        public void Initialize(ToolContext? context, LocalizationManager? localizer)
        {
        }

        public bool CanClose() => false;

        public void Dispose()
        {
        }
    }
}
