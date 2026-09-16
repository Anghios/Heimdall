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

using Heimdall.App.Services;

namespace Heimdall.App.Tests;

/// <summary>
/// Covers the diagram editor's document record: what a save must do next, and
/// when there is unsaved work to warn about.
/// </summary>
/// <remarks>
/// The shipped editor had no such record. It kept one field holding "the last
/// XML the editor mentioned" and treated it as both the content and the proof
/// that the content was saved, so a diagram opened from Network Cartography was
/// written straight back into the temporary folder and closing a tab with
/// unsaved work said nothing.
/// </remarks>
public class DiagramDocumentStateTests
{
    private const string Xml = "<mxfile><diagram/></mxfile>";
    private const string OtherXml = "<mxfile><diagram name=\"edited\"/></mxfile>";
    private const string Path = @"C:\diagrams\topology.drawio";

    [Fact]
    public void NewDocument_IsNotDirtyAndHasNoLocation()
    {
        var state = new DiagramDocumentState();

        Assert.False(state.IsDirty);
        Assert.Null(state.FilePath);
        Assert.Null(state.EditorXml);
    }

    [Fact]
    public void NewDocument_SaveAsksTheEditorForItsContentFirst()
    {
        var state = new DiagramDocumentState();

        Assert.Equal(DiagramSaveStep.RequestContent, state.NextSaveStep(chooseNewLocation: false));
    }

    [Fact]
    public void DocumentOpenedFromDisk_IsClean()
    {
        var state = new DiagramDocumentState();
        state.OpenFromDisk(Path, Xml);

        Assert.False(state.IsDirty);
        Assert.Equal(Path, state.FilePath);
        Assert.Equal(DiagramSaveStep.Write, state.NextSaveStep(chooseNewLocation: false));
    }

    [Fact]
    public void DocumentOpenedFromDisk_TurnsDirtyWhenTheEditorReportsSomethingElse()
    {
        var state = new DiagramDocumentState();
        state.OpenFromDisk(Path, Xml);

        state.EditorReported(OtherXml);

        Assert.True(state.IsDirty);
    }

    [Fact]
    public void EditorReportingIdenticalContent_LeavesTheDocumentClean()
    {
        var state = new DiagramDocumentState();
        state.OpenFromDisk(Path, Xml);

        state.EditorReported(Xml);

        Assert.False(state.IsDirty);
    }

    /// <summary>
    /// A network map handed over for editing exists nowhere on disk. It must be
    /// dirty from the start, and its first save must ask for a location instead
    /// of writing back into whatever temporary file produced it.
    /// </summary>
    [Fact]
    public void UnsavedHandover_IsDirtyAndHasNoLocation()
    {
        var state = new DiagramDocumentState();
        state.OpenUnsaved(Xml);

        Assert.True(state.IsDirty);
        Assert.Null(state.FilePath);
        Assert.Equal(DiagramSaveStep.ChooseLocation, state.NextSaveStep(chooseNewLocation: false));
    }

    [Fact]
    public void WritingTheDocument_RecordsTheLocationAndClearsTheDirtyFlag()
    {
        var state = new DiagramDocumentState();
        state.OpenUnsaved(Xml);

        state.MarkWritten(Path, Xml);

        Assert.False(state.IsDirty);
        Assert.Equal(Path, state.FilePath);
        Assert.Equal(DiagramSaveStep.Write, state.NextSaveStep(chooseNewLocation: false));
    }

    [Fact]
    public void SaveAs_AsksForALocationEvenWhenTheDocumentHasOne()
    {
        var state = new DiagramDocumentState();
        state.OpenFromDisk(Path, Xml);

        Assert.Equal(DiagramSaveStep.ChooseLocation, state.NextSaveStep(chooseNewLocation: true));
    }

    [Fact]
    public void SaveAs_StillAsksTheEditorForContentWhenThereIsNone()
    {
        var state = new DiagramDocumentState();

        Assert.Equal(DiagramSaveStep.RequestContent, state.NextSaveStep(chooseNewLocation: true));
    }

    [Fact]
    public void EditingAfterASave_TurnsTheDocumentDirtyAgain()
    {
        var state = new DiagramDocumentState();
        state.OpenFromDisk(Path, Xml);
        state.EditorReported(OtherXml);
        state.MarkWritten(Path, OtherXml);

        Assert.False(state.IsDirty);

        state.EditorReported(Xml);

        Assert.True(state.IsDirty);
    }

    [Fact]
    public void Reset_ClearsEverything()
    {
        var state = new DiagramDocumentState();
        state.OpenFromDisk(Path, Xml);
        state.EditorReported(OtherXml);

        state.Reset();

        Assert.False(state.IsDirty);
        Assert.Null(state.FilePath);
        Assert.Null(state.EditorXml);
        Assert.Equal(DiagramSaveStep.RequestContent, state.NextSaveStep(chooseNewLocation: false));
    }

    /// <summary>
    /// An empty diagram is content, not the absence of it: the editor reports an
    /// empty document for a blank canvas, and saving it must still write a file.
    /// </summary>
    [Fact]
    public void EmptyContentFromTheEditor_CountsAsContent()
    {
        var state = new DiagramDocumentState();
        state.EditorReported(string.Empty);

        Assert.Equal(DiagramSaveStep.ChooseLocation, state.NextSaveStep(chooseNewLocation: false));
        Assert.True(state.IsDirty);
    }
}
