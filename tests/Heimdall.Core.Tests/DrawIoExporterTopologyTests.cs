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
using Heimdall.Core.Models;

namespace Heimdall.Core.Tests;

/// <summary>
/// Covers what makes the exported diagram a topology rather than a list: the
/// segments, the links between hosts and their segment, the identifiers that
/// survive a re-export, and the session each node offers.
/// </summary>
/// <remarks>
/// The exporter used to emit vertices and nothing else. A drawing of a network
/// with no edge says what exists and never says what talks to what, which is the
/// one thing a network diagram is for.
/// </remarks>
public sealed class DrawIoExporterTopologyTests
{
    private static string Localize(string key) => key switch
    {
        DrawIoExporter.GroupPingOnlyKey => "Ping only",
        DrawIoExporter.GroupUnclassifiedKey => "Unclassified",
        DrawIoExporter.SegmentSubnetKey => "Subnet {0}",
        DrawIoExporter.SegmentGatewayKey => "Gateway: {0}",
        _ => key + "{0}"
    };

    private static NetworkScanSnapshot Snapshot(
        List<HostScanResult> hosts,
        List<VlanInfo>? vlans = null,
        string? gateway = null)
    {
        return new NetworkScanSnapshot(
            "test", DateTime.UtcNow,
            new ScanProfile("192.168.1.0/24", ScanDepth.Quick, null, 50, 2000, false, false),
            gateway, TimeSpan.Zero, hosts, vlans);
    }

    private static HostScanResult Host(string ip, string? role, params int[] ports)
    {
        var services = ports.Select(p => new ServiceResult(p, true, null, null, null, 0)).ToList();
        var match = role is null ? null : new RoleMatch(role, 80, []);
        return new HostScanResult(ip, null, true, 1, services, match, match is null ? [] : [match]);
    }

    private static List<XElement> Cells(string xml) =>
        XDocument.Parse(xml).Descendants().Where(e => e.Attribute("id") is not null).ToList();

    private static List<XElement> Edges(string xml) =>
        XDocument.Parse(xml).Descendants("mxCell").Where(e => e.Attribute("edge")?.Value == "1").ToList();

    // -- Links between hosts and their segment -------------------------

    [Fact]
    public void Generate_JoinsEveryHostToASegment()
    {
        var xml = DrawIoExporter.Generate(
            Snapshot([Host("192.168.1.10", "SSH Server", 22), Host("192.168.1.20", "Web Server", 80)]),
            Localize);

        var edges = Edges(xml);

        Assert.Equal(2, edges.Count);
        Assert.All(edges, edge =>
        {
            Assert.StartsWith(DrawIoExporter.HostCellPrefix, edge.Attribute("source")!.Value, StringComparison.Ordinal);
            Assert.StartsWith(DrawIoExporter.SegmentCellPrefix, edge.Attribute("target")!.Value, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Generate_WithoutVlans_PutsEveryHostInOneSubnetSegment()
    {
        var xml = DrawIoExporter.Generate(
            Snapshot([Host("192.168.1.10", "SSH Server", 22)], gateway: "192.168.1.1"),
            Localize);

        var segments = Cells(xml)
            .Where(c => c.Attribute("id")!.Value.StartsWith(DrawIoExporter.SegmentCellPrefix, StringComparison.Ordinal))
            .ToList();

        Assert.Single(segments);
        Assert.Contains("Subnet 192.168.1.0/24", segments[0].Attribute("value")!.Value, StringComparison.Ordinal);
        Assert.Contains("Gateway: 192.168.1.1", segments[0].Attribute("value")!.Value, StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_WithVlans_DrawsOneSegmentEachAndJoinsThem()
    {
        var vlans = new List<VlanInfo>
        {
            new(10, "VLAN 10", "192.168.1.0/24", "192.168.1.1", ["192.168.1.10"]),
            new(20, "VLAN 20", "192.168.2.0/24", "192.168.2.1", ["192.168.2.10"]),
        };

        var xml = DrawIoExporter.Generate(
            Snapshot([Host("192.168.1.10", "SSH Server", 22), Host("192.168.2.10", "Web Server", 80)], vlans),
            Localize);

        var segments = Cells(xml)
            .Where(c => c.Attribute("id")!.Value.StartsWith(DrawIoExporter.SegmentCellPrefix, StringComparison.Ordinal))
            .ToList();

        Assert.Equal(2, segments.Count);

        // Two host edges plus the one joining the second segment to the first.
        Assert.Equal(3, Edges(xml).Count);
    }

    /// <summary>
    /// A host that is the gateway is the centre of its segment, so the link runs
    /// outwards from the segment rather than into it.
    /// </summary>
    [Fact]
    public void Generate_DrawsTheGatewayHostAsTheSegmentsCentre()
    {
        var vlans = new List<VlanInfo>
        {
            new(10, "VLAN 10", "192.168.1.0/24", "192.168.1.1", ["192.168.1.1", "192.168.1.10"]),
        };

        var xml = DrawIoExporter.Generate(
            Snapshot([Host("192.168.1.1", "Router/Gateway", 22), Host("192.168.1.10", "SSH Server", 22)], vlans),
            Localize);

        var gatewayEdge = Edges(xml).Single(e =>
            e.Attribute("target")!.Value == DrawIoExporter.HostCellPrefix + "192-168-1-1");

        Assert.StartsWith(DrawIoExporter.SegmentCellPrefix, gatewayEdge.Attribute("source")!.Value, StringComparison.Ordinal);
    }

    // -- Identifiers that survive a re-export --------------------------

    /// <summary>
    /// Identifiers used to be counters, so the same host came back under a
    /// different name on every export and nothing could be merged.
    /// </summary>
    [Fact]
    public void Generate_GivesAHostTheSameIdentifierEveryTime()
    {
        var first = DrawIoExporter.Generate(
            Snapshot([Host("10.0.0.5", "SSH Server", 22)]), Localize);

        // The same host, now behind another one in the ordering.
        var second = DrawIoExporter.Generate(
            Snapshot([Host("10.0.0.1", "Active Directory", 389), Host("10.0.0.5", "SSH Server", 22)]),
            Localize);

        const string expected = DrawIoExporter.HostCellPrefix + "10-0-0-5";
        Assert.Contains(Cells(first), c => c.Attribute("id")!.Value == expected);
        Assert.Contains(Cells(second), c => c.Attribute("id")!.Value == expected);
    }

    [Fact]
    public void Generate_GivesEveryCellAnIdentifierOfItsOwn()
    {
        var xml = DrawIoExporter.Generate(
            Snapshot([Host("10.0.0.1", "SSH Server", 22), Host("10.0.0.2", "SSH Server", 22)]),
            Localize);

        var ids = Cells(xml).Select(c => c.Attribute("id")!.Value).ToList();

        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
    }

    // -- The session a node offers -------------------------------------

    [Fact]
    public void Generate_LinksAHostToTheSessionItsPortsCallFor()
    {
        var xml = DrawIoExporter.Generate(
            Snapshot([Host("10.0.0.5", "Windows RDP", 3389, 22)]), Localize);

        var node = Cells(xml).Single(c => c.Attribute("id")!.Value == DrawIoExporter.HostCellPrefix + "10-0-0-5");

        Assert.Equal("UserObject", node.Name.LocalName);
        Assert.True(SessionLaunchRequest.TryParse(node.Attribute("link")!.Value, out var request));
        Assert.Equal("RDP", request.Protocol);
        Assert.Equal(3389, request.Port);
        Assert.Equal("10.0.0.5", request.Host);
    }

    [Fact]
    public void Generate_LeavesAHostWithNoSessionUnlinked()
    {
        var xml = DrawIoExporter.Generate(
            Snapshot([Host("10.0.0.7", "Network Printer", 9100)]), Localize);

        var node = Cells(xml).Single(c => c.Attribute("id")!.Value == DrawIoExporter.HostCellPrefix + "10-0-0-7");

        Assert.Equal("mxCell", node.Name.LocalName);
        Assert.Null(node.Attribute("link"));
    }

    [Fact]
    public void Generate_LinkedNodeKeepsItsGeometryAndParent()
    {
        var xml = DrawIoExporter.Generate(
            Snapshot([Host("10.0.0.5", "SSH Server", 22)]), Localize);

        var node = Cells(xml).Single(c => c.Attribute("id")!.Value == DrawIoExporter.HostCellPrefix + "10-0-0-5");
        var cell = node.Element("mxCell")!;

        Assert.StartsWith(DrawIoExporter.LaneCellPrefix, cell.Attribute("parent")!.Value, StringComparison.Ordinal);
        Assert.NotNull(cell.Element("mxGeometry"));
    }

    // -- Shapes ---------------------------------------------------------

    /// <summary>
    /// Shapes must be mxGraph's own, never a stencil: the vendored bundle is
    /// pruned, and a missing stencil renders as an empty box.
    /// </summary>
    [Fact]
    public void Generate_UsesOnlyShapesBuiltIntoTheGraphLibrary()
    {
        string[] builtIn =
        [
            "cylinder3", "hexagon", "cloud", "cube", "note", "step",
            "internalStorage", "trapezoid", "message"
        ];

        string[] roles =
        [
            "Active Directory", "Web Server", "Database (MySQL)", "Mail Server",
            "Windows RDP", "SSH Server", "IP Camera (RTSP)", "NAS (Synology)",
            "Router/Gateway", "Firewall", "Network Printer", "Hypervisor (VMware)",
        ];

        var unknown = new List<string>();

        foreach (var role in roles)
        {
            var xml = DrawIoExporter.Generate(Snapshot([Host("10.0.0.1", role, 443)]), Localize);

            foreach (var cell in XDocument.Parse(xml).Descendants("mxCell"))
            {
                var style = cell.Attribute("style")?.Value;
                if (style is null) continue;

                foreach (var part in style.Split(';', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (part.StartsWith("shape=", StringComparison.Ordinal)
                        && !builtIn.Contains(part["shape=".Length..], StringComparer.Ordinal))
                    {
                        unknown.Add($"{role}: {part}");
                    }
                }
            }
        }

        Assert.True(unknown.Count == 0,
            "These styles name a shape that is not known to be built in:\n" + string.Join("\n", unknown));
    }
}
