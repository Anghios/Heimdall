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

namespace Heimdall.App.Services;

/// <summary>A diagram to start from, shipped with the application.</summary>
/// <param name="FileName">File under the templates directory.</param>
/// <param name="NameKey">Locale key of the name shown in the menu.</param>
internal sealed record DiagramTemplate(string FileName, string NameKey)
{
    /// <summary>Reads the template, or null when it is missing or unreadable.</summary>
    public string? TryRead()
    {
        try
        {
            var path = Path.Combine(DiagramTemplates.Directory, FileName);
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Core.Logging.FileLogger.Warn($"[DiagramEditor] Could not read template '{FileName}': {ex.Message}");
            return null;
        }
    }
}

/// <summary>
/// The diagrams the editor offers as a starting point.
/// </summary>
/// <remarks>
/// A blank canvas is the worst first screen a diagram tool can show: it asks the
/// user to know draw.io before they can begin. These are deliberately small, so
/// they read as a shape to edit rather than as a drawing to dismantle.
/// </remarks>
internal static class DiagramTemplates
{
    /// <summary>Where the shipped templates live, beside the application.</summary>
    public static string Directory =>
        Path.Combine(AppContext.BaseDirectory, "Assets", "diagram-templates");

    private static readonly DiagramTemplate[] All =
    [
        new("network-segment.drawio", "ToolDiagramTemplateNetwork"),
        new("three-tier.drawio", "ToolDiagramTemplateThreeTier"),
        new("flow.drawio", "ToolDiagramTemplateFlow"),
    ];

    /// <summary>The templates present on disk, in menu order.</summary>
    public static IReadOnlyList<DiagramTemplate> Available()
    {
        if (!System.IO.Directory.Exists(Directory))
        {
            return [];
        }

        return All
            .Where(template => File.Exists(Path.Combine(Directory, template.FileName)))
            .ToList();
    }
}
