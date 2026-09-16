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
using System.Text.Json;

namespace Heimdall.App.Services;

/// <summary>
/// The diagrams opened most recently, so the tool can offer them again.
/// </summary>
/// <remarks>
/// The editor had no memory at all: every session started at an empty canvas and
/// every open went through a file dialog, while the Notes tool has had its own
/// folder from the start. The list is small, plain, and rewritten whole, because
/// nothing here is worth a more careful format.
/// </remarks>
internal sealed class DiagramRecentFiles
{
    /// <summary>How many entries are kept. Beyond this a list stops being a shortcut.</summary>
    public const int MaxEntries = 8;

    private const string FileName = "diagram-recent.json";

    private readonly string _path;

    /// <summary>Creates a list under the application's own data directory.</summary>
    public DiagramRecentFiles()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Heimdall",
            FileName))
    {
    }

    /// <summary>Creates a list stored at <paramref name="path"/>, for tests.</summary>
    public DiagramRecentFiles(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = path;
    }

    /// <summary>
    /// The remembered paths, most recent first, with anything that has since been
    /// deleted or moved left out.
    /// </summary>
    public IReadOnlyList<string> Read()
    {
        return ReadRaw().Where(File.Exists).ToList();
    }

    /// <summary>
    /// Puts <paramref name="documentPath"/> at the top, removing any earlier
    /// mention of the same file and trimming the list to <see cref="MaxEntries"/>.
    /// </summary>
    public void Remember(string documentPath)
    {
        if (string.IsNullOrWhiteSpace(documentPath))
        {
            return;
        }

        try
        {
            var full = Path.GetFullPath(documentPath);
            var entries = new List<string> { full };

            entries.AddRange(ReadRaw()
                .Where(path => !string.Equals(path, full, StringComparison.OrdinalIgnoreCase))
                .Take(MaxEntries - 1));

            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(entries));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            Core.Logging.FileLogger.Warn($"[DiagramEditor] Could not record the recent diagram: {ex.Message}");
        }
    }

    private List<string> ReadRaw()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return [];
            }

            return JsonSerializer.Deserialize<List<string>>(File.ReadAllText(_path)) ?? [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Core.Logging.FileLogger.Warn($"[DiagramEditor] Could not read the recent diagrams: {ex.Message}");
            return [];
        }
    }
}
