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

using System.Globalization;
using System.Xml.Linq;

namespace Heimdall.Core.Discovery;

/// <summary>What a merge did, so the tool can say it in one line.</summary>
/// <param name="Updated">Cells the scan refreshed in place.</param>
/// <param name="Added">Cells the scan introduced.</param>
/// <param name="Missing">Cells the scan no longer reports, kept and marked.</param>
/// <param name="Untouched">Cells the user drew, left exactly as they were.</param>
public sealed record DiagramMergeSummary(int Updated, int Added, int Missing, int Untouched);

/// <summary>
/// Merges a freshly exported network scan into a diagram the user has already
/// arranged, instead of replacing it.
/// </summary>
/// <remarks>
/// <para>Re-exporting a scan used to throw away every position, colour and
/// annotation the user had put in. It could not do otherwise: cell identifiers
/// were counters, so the same host came back under a different name each time.
/// <see cref="DrawIoExporter"/> now derives them from the host address, which is
/// what makes this possible.</para>
/// <para>Three rules, in order:</para>
/// <list type="number">
/// <item>A cell the scan reports and the diagram already has keeps its geometry
/// and takes the scan's label and style. The user's layout survives; the data is
/// current.</item>
/// <item>A cell the user drew, meaning one whose identifier Heimdall did not
/// issue, is never touched.</item>
/// <item>A Heimdall cell the scan no longer reports is kept and marked, because
/// a host that has gone is a finding, not a mistake to erase.</item>
/// </list>
/// </remarks>
public static class DiagramScanMerge
{
    /// <summary>Appended to the style of a cell the latest scan did not report.</summary>
    public const string MissingHostStyleSuffix = "opacity=40;dashed=1;";

    /// <summary>The two cells every mxGraph document opens with: the model root and its default layer.</summary>
    private static readonly string[] StructuralIds = ["0", "1"];

    private const string MxCell = "mxCell";
    private const string UserObject = "UserObject";
    private const string Geometry = "mxGeometry";
    private const string IdAttribute = "id";
    private const string StyleAttribute = "style";
    private const string ValueAttribute = "value";
    private const string LabelAttribute = "label";
    private const string LinkAttribute = "link";

    /// <summary>
    /// Merges <paramref name="incomingXml"/> into <paramref name="existingXml"/>.
    /// </summary>
    /// <param name="existingXml">The diagram as the user has it now.</param>
    /// <param name="incomingXml">A diagram freshly produced by <see cref="DrawIoExporter"/>.</param>
    /// <param name="summary">What the merge did.</param>
    /// <returns>The merged diagram.</returns>
    /// <exception cref="System.Xml.XmlException">Either document is not XML.</exception>
    public static string Merge(string existingXml, string incomingXml, out DiagramMergeSummary summary)
    {
        ArgumentNullException.ThrowIfNull(existingXml);
        ArgumentNullException.ThrowIfNull(incomingXml);

        var existing = XDocument.Parse(existingXml);
        var incoming = XDocument.Parse(incomingXml);

        var existingRoot = FindRoot(existing);
        var incomingRoot = FindRoot(incoming);

        if (existingRoot is null || incomingRoot is null)
        {
            // Nothing recognisable to merge into: the fresh scan stands alone.
            summary = new DiagramMergeSummary(0, CountHeimdallCells(incoming), 0, 0);
            return incomingXml;
        }

        var existingById = existingRoot.Elements()
            .Where(e => Identifier(e) is not null)
            .GroupBy(Identifier!)
            .ToDictionary(g => g.Key!, g => g.First(), StringComparer.Ordinal);

        var updated = 0;
        var added = 0;
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var incomingCell in incomingRoot.Elements().ToList())
        {
            var id = Identifier(incomingCell);
            if (id is null)
            {
                continue;
            }

            seen.Add(id);

            if (existingById.TryGetValue(id, out var existingCell))
            {
                RefreshInPlace(existingCell, incomingCell);
                if (!IsStructural(id))
                {
                    updated++;
                }
            }
            else
            {
                existingRoot.Add(new XElement(incomingCell));
                added++;
            }
        }

        var missing = 0;
        var untouched = 0;

        foreach (var existingCell in existingRoot.Elements())
        {
            var id = Identifier(existingCell);
            if (id is null || seen.Contains(id) || IsStructural(id))
            {
                continue;
            }

            if (IsHeimdallCell(id))
            {
                MarkMissing(existingCell);
                missing++;
            }
            else
            {
                untouched++;
            }
        }

        summary = new DiagramMergeSummary(updated, added, missing, untouched);
        return existing.ToString(SaveOptions.None);
    }

    /// <summary>The model root and its default layer, which carry no content.</summary>
    private static bool IsStructural(string id) => StructuralIds.Contains(id, StringComparer.Ordinal);

    /// <summary>
    /// Whether <paramref name="id"/> was issued by <see cref="DrawIoExporter"/>.
    /// Anything else belongs to the user and is left alone.
    /// </summary>
    public static bool IsHeimdallCell(string? id)
    {
        return id is not null
            && (id.StartsWith(DrawIoExporter.HostCellPrefix, StringComparison.Ordinal)
                || id.StartsWith(DrawIoExporter.LaneCellPrefix, StringComparison.Ordinal)
                || id.StartsWith(DrawIoExporter.SegmentCellPrefix, StringComparison.Ordinal)
                || id.StartsWith(DrawIoExporter.EdgeCellPrefix, StringComparison.Ordinal));
    }

    /// <summary>
    /// Takes the scan's label, style and link, and keeps everything the user
    /// decided: where the cell sits, and how big it is.
    /// </summary>
    private static void RefreshInPlace(XElement existingCell, XElement incomingCell)
    {
        CopyAttribute(incomingCell, existingCell, ValueAttribute);
        CopyAttribute(incomingCell, existingCell, LabelAttribute);
        CopyAttribute(incomingCell, existingCell, LinkAttribute);

        var incomingStyle = StyleOf(incomingCell);
        if (incomingStyle is not null)
        {
            SetStyle(existingCell, incomingStyle);
        }

        // Geometry is the user's layout decision and is never overwritten. A cell
        // that somehow has none takes the scan's, so it is not left unplaceable.
        if (FindGeometry(existingCell) is null && FindGeometry(incomingCell) is { } incomingGeometry)
        {
            var target = CellElement(existingCell);
            target?.Add(new XElement(incomingGeometry));
        }
    }

    private static void MarkMissing(XElement cell)
    {
        var style = StyleOf(cell);
        if (style is null || style.Contains(MissingHostStyleSuffix, StringComparison.Ordinal))
        {
            return;
        }

        var separator = style.EndsWith(';') ? string.Empty : ";";
        SetStyle(cell, style + separator + MissingHostStyleSuffix);
    }

    private static XElement? FindRoot(XDocument document)
    {
        return document.Descendants("root").FirstOrDefault();
    }

    /// <summary>
    /// The identifier of a cell, whether it is written on the mxCell itself or on
    /// the UserObject wrapping it, which is how a linked node is stored.
    /// </summary>
    private static string? Identifier(XElement element)
    {
        return element.Attribute(IdAttribute)?.Value;
    }

    /// <summary>The mxCell carrying the style, reached through a UserObject if present.</summary>
    private static XElement? CellElement(XElement element)
    {
        return element.Name.LocalName == UserObject
            ? element.Element(MxCell)
            : element.Name.LocalName == MxCell ? element : null;
    }

    private static string? StyleOf(XElement element)
    {
        return CellElement(element)?.Attribute(StyleAttribute)?.Value;
    }

    private static void SetStyle(XElement element, string style)
    {
        CellElement(element)?.SetAttributeValue(StyleAttribute, style);
    }

    private static XElement? FindGeometry(XElement element)
    {
        return CellElement(element)?.Element(Geometry);
    }

    private static void CopyAttribute(XElement source, XElement target, string name)
    {
        var value = source.Attribute(name)?.Value;
        if (value is not null)
        {
            target.SetAttributeValue(name, value);
        }
    }

    private static int CountHeimdallCells(XDocument document)
    {
        return document.Descendants()
            .Count(e => IsHeimdallCell(e.Attribute(IdAttribute)?.Value));
    }

    /// <summary>A one-line account of a merge, for the tool's status line.</summary>
    public static string Describe(DiagramMergeSummary summary, Func<string, string> localize)
    {
        ArgumentNullException.ThrowIfNull(summary);
        ArgumentNullException.ThrowIfNull(localize);

        return string.Format(
            CultureInfo.InvariantCulture,
            localize("ToolDiagramMergeSummary"),
            summary.Updated,
            summary.Added,
            summary.Missing,
            summary.Untouched);
    }
}
