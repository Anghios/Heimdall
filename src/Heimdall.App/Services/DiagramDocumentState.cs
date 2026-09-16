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

namespace Heimdall.App.Services;

/// <summary>
/// What the next step of a save must be, given the document's current state.
/// </summary>
internal enum DiagramSaveStep
{
    /// <summary>The editor has not reported any content yet: ask it for the XML first.</summary>
    RequestContent,

    /// <summary>There is content but no destination (new document, or Save As): ask for a path.</summary>
    ChooseLocation,

    /// <summary>Content and destination are known: write.</summary>
    Write
}

/// <summary>
/// Decision record of the diagram editor's document: where it lives, what the
/// editor last reported, and whether the two differ.
/// </summary>
/// <remarks>
/// The editor pushes its content on every change (autosave). The document is
/// dirty as soon as that content differs from what was last read from or
/// written to disk. A document handed over as unsaved content (a network map
/// opened for editing) has no location and is dirty from the start, so the
/// first save asks for a path and closing asks for a decision.
/// </remarks>
internal sealed class DiagramDocumentState
{
    private string? _persistedXml;

    /// <summary>Path of the file the document was read from or last written to; null while unsaved.</summary>
    public string? FilePath { get; private set; }

    /// <summary>Latest content reported by the editor; null until the editor reports or a document is opened.</summary>
    public string? EditorXml { get; private set; }

    /// <summary>True when the editor holds content that is not on disk.</summary>
    public bool IsDirty =>
        EditorXml is not null
        && !string.Equals(EditorXml, _persistedXml, StringComparison.Ordinal);

    /// <summary>Starts a blank document.</summary>
    public void Reset()
    {
        FilePath = null;
        EditorXml = null;
        _persistedXml = null;
    }

    /// <summary>Records a document read from <paramref name="path"/>.</summary>
    public void OpenFromDisk(string path, string xml)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(xml);

        FilePath = path;
        EditorXml = xml;
        _persistedXml = xml;
    }

    /// <summary>Records content that exists nowhere on disk yet.</summary>
    public void OpenUnsaved(string xml)
    {
        ArgumentNullException.ThrowIfNull(xml);

        FilePath = null;
        EditorXml = xml;
        _persistedXml = null;
    }

    /// <summary>Records the content the editor just reported.</summary>
    public void EditorReported(string xml)
    {
        ArgumentNullException.ThrowIfNull(xml);

        EditorXml = xml;
    }

    /// <summary>Records a successful write of <paramref name="xml"/> to <paramref name="path"/>.</summary>
    public void MarkWritten(string path, string xml)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(xml);

        FilePath = path;
        EditorXml = xml;
        _persistedXml = xml;
    }

    /// <summary>
    /// Decides the next step of a save. <paramref name="chooseNewLocation"/> is the
    /// Save As intent: it forces a location prompt even when the document has one.
    /// </summary>
    public DiagramSaveStep NextSaveStep(bool chooseNewLocation)
    {
        if (EditorXml is null)
        {
            return DiagramSaveStep.RequestContent;
        }

        return chooseNewLocation || FilePath is null
            ? DiagramSaveStep.ChooseLocation
            : DiagramSaveStep.Write;
    }
}
