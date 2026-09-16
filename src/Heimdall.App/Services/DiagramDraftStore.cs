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
using System.Security.Cryptography;
using System.Text;

namespace Heimdall.App.Services;

/// <summary>
/// Keeps the diagram the editor holds on disk, so work survives a crash.
/// </summary>
/// <remarks>
/// <para>The editor reports its content on every change, but that only ever
/// reached memory: if the application went away, so did everything since the
/// last explicit save. A draft is written beside the application's other data
/// and removed as soon as the document reaches its real file, so a draft that
/// is still there on open means the previous run did not finish.</para>
/// <para>A draft is keyed by the document's path, hashed, so two diagrams open
/// in two panes never overwrite one another's draft and no file name has to be
/// derived from a path. The unsaved document has its own key.</para>
/// </remarks>
internal sealed class DiagramDraftStore
{
    private const string DraftDirectoryName = "diagram-drafts";
    private const string DraftExtension = ".drawio";
    private const string UnsavedKey = "unsaved";

    private readonly string _directory;

    /// <summary>Creates a store under the application's own data directory.</summary>
    public DiagramDraftStore()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Heimdall",
            DraftDirectoryName))
    {
    }

    /// <summary>Creates a store rooted at <paramref name="directory"/>, for tests.</summary>
    public DiagramDraftStore(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = directory;
    }

    /// <summary>The file a document's draft is written to.</summary>
    public string DraftPathFor(string? documentPath)
    {
        return Path.Combine(_directory, KeyFor(documentPath) + DraftExtension);
    }

    /// <summary>
    /// Writes the draft for <paramref name="documentPath"/>, replacing any previous one.
    /// </summary>
    /// <returns>True when the draft reached the disk.</returns>
    public bool TryWrite(string? documentPath, string xml)
    {
        ArgumentNullException.ThrowIfNull(xml);

        try
        {
            Directory.CreateDirectory(_directory);
            File.WriteAllText(DraftPathFor(documentPath), xml, Encoding.UTF8);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Core.Logging.FileLogger.Warn($"[DiagramEditor] Could not write the draft: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Reads the draft left for <paramref name="documentPath"/> by a previous run.
    /// </summary>
    /// <returns>The draft, or null when there is none or it cannot be read.</returns>
    public string? TryRead(string? documentPath)
    {
        try
        {
            var path = DraftPathFor(documentPath);
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Core.Logging.FileLogger.Warn($"[DiagramEditor] Could not read the draft: {ex.Message}");
            return null;
        }
    }

    /// <summary>Removes the draft, which a successful save makes pointless.</summary>
    public void Discard(string? documentPath)
    {
        try
        {
            var path = DraftPathFor(documentPath);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Core.Logging.FileLogger.Warn($"[DiagramEditor] Could not discard the draft: {ex.Message}");
        }
    }

    /// <summary>
    /// A stable file-name-safe key for a document path. Hashed rather than
    /// escaped: a path can be longer than a file name may be, and two paths
    /// differing only in case must not collide on a case-insensitive volume.
    /// </summary>
    private static string KeyFor(string? documentPath)
    {
        if (string.IsNullOrWhiteSpace(documentPath))
        {
            return UnsavedKey;
        }

        var normalized = Path.GetFullPath(documentPath).ToUpperInvariant();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(hash, 0, 8).ToLowerInvariant();
    }
}
