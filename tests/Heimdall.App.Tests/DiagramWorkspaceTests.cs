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

namespace Heimdall.App.Tests;

/// <summary>
/// Covers what the diagram editor remembers between runs: the draft that
/// survives a crash, and the diagrams opened recently.
/// </summary>
/// <remarks>
/// The editor used to remember nothing at all. Its autosave reached memory only,
/// so a crash took the work with it, and every open started from a file dialog.
/// </remarks>
public sealed class DiagramWorkspaceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "heimdall-diagram-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // A leftover temporary directory is not worth failing a test over.
        }
    }

    private DiagramDraftStore Drafts() => new(Path.Combine(_root, "drafts"));

    private DiagramRecentFiles Recents() => new(Path.Combine(_root, "recent.json"));

    // -- Drafts ---------------------------------------------------------

    [Fact]
    public void Draft_ComesBackAsItWasWritten()
    {
        var drafts = Drafts();
        drafts.TryWrite(@"C:\diagrams\a.drawio", "<mxfile/>");

        Assert.Equal("<mxfile/>", drafts.TryRead(@"C:\diagrams\a.drawio"));
    }

    [Fact]
    public void Draft_IsAbsentUntilOneIsWritten()
    {
        Assert.Null(Drafts().TryRead(@"C:\diagrams\never-seen.drawio"));
    }

    [Fact]
    public void Draft_OfOneDocumentIsNotTheDraftOfAnother()
    {
        var drafts = Drafts();
        drafts.TryWrite(@"C:\diagrams\a.drawio", "first");
        drafts.TryWrite(@"C:\diagrams\b.drawio", "second");

        Assert.Equal("first", drafts.TryRead(@"C:\diagrams\a.drawio"));
        Assert.Equal("second", drafts.TryRead(@"C:\diagrams\b.drawio"));
    }

    /// <summary>An unsaved document has no path, and still needs its own draft.</summary>
    [Fact]
    public void Draft_OfAnUnsavedDocumentHasItsOwnPlace()
    {
        var drafts = Drafts();
        drafts.TryWrite(null, "unsaved work");
        drafts.TryWrite(@"C:\diagrams\a.drawio", "saved work");

        Assert.Equal("unsaved work", drafts.TryRead(null));
    }

    [Fact]
    public void Draft_IsGoneOnceDiscarded()
    {
        var drafts = Drafts();
        drafts.TryWrite(@"C:\diagrams\a.drawio", "<mxfile/>");

        drafts.Discard(@"C:\diagrams\a.drawio");

        Assert.Null(drafts.TryRead(@"C:\diagrams\a.drawio"));
    }

    [Fact]
    public void Draft_DiscardingOneThatIsNotThereIsHarmless()
    {
        Drafts().Discard(@"C:\diagrams\never-seen.drawio");
    }

    /// <summary>
    /// Windows paths differing only in case name the same file, so they must not
    /// end up with two drafts that overwrite one another unpredictably.
    /// </summary>
    [Fact]
    public void Draft_TreatsPathsDifferingOnlyInCaseAsOneDocument()
    {
        var drafts = Drafts();

        Assert.Equal(
            drafts.DraftPathFor(@"C:\Diagrams\A.drawio"),
            drafts.DraftPathFor(@"c:\diagrams\a.drawio"));
    }

    // -- Recent files ---------------------------------------------------

    [Fact]
    public void Recent_IsEmptyBeforeAnythingIsOpened()
    {
        Assert.Empty(Recents().Read());
    }

    [Fact]
    public void Recent_PutsTheLatestFirst()
    {
        var first = CreateFile("first.drawio");
        var second = CreateFile("second.drawio");

        var recents = Recents();
        recents.Remember(first);
        recents.Remember(second);

        Assert.Equal([second, first], recents.Read());
    }

    [Fact]
    public void Recent_MentionsAFileOnlyOnce()
    {
        var path = CreateFile("a.drawio");
        var other = CreateFile("b.drawio");

        var recents = Recents();
        recents.Remember(path);
        recents.Remember(other);
        recents.Remember(path);

        Assert.Equal([path, other], recents.Read());
    }

    [Fact]
    public void Recent_StopsAtItsLimit()
    {
        var recents = Recents();
        var paths = Enumerable.Range(0, DiagramRecentFiles.MaxEntries + 4)
            .Select(index => CreateFile($"d{index}.drawio"))
            .ToList();

        foreach (var path in paths)
        {
            recents.Remember(path);
        }

        Assert.Equal(DiagramRecentFiles.MaxEntries, recents.Read().Count);
    }

    /// <summary>A file the user has since deleted is not a shortcut, it is a dead end.</summary>
    [Fact]
    public void Recent_LeavesOutWhatIsNoLongerThere()
    {
        var kept = CreateFile("kept.drawio");
        var removed = CreateFile("removed.drawio");

        var recents = Recents();
        recents.Remember(kept);
        recents.Remember(removed);
        File.Delete(removed);

        Assert.Equal([kept], recents.Read());
    }

    [Fact]
    public void Recent_IgnoresAnEmptyPath()
    {
        var recents = Recents();
        recents.Remember("   ");

        Assert.Empty(recents.Read());
    }

    private string CreateFile(string name)
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, name);
        File.WriteAllText(path, "<mxfile/>");
        return path;
    }
}
