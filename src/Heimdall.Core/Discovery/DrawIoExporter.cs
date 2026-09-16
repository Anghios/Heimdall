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

namespace Heimdall.Core.Discovery;

/// <summary>
/// Generates Draw.io (diagrams.net) XML files from network scan snapshots
/// for visual network topology diagrams.
/// </summary>
/// <remarks>
/// Hosts are grouped into one swimlane per role. The role name is the
/// classifier's identity string and drives the palette; every other piece
/// of user-facing text goes through the localizer so the two synthetic
/// groups and the node labels follow the application language.
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

    private const int LaneStartX = 40;
    private const int LaneStartY = 40;
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

        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine("<mxfile host=\"Heimdall\">");
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"  <diagram name=\"{EscapeXml(snapshot.Profile.Subnet)} - {snapshot.Timestamp:yyyy-MM-dd HH:mm}\">"));
        sb.AppendLine("    <mxGraphModel dx=\"1200\" dy=\"800\" grid=\"1\" gridSize=\"10\">");
        sb.AppendLine("      <root>");
        sb.AppendLine("        <mxCell id=\"0\"/>");
        sb.AppendLine("        <mxCell id=\"1\" parent=\"0\"/>");

        // Hosts with an open port are grouped by classified role; ping-only hosts
        // sit in their own lane at the end.
        var groups = snapshot.Hosts
            .GroupBy(HostGroup.For)
            .OrderBy(g => g.Key.IsPingOnly ? 1 : 0)
            .ThenBy(g => g.Key.Identity, StringComparer.Ordinal)
            .ToList();

        var cellId = 2;
        var laneX = LaneStartX;

        foreach (var group in groups)
        {
            var laneId = cellId++;
            var palette = PaletteFor(group.Key);
            var laneHeight = Math.Max(LaneMinHeight, group.Count() * NodeSlotHeight + LaneHeaderHeight);

            sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"        <mxCell id=\"{laneId}\" value=\"{EscapeXml(group.Key.Label(localize))}\" " +
                $"style=\"swimlane;startSize=30;fillColor={palette.LaneFill};fontColor=#ffffff;rounded=1;\" " +
                $"vertex=\"1\" parent=\"1\">"));
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"          <mxGeometry x=\"{laneX}\" y=\"{LaneStartY}\" width=\"{LaneWidth}\" height=\"{laneHeight}\" as=\"geometry\"/>"));
            sb.AppendLine("        </mxCell>");

            var nodeY = NodeFirstY;
            foreach (var host in group)
            {
                var nodeId = cellId++;
                var label = BuildNodeLabel(host, localize);

                sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
                    $"        <mxCell id=\"{nodeId}\" value=\"{EscapeXml(label)}\" " +
                    $"style=\"{palette.NodeStyle}\" vertex=\"1\" parent=\"{laneId}\">"));
                sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
                    $"          <mxGeometry x=\"{NodeX}\" y=\"{nodeY}\" width=\"{NodeWidth}\" height=\"{NodeHeight}\" as=\"geometry\"/>"));
                sb.AppendLine("        </mxCell>");

                nodeY += NodeSpacing;
            }

            laneX += LaneSpacing;
        }

        sb.AppendLine("      </root>");
        sb.AppendLine("    </mxGraphModel>");
        sb.AppendLine("  </diagram>");
        sb.AppendLine("</mxfile>");

        return sb.ToString();
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
    private sealed record RolePalette(
        string LaneFill,
        string NodeFill,
        string NodeStroke,
        string NodeFontColor = "#ffffff",
        string? Shape = null,
        bool Dashed = false)
    {
        public string NodeStyle => Shape is null
            ? $"rounded=1;whiteSpace=wrap;fillColor={NodeFill};fontColor={NodeFontColor};strokeColor={NodeStroke};fontSize=10;align=left;spacingLeft=8;{(Dashed ? "dashed=1;" : string.Empty)}"
            : $"shape={Shape};whiteSpace=wrap;fillColor={NodeFill};fontColor={NodeFontColor};strokeColor={NodeStroke};fontSize=10;size=8;";
    }

    private static readonly RolePalette DefaultPalette = new("#44475A", "#44475A", "#6272A4");

    private static readonly RolePalette PingOnlyPalette =
        new("#9CA3AF", "#E5E7EB", "#9CA3AF", NodeFontColor: "#6B7280", Dashed: true);

    private static readonly (Func<string, bool> Matches, RolePalette Palette)[] RolePalettes =
    [
        (r => r == "Active Directory", new RolePalette("#1E40AF", "#1E40AF", "#1E3A8A")),
        (r => r.StartsWith("Web Server", StringComparison.Ordinal), new RolePalette("#16A34A", "#16A34A", "#15803D")),
        (r => r.StartsWith("Database", StringComparison.Ordinal), new RolePalette("#D97706", "#D97706", "#B45309", Shape: "cylinder3")),
        (r => r == "Mail Server", new RolePalette("#7C3AED", "#7C3AED", "#6D28D9")),
        (r => r == "Windows RDP", new RolePalette("#2563EB", "#2563EB", "#1D4ED8")),
        (r => r == "SSH Server", new RolePalette("#059669", "#059669", "#047857")),
        (r => r.Contains("Camera", StringComparison.Ordinal), new RolePalette("#DC2626", "#DC2626", "#B91C1C")),
        (r => r.Contains("NAS", StringComparison.Ordinal), new RolePalette("#EA580C", "#EA580C", "#C2410C")),
        (r => r.Contains("Router", StringComparison.Ordinal) || r.Contains("Switch", StringComparison.Ordinal), new RolePalette("#0891B2", "#0891B2", "#0E7490")),
        (r => r.Contains("Firewall", StringComparison.Ordinal), new RolePalette("#B91C1C", "#B91C1C", "#991B1B")),
        (r => r.Contains("Printer", StringComparison.Ordinal), new RolePalette("#6B7280", "#6B7280", "#4B5563")),
        (r => r.Contains("Hypervisor", StringComparison.Ordinal) || r.Contains("VMware", StringComparison.Ordinal) || r.Contains("Proxmox", StringComparison.Ordinal), new RolePalette("#7C3AED", "#7C3AED", "#6D28D9")),
        (r => r == "Network Equipment (SNMP)", new RolePalette("#6B7280", "#6B7280", "#4B5563")),
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
