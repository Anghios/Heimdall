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

using System.Xml.Linq;
using Heimdall.Core.Discovery;

namespace Heimdall.Core.Tests;

/// <summary>
/// Covers merging a fresh scan into a diagram the user has already arranged.
/// </summary>
/// <remarks>
/// Re-exporting a scan used to replace the file, discarding every position and
/// every shape the user had added. The point of the merge is that the layout is
/// the user's decision and the data is the scan's, and neither overwrites the
/// other.
/// </remarks>
public sealed class DiagramScanMergeTests
{
    private const string HostId = DrawIoExporter.HostCellPrefix + "10-0-0-5";

    private static string Diagram(params string[] cells)
    {
        return "<mxfile><diagram><mxGraphModel><root>"
            + "<mxCell id=\"0\"/><mxCell id=\"1\" parent=\"0\"/>"
            + string.Join(string.Empty, cells)
            + "</root></mxGraphModel></diagram></mxfile>";
    }

    private static string HostCell(string id, string value, string style, int x, int y)
    {
        return $"<mxCell id=\"{id}\" value=\"{value}\" style=\"{style}\" vertex=\"1\" parent=\"1\">"
            + $"<mxGeometry x=\"{x}\" y=\"{y}\" width=\"160\" height=\"70\" as=\"geometry\"/></mxCell>";
    }

    private static XElement? Cell(string xml, string id)
    {
        return XDocument.Parse(xml).Descendants()
            .FirstOrDefault(e => e.Attribute("id")?.Value == id);
    }

    private static (double X, double Y) GeometryOf(string xml, string id)
    {
        var geometry = Cell(xml, id)!.Descendants("mxGeometry").First();
        return (
            double.Parse(geometry.Attribute("x")!.Value, System.Globalization.CultureInfo.InvariantCulture),
            double.Parse(geometry.Attribute("y")!.Value, System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// The whole point: the user moved a node, the scan says something new about
    /// it, and both survive.
    /// </summary>
    [Fact]
    public void Merge_KeepsTheUsersLayoutAndTakesTheScansData()
    {
        var existing = Diagram(HostCell(HostId, "old label", "rounded=1;fillColor=#111111;", 900, 700));
        var incoming = Diagram(HostCell(HostId, "new label", "rounded=1;fillColor=#222222;", 20, 40));

        var merged = DiagramScanMerge.Merge(existing, incoming, out var summary);

        Assert.Equal((900, 700), GeometryOf(merged, HostId));
        Assert.Equal("new label", Cell(merged, HostId)!.Attribute("value")!.Value);
        Assert.Contains("#222222", Cell(merged, HostId)!.Attribute("style")!.Value, StringComparison.Ordinal);
        Assert.Equal(1, summary.Updated);
        Assert.Equal(0, summary.Added);
    }

    [Fact]
    public void Merge_AddsAHostTheScanFoundForTheFirstTime()
    {
        var newId = DrawIoExporter.HostCellPrefix + "10-0-0-9";
        var existing = Diagram(HostCell(HostId, "a", "rounded=1;", 0, 0));
        var incoming = Diagram(
            HostCell(HostId, "a", "rounded=1;", 0, 0),
            HostCell(newId, "b", "rounded=1;", 0, 90));

        var merged = DiagramScanMerge.Merge(existing, incoming, out var summary);

        Assert.NotNull(Cell(merged, newId));
        Assert.Equal(1, summary.Added);
    }

    /// <summary>
    /// A host that has gone is a finding. Erasing it would hide it, so it is kept
    /// and marked.
    /// </summary>
    [Fact]
    public void Merge_KeepsAndMarksAHostTheScanNoLongerReports()
    {
        var goneId = DrawIoExporter.HostCellPrefix + "10-0-0-99";
        var existing = Diagram(
            HostCell(HostId, "a", "rounded=1;", 0, 0),
            HostCell(goneId, "gone", "rounded=1;", 0, 90));
        var incoming = Diagram(HostCell(HostId, "a", "rounded=1;", 0, 0));

        var merged = DiagramScanMerge.Merge(existing, incoming, out var summary);

        var gone = Cell(merged, goneId);
        Assert.NotNull(gone);
        Assert.Contains(
            DiagramScanMerge.MissingHostStyleSuffix,
            gone!.Attribute("style")!.Value,
            StringComparison.Ordinal);
        Assert.Equal(1, summary.Missing);
    }

    /// <summary>
    /// Anything the user drew is theirs. A merge that touched it would make the
    /// feature useless, because annotating is the reason to keep a diagram.
    /// </summary>
    [Fact]
    public void Merge_NeverTouchesAShapeTheUserAdded()
    {
        var note = "<mxCell id=\"my-note\" value=\"check the firewall\" style=\"shape=note;\" vertex=\"1\" parent=\"1\">"
            + "<mxGeometry x=\"500\" y=\"500\" width=\"100\" height=\"40\" as=\"geometry\"/></mxCell>";
        var existing = Diagram(HostCell(HostId, "a", "rounded=1;", 0, 0), note);
        var incoming = Diagram(HostCell(HostId, "a", "rounded=1;", 0, 0));

        var merged = DiagramScanMerge.Merge(existing, incoming, out var summary);

        var kept = Cell(merged, "my-note");
        Assert.NotNull(kept);
        Assert.Equal("check the firewall", kept!.Attribute("value")!.Value);
        Assert.Equal("shape=note;", kept.Attribute("style")!.Value);
        Assert.DoesNotContain(
            DiagramScanMerge.MissingHostStyleSuffix,
            kept.Attribute("style")!.Value,
            StringComparison.Ordinal);
        Assert.Equal(1, summary.Untouched);
    }

    [Fact]
    public void Merge_KeepsTheLinkTheScanPutOnALinkedNode()
    {
        var linked = $"<UserObject id=\"{HostId}\" label=\"host\" link=\"heimdall://session/SSH/10.0.0.5:22\">"
            + "<mxCell style=\"rounded=1;\" vertex=\"1\" parent=\"1\">"
            + "<mxGeometry x=\"10\" y=\"10\" width=\"160\" height=\"70\" as=\"geometry\"/></mxCell></UserObject>";
        var existing = Diagram(HostCell(HostId, "host", "rounded=1;", 800, 600));

        var merged = DiagramScanMerge.Merge(existing, Diagram(linked), out _);

        Assert.Equal(
            "heimdall://session/SSH/10.0.0.5:22",
            Cell(merged, HostId)!.Attribute("link")!.Value);
        Assert.Equal((800, 600), GeometryOf(merged, HostId));
    }

    [Fact]
    public void Merge_MarksAMissingHostOnlyOnce()
    {
        var goneId = DrawIoExporter.HostCellPrefix + "10-0-0-99";
        var existing = Diagram(HostCell(goneId, "gone", "rounded=1;", 0, 0));
        var incoming = Diagram();

        var once = DiagramScanMerge.Merge(existing, incoming, out _);
        var twice = DiagramScanMerge.Merge(once, incoming, out _);

        var style = Cell(twice, goneId)!.Attribute("style")!.Value;
        var occurrences = style.Split(DiagramScanMerge.MissingHostStyleSuffix).Length - 1;
        Assert.Equal(1, occurrences);
    }

    [Theory]
    [InlineData(DrawIoExporter.HostCellPrefix + "1", true)]
    [InlineData(DrawIoExporter.LaneCellPrefix + "x", true)]
    [InlineData(DrawIoExporter.SegmentCellPrefix + "x", true)]
    [InlineData(DrawIoExporter.EdgeCellPrefix + "x", true)]
    [InlineData("my-note", false)]
    [InlineData("2", false)]
    [InlineData(null, false)]
    public void IsHeimdallCell_TellsTheScansCellsFromTheUsers(string? id, bool expected)
    {
        Assert.Equal(expected, DiagramScanMerge.IsHeimdallCell(id));
    }

    [Fact]
    public void Merge_OnSomethingThatIsNotADiagram_KeepsTheScan()
    {
        var incoming = Diagram(HostCell(HostId, "a", "rounded=1;", 0, 0));

        var merged = DiagramScanMerge.Merge("<other/>", incoming, out var summary);

        Assert.Equal(incoming, merged);
        Assert.True(summary.Added > 0);
    }
}
