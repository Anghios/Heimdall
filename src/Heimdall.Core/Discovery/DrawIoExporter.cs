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
using System.Text;
using Heimdall.Core.Models;

namespace Heimdall.Core.Discovery;

/// <summary>
/// Generates Draw.io (diagrams.net) XML files from network scan snapshots
/// for visual network topology diagrams.
/// </summary>
/// <remarks>
/// <para>Hosts are grouped into one swimlane per role, and each host is joined to
/// the segment it belongs to, so the drawing says what talks to what instead of
/// only listing what exists. Segments come from the detected VLANs, which already
/// carry their own member list and gateway; a scan with no VLAN detected gets one
/// segment named after the scanned subnet.</para>
/// <para>Each host node carries a link back into Heimdall, built from the ports
/// found open on it, so activating the node opens that session. See
/// <see cref="SessionLaunchRequest"/> for the format and
/// <see cref="SessionProtocolChoice"/> for which session a host gets.</para>
/// <para>Cell identifiers are derived from the host address rather than from a
/// counter, so the same host keeps the same identifier across exports. That is
/// what lets a later scan be merged into a diagram the user has already arranged
/// by hand.</para>
/// </remarks>
public static class DrawIoExporter
{
    /// <summary>Locale key of the swimlane holding hosts without any open port.</summary>
    public const string GroupPingOnlyKey = "DrawIoExportGroupPingOnly";

    /// <summary>Locale key of the swimlane holding hosts the classifier could not name.</summary>
    public const string GroupUnclassifiedKey = "DrawIoExportGroupUnclassified";

    /// <summary>Locale key of the "Ports: {0}" node line.</summary>
    public const string LabelPortsKey = "DrawIoExportLabelPorts";

    /// <summary>Locale key of the "MAC: {0}" node line.</summary>
    public const string LabelMacKey = "DrawIoExportLabelMac";

    /// <summary>Locale key of the "OS: {0}" node line.</summary>
    public const string LabelOsKey = "DrawIoExportLabelOs";

    /// <summary>Locale key of the "NetBIOS: {0}" node line.</summary>
    public const string LabelNetBiosKey = "DrawIoExportLabelNetBios";

    /// <summary>Locale key of the "SNMP: {0}" node line.</summary>
    public const string LabelSnmpKey = "DrawIoExportLabelSnmp";

    /// <summary>Locale key of the expired-certificate node line.</summary>
    public const string LabelCertExpiredKey = "DrawIoExportLabelCertExpired";

    /// <summary>Locale key of the "Cert: {0}" node line.</summary>
    public const string LabelCertKey = "DrawIoExportLabelCert";

    /// <summary>Locale key of the segment node holding a whole scanned subnet.</summary>
    public const string SegmentSubnetKey = "DrawIoExportSegmentSubnet";

    /// <summary>Locale key of the "Gateway: {0}" line on a segment node.</summary>
    public const string SegmentGatewayKey = "DrawIoExportSegmentGateway";

    /// <summary>Prefix of a host cell's identifier, followed by the address.</summary>
    public const string HostCellPrefix = "heimdall-host-";

    /// <summary>Prefix of a swimlane cell's identifier, followed by the role.</summary>
    public const string LaneCellPrefix = "heimdall-lane-";

    /// <summary>Prefix of a segment cell's identifier, followed by the segment name.</summary>
    public const string SegmentCellPrefix = "heimdall-segment-";

    /// <summary>Prefix of an edge cell's identifier.</summary>
    public const string EdgeCellPrefix = "heimdall-link-";

    private const int SegmentRowY = 40;
    private const int SegmentWidth = 200;
    private const int SegmentHeight = 60;
    private const int SegmentSpacing = 240;

    private const int LaneStartX = 40;
    private const int LaneStartY = 180;
    private const int LaneWidth = 200;
    private const int LaneSpacing = 240;
    private const int LaneMinHeight = 120;
    private const int LaneHeaderHeight = 40;

    private const int NodeX = 20;
    private const int NodeFirstY = 40;
    private const int NodeWidth = 160;
    private const int NodeHeight = 70;
    private const int NodeSpacing = 80;
    private const int NodeSlotHeight = 90;

    private const string SegmentStyle =
        "shape=hexagon;whiteSpace=wrap;html=1;fillColor=#0891B2;fontColor=#ffffff;"
        + "strokeColor=#0E7490;fontSize=11;fontStyle=1;";

    private const string EdgeStyle =
        "edgeStyle=orthogonalEdgeStyle;rounded=1;html=1;endArrow=none;strokeColor=#6272A4;";

    private const string GatewayEdgeStyle =
        "edgeStyle=orthogonalEdgeStyle;rounded=1;html=1;endArrow=none;strokeColor=#0891B2;strokeWidth=2;";

    /// <summary>
    /// Generates a Draw.io XML string from a <see cref="NetworkScanSnapshot"/>.
    /// </summary>
    /// <param name="snapshot">The scan to draw.</param>
    /// <param name="localize">
    /// Resolves a locale key to its user-facing text; the "{0}" placeholders of
    /// the label keys are filled by this method.
    /// </param>
    public static string Generate(NetworkScanSnapshot snapshot, Func<string, string> localize)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(localize);

        var segments = BuildSegments(snapshot, localize);

        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine("<mxfile host=\"Heimdall\">");
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"  <diagram name=\"{EscapeXml(snapshot.Profile.Subnet)} - {snapshot.Timestamp:yyyy-MM-dd HH:mm}\">"));
        sb.AppendLine("    <mxGraphModel dx=\"1200\" dy=\"800\" grid=\"1\" gridSize=\"10\">");
        sb.AppendLine("      <root>");
        sb.AppendLine("        <mxCell id=\"0\"/>");
        sb.AppendLine("        <mxCell id=\"1\" parent=\"0\"/>");

        AppendSegments(sb, segments);

        // Hosts with an open port are grouped by classified role; ping-only hosts
        // sit in their own lane at the end.
        var groups = snapshot.Hosts
            .GroupBy(HostGroup.For)
            .OrderBy(g => g.Key.IsPingOnly ? 1 : 0)
            .ThenBy(g => g.Key.Identity, StringComparer.Ordinal)
            .ToList();

        var laneX = LaneStartX;
        var edges = new List<(string Source, string Target)>();

        foreach (var group in groups)
        {
            var laneId = LaneCellPrefix + Slug(group.Key.Identity);
            var palette = PaletteFor(group.Key);
            var laneHeight = Math.Max(LaneMinHeight, group.Count() * NodeSlotHeight + LaneHeaderHeight);

            sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"        <mxCell id=\"{EscapeXml(laneId)}\" value=\"{EscapeXml(group.Key.Label(localize))}\" " +
                $"style=\"swimlane;startSize=30;fillColor={palette.LaneFill};fontColor=#ffffff;rounded=1;\" " +
                $"vertex=\"1\" parent=\"1\">"));
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"          <mxGeometry x=\"{laneX}\" y=\"{LaneStartY}\" width=\"{LaneWidth}\" height=\"{laneHeight}\" as=\"geometry\"/>"));
            sb.AppendLine("        </mxCell>");

            var nodeY = NodeFirstY;
            foreach (var host in group)
            {
                var nodeId = HostCellPrefix + Slug(host.IpAddress);
                AppendHostNode(sb, host, nodeId, laneId, palette, nodeY, localize);

                // A host that is itself a segment's gateway is joined from the
                // segment rather than to it, so the gateway reads as the centre.
                var segment = SegmentFor(segments, host.IpAddress);
                if (segment is not null)
                {
                    edges.Add(segment.IsGateway(host.IpAddress)
                        ? (segment.CellId, nodeId)
                        : (nodeId, segment.CellId));
                }

                nodeY += NodeSpacing;
            }

            laneX += LaneSpacing;
        }

        AppendEdges(sb, segments, edges);

        sb.AppendLine("      </root>");
        sb.AppendLine("    </mxGraphModel>");
        sb.AppendLine("  </diagram>");
        sb.AppendLine("</mxfile>");

        return sb.ToString();
    }

    private static void AppendHostNode(
        StringBuilder sb,
        HostScanResult host,
        string nodeId,
        string laneId,
        RolePalette palette,
        int nodeY,
        Func<string, string> localize)
    {
        var label = BuildNodeLabel(host, localize);
        var openPorts = host.Services.Where(s => s.IsOpen).Select(s => s.Port).ToList();
        var session = SessionProtocolChoice.ForHost(host.IpAddress, openPorts);

        // A node that can be connected to is written as a UserObject carrying the
        // link; draw.io reports its activation through the embed protocol.
        if (session is not null)
        {
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"        <UserObject id=\"{EscapeXml(nodeId)}\" label=\"{EscapeXml(label)}\" " +
                $"link=\"{EscapeXml(session.ToUri().AbsoluteUri)}\">"));
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"          <mxCell style=\"{palette.NodeStyle}\" vertex=\"1\" parent=\"{EscapeXml(laneId)}\">"));
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"            <mxGeometry x=\"{NodeX}\" y=\"{nodeY}\" width=\"{NodeWidth}\" height=\"{NodeHeight}\" as=\"geometry\"/>"));
            sb.AppendLine("          </mxCell>");
            sb.AppendLine("        </UserObject>");
            return;
        }

        sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"        <mxCell id=\"{EscapeXml(nodeId)}\" value=\"{EscapeXml(label)}\" " +
            $"style=\"{palette.NodeStyle}\" vertex=\"1\" parent=\"{EscapeXml(laneId)}\">"));
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"          <mxGeometry x=\"{NodeX}\" y=\"{nodeY}\" width=\"{NodeWidth}\" height=\"{NodeHeight}\" as=\"geometry\"/>"));
        sb.AppendLine("        </mxCell>");
    }

    private static void AppendSegments(StringBuilder sb, IReadOnlyList<NetworkSegment> segments)
    {
        var x = LaneStartX;

        foreach (var segment in segments)
        {
            sb.Append("        <mxCell id=\"").Append(EscapeXml(segment.CellId))
                .Append("\" value=\"").Append(EscapeXml(segment.Label))
                .Append("\" style=\"").Append(SegmentStyle)
                .AppendLine("\" vertex=\"1\" parent=\"1\">");
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"          <mxGeometry x=\"{x}\" y=\"{SegmentRowY}\" width=\"{SegmentWidth}\" height=\"{SegmentHeight}\" as=\"geometry\"/>"));
            sb.AppendLine("        </mxCell>");

            x += SegmentSpacing;
        }
    }

    private static void AppendEdges(
        StringBuilder sb,
        IReadOnlyList<NetworkSegment> segments,
        IReadOnlyList<(string Source, string Target)> hostEdges)
    {
        foreach (var (source, target) in hostEdges)
        {
            AppendEdge(sb, source, target, EdgeStyle);
        }

        // Segments are joined to one another so a multi-VLAN scan reads as a
        // network rather than as several unrelated drawings. The first segment
        // stands in for the route between them, which a scan cannot observe.
        for (var index = 1; index < segments.Count; index++)
        {
            AppendEdge(sb, segments[index].CellId, segments[0].CellId, GatewayEdgeStyle);
        }
    }

    private static void AppendEdge(StringBuilder sb, string source, string target, string style)
    {
        var id = EdgeCellPrefix + Slug(source) + "--" + Slug(target);

        sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"        <mxCell id=\"{EscapeXml(id)}\" style=\"{style}\" edge=\"1\" parent=\"1\" " +
            $"source=\"{EscapeXml(source)}\" target=\"{EscapeXml(target)}\">"));
        sb.AppendLine("          <mxGeometry relative=\"1\" as=\"geometry\"/>");
        sb.AppendLine("        </mxCell>");
    }

    /// <summary>
    /// Works out which segments the drawing has. Detected VLANs already carry
    /// their members and their gateway; without any, the whole scan is one
    /// segment named after the subnet that was scanned.
    /// </summary>
    private static List<NetworkSegment> BuildSegments(
        NetworkScanSnapshot snapshot,
        Func<string, string> localize)
    {
        if (snapshot.DetectedVlans is { Count: > 0 } vlans)
        {
            return vlans
                .Select(vlan => new NetworkSegment(
                    SegmentCellPrefix + Slug(vlan.Subnet),
                    BuildSegmentLabel(vlan.Name, vlan.Gateway, localize),
                    vlan.Gateway,
                    new HashSet<string>(vlan.MemberIps ?? [], StringComparer.OrdinalIgnoreCase)))
                .ToList();
        }

        var everyHost = new HashSet<string>(
            snapshot.Hosts.Select(h => h.IpAddress),
            StringComparer.OrdinalIgnoreCase);

        return
        [
            new NetworkSegment(
                SegmentCellPrefix + Slug(snapshot.Profile.Subnet),
                BuildSegmentLabel(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        localize(SegmentSubnetKey),
                        snapshot.Profile.Subnet),
                    snapshot.GatewayName,
                    localize),
                snapshot.GatewayName,
                everyHost)
        ];
    }

    private static string BuildSegmentLabel(string name, string? gateway, Func<string, string> localize)
    {
        return string.IsNullOrWhiteSpace(gateway)
            ? name
            : name + "\n" + string.Format(CultureInfo.InvariantCulture, localize(SegmentGatewayKey), gateway);
    }

    private static NetworkSegment? SegmentFor(IReadOnlyList<NetworkSegment> segments, string ipAddress)
    {
        foreach (var segment in segments)
        {
            if (segment.MemberIps.Contains(ipAddress))
            {
                return segment;
            }
        }

        // A host outside every declared member list still belongs to the drawing;
        // the first segment stands in, rather than leaving the node unconnected.
        return segments.Count > 0 ? segments[0] : null;
    }

    private static string BuildNodeLabel(HostScanResult host, Func<string, string> localize)
    {
        var sb = new StringBuilder();
        sb.Append(host.IpAddress);
        if (!string.IsNullOrEmpty(host.Hostname))
            sb.Append('\n').Append(host.Hostname);

        // Show manufacturer from MAC OUI if available
        if (!string.IsNullOrEmpty(host.Manufacturer))
            sb.Append("\n[").Append(host.Manufacturer).Append(']');

        var ports = string.Join(", ", host.Services.Where(s => s.IsOpen).Select(s => s.Port));
        if (!string.IsNullOrEmpty(ports))
            AppendLine(sb, localize, LabelPortsKey, ports);
        else if (!string.IsNullOrEmpty(host.MacAddress))
            AppendLine(sb, localize, LabelMacKey, host.MacAddress);

        if (host.OsFingerprint is not null)
            AppendLine(sb, localize, LabelOsKey, host.OsFingerprint.OsGuess);

        if (!string.IsNullOrEmpty(host.NetBiosName))
            AppendLine(sb, localize, LabelNetBiosKey, host.NetBiosName);

        if (host.SnmpInfo?.SysName is not null)
            AppendLine(sb, localize, LabelSnmpKey, host.SnmpInfo.SysName);

        var tlsCert = host.Services.FirstOrDefault(s => s.Certificate is not null)?.Certificate;
        if (tlsCert is not null)
        {
            if (tlsCert.IsExpired)
                sb.Append('\n').Append(localize(LabelCertExpiredKey));
            else
                AppendLine(sb, localize, LabelCertKey, tlsCert.TlsVersion);
        }

        return sb.ToString();
    }

    private static void AppendLine(StringBuilder sb, Func<string, string> localize, string key, string? value)
    {
        sb.Append('\n').Append(string.Format(CultureInfo.InvariantCulture, localize(key), value));
    }

    /// <summary>
    /// Turns arbitrary text into something that can stand in a cell identifier,
    /// so the identifier stays readable and stable across exports.
    /// </summary>
    private static string Slug(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            sb.Append(char.IsAsciiLetterOrDigit(character) ? char.ToLowerInvariant(character) : '-');
        }

        return sb.ToString();
    }

    /// <summary>
    /// One broadcast domain of the drawing: a detected VLAN, or the whole scan.
    /// </summary>
    private sealed record NetworkSegment(
        string CellId,
        string Label,
        string? Gateway,
        IReadOnlySet<string> MemberIps)
    {
        public bool IsGateway(string ipAddress) =>
            !string.IsNullOrWhiteSpace(Gateway)
            && string.Equals(Gateway, ipAddress, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Identity of a swimlane: the classifier's role name, or one of the two
    /// synthetic groups. The identity is never shown; the label is.
    /// </summary>
    private sealed record HostGroup(string Identity, bool IsPingOnly, bool IsUnclassified)
    {
        public static HostGroup For(HostScanResult host)
        {
            if (!host.Services.Any(s => s.IsOpen))
                return new HostGroup(GroupPingOnlyKey, IsPingOnly: true, IsUnclassified: false);

            var role = host.PrimaryRole?.Role;
            return role is null
                ? new HostGroup(GroupUnclassifiedKey, IsPingOnly: false, IsUnclassified: true)
                : new HostGroup(role, IsPingOnly: false, IsUnclassified: false);
        }

        public string Label(Func<string, string> localize) =>
            IsPingOnly || IsUnclassified ? localize(Identity) : Identity;
    }

    /// <summary>
    /// One palette per role family. The lane and its nodes are derived from the
    /// same record so they can never disagree.
    /// </summary>
    /// <remarks>
    /// <paramref name="Shape"/> names a shape built into mxGraph itself, never a
    /// stencil: a stencil that is pruned from the vendored bundle would render as
    /// an empty box, and the export must survive that.
    /// </remarks>
    private sealed record RolePalette(
        string LaneFill,
        string NodeFill,
        string NodeStroke,
        string NodeFontColor = "#ffffff",
        string? Shape = null,
        bool Dashed = false)
    {
        public string NodeStyle => Shape is null
            ? $"rounded=1;whiteSpace=wrap;html=1;fillColor={NodeFill};fontColor={NodeFontColor};strokeColor={NodeStroke};fontSize=10;align=left;spacingLeft=8;{(Dashed ? "dashed=1;" : string.Empty)}"
            : $"shape={Shape};whiteSpace=wrap;html=1;fillColor={NodeFill};fontColor={NodeFontColor};strokeColor={NodeStroke};fontSize=10;";
    }

    private static readonly RolePalette DefaultPalette = new("#44475A", "#44475A", "#6272A4");

    private static readonly RolePalette PingOnlyPalette =
        new("#9CA3AF", "#E5E7EB", "#9CA3AF", NodeFontColor: "#6B7280", Dashed: true);

    private static readonly (Func<string, bool> Matches, RolePalette Palette)[] RolePalettes =
    [
        (r => r == "Active Directory", new RolePalette("#1E40AF", "#1E40AF", "#1E3A8A", Shape: "internalStorage")),
        (r => r.StartsWith("Web Server", StringComparison.Ordinal), new RolePalette("#16A34A", "#16A34A", "#15803D", Shape: "cloud")),
        (r => r.StartsWith("Database", StringComparison.Ordinal), new RolePalette("#D97706", "#D97706", "#B45309", Shape: "cylinder3")),
        (r => r == "Mail Server", new RolePalette("#7C3AED", "#7C3AED", "#6D28D9", Shape: "message")),
        (r => r == "Windows RDP", new RolePalette("#2563EB", "#2563EB", "#1D4ED8", Shape: "internalStorage")),
        (r => r == "SSH Server", new RolePalette("#059669", "#059669", "#047857", Shape: "internalStorage")),
        (r => r.Contains("Camera", StringComparison.Ordinal), new RolePalette("#DC2626", "#DC2626", "#B91C1C", Shape: "trapezoid")),
        (r => r.Contains("NAS", StringComparison.Ordinal), new RolePalette("#EA580C", "#EA580C", "#C2410C", Shape: "cylinder3")),
        (r => r.Contains("Router", StringComparison.Ordinal) || r.Contains("Switch", StringComparison.Ordinal), new RolePalette("#0891B2", "#0891B2", "#0E7490", Shape: "hexagon")),
        (r => r.Contains("Firewall", StringComparison.Ordinal), new RolePalette("#B91C1C", "#B91C1C", "#991B1B", Shape: "step")),
        (r => r.Contains("Printer", StringComparison.Ordinal), new RolePalette("#6B7280", "#6B7280", "#4B5563", Shape: "note")),
        (r => r.Contains("Hypervisor", StringComparison.Ordinal) || r.Contains("VMware", StringComparison.Ordinal) || r.Contains("Proxmox", StringComparison.Ordinal), new RolePalette("#7C3AED", "#7C3AED", "#6D28D9", Shape: "cube")),
        (r => r == "Network Equipment (SNMP)", new RolePalette("#6B7280", "#6B7280", "#4B5563", Shape: "hexagon")),
    ];

    private static RolePalette PaletteFor(HostGroup group)
    {
        if (group.IsPingOnly)
            return PingOnlyPalette;
        if (group.IsUnclassified)
            return DefaultPalette;

        foreach (var (matches, palette) in RolePalettes)
        {
            if (matches(group.Identity))
                return palette;
        }

        return DefaultPalette;
    }

    private static string EscapeXml(string s) => s
        .Replace("&", "&amp;")
        .Replace("<", "&lt;")
        .Replace(">", "&gt;")
        .Replace("\"", "&quot;")
        .Replace("\n", "&#xa;");
}
