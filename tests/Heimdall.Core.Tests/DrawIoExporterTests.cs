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
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Heimdall.Core.Discovery;

namespace Heimdall.Core.Tests;

public class DrawIoExporterTests
{
    /// <summary>
    /// Stands in for the application localizer: returns the English text for the
    /// keys the exporter uses, so assertions read like the shipped output.
    /// </summary>
    private static string Localize(string key) => key switch
    {
        DrawIoExporter.GroupPingOnlyKey => "Ping only (no open port)",
        DrawIoExporter.GroupUnclassifiedKey => "Unclassified",
        DrawIoExporter.LabelPortsKey => "Ports: {0}",
        DrawIoExporter.LabelMacKey => "MAC: {0}",
        DrawIoExporter.LabelOsKey => "OS: {0}",
        DrawIoExporter.LabelNetBiosKey => "NetBIOS: {0}",
        DrawIoExporter.LabelSnmpKey => "SNMP: {0}",
        DrawIoExporter.LabelCertExpiredKey => "Certificate EXPIRED",
        DrawIoExporter.LabelCertKey => "Cert: {0}",
        _ => key
    };

    /// <summary>Marks every localized fragment so a raw English string in the exporter is visible.</summary>
    private static string Marked(string key) => "[[" + key + "]]{0}";

    // -- XML validity --------------------------------------------------

    [Fact]
    public void Generate_ProducesValidXml()
    {
        var snapshot = CreateSnapshot(
            ("192.168.1.1", "gateway", "Router/Gateway", [80, 443]),
            ("192.168.1.10", "web-srv", "Web Server", [80, 443, 8080]));

        string xml = DrawIoExporter.Generate(snapshot, Localize);

        // Must parse without exceptions
        var doc = XDocument.Parse(xml);
        Assert.NotNull(doc.Root);
        Assert.Equal("mxfile", doc.Root!.Name.LocalName);
    }

    [Fact]
    public void Generate_ContainsDiagramElement()
    {
        var snapshot = CreateSnapshot(("192.168.1.1", null, null, [22]));

        string xml = DrawIoExporter.Generate(snapshot, Localize);
        var doc = XDocument.Parse(xml);

        var diagram = doc.Root!.Element("diagram");
        Assert.NotNull(diagram);
    }

    [Fact]
    public void Generate_ContainsHostIpInNodeLabels()
    {
        var snapshot = CreateSnapshot(("10.0.0.5", "myserver", "SSH Server", [22]));

        string xml = DrawIoExporter.Generate(snapshot, Localize);

        Assert.Contains("10.0.0.5", xml);
        Assert.Contains("myserver", xml);
    }

    [Fact]
    public void Generate_NullLocalizer_Throws()
    {
        var snapshot = CreateSnapshot(("192.168.1.1", null, "SSH Server", [22]));

        Assert.Throws<ArgumentNullException>(() => DrawIoExporter.Generate(snapshot, null!));
    }

    // -- Role grouping -------------------------------------------------

    [Fact]
    public void Generate_GroupsHostsByRole()
    {
        var snapshot = CreateSnapshot(
            ("192.168.1.1", null, "SSH Server", [22]),
            ("192.168.1.2", null, "SSH Server", [22]),
            ("192.168.1.3", null, "Web Server", [80]));

        string xml = DrawIoExporter.Generate(snapshot, Localize);
        var doc = XDocument.Parse(xml);

        // Should have swimlane cells for each role
        var cells = doc.Descendants("mxCell").ToList();
        Assert.Contains(cells, c => c.Attribute("value")?.Value == "SSH Server");
        Assert.Contains(cells, c => c.Attribute("value")?.Value == "Web Server");

        // One swimlane per role, not one per host.
        Assert.Equal(1, cells.Count(c => c.Attribute("value")?.Value == "SSH Server"));
    }

    [Fact]
    public void Generate_PingOnlyHostsGroupedSeparately()
    {
        var snapshot = CreateSnapshot(
            ("192.168.1.1", null, "SSH Server", [22]),
            ("192.168.1.2", null, null, []));

        string xml = DrawIoExporter.Generate(snapshot, Localize);

        Assert.Contains(Localize(DrawIoExporter.GroupPingOnlyKey), xml);
    }

    [Fact]
    public void Generate_HostWithOpenPortsButNoRole_GoesToUnclassified()
    {
        var snapshot = CreateSnapshot(("192.168.1.9", null, null, [8080]));

        string xml = DrawIoExporter.Generate(snapshot, Localize);

        Assert.Contains(Localize(DrawIoExporter.GroupUnclassifiedKey), xml);
        Assert.DoesNotContain(Localize(DrawIoExporter.GroupPingOnlyKey), xml);
    }

    // -- Localization --------------------------------------------------

    [Fact]
    public void Generate_RoutesEveryUserFacingStringThroughTheLocalizer()
    {
        var hosts = new List<HostScanResult>
        {
            new("192.168.1.1", "srv", true, 0,
                [new ServiceResult(22, true, "SSH", null, null, 0)],
                new RoleMatch("SSH Server", 50, []),
                [new RoleMatch("SSH Server", 50, [])],
                OsFingerprint: new OsFingerprint("Linux", "banner", 80),
                MacAddress: "AA-BB-CC-DD-EE-FF",
                NetBiosName: "SRV",
                SnmpInfo: new SnmpInfo(null, "snmp-name", null)),
            new("192.168.1.2", null, true, 0, [], null, [])
        };

        string xml = DrawIoExporter.Generate(CreateSnapshot(hosts), Marked);

        // Every label the exporter emits must come back marked: an English string
        // left in the source would appear here unmarked.
        Assert.Contains("[[" + DrawIoExporter.GroupPingOnlyKey + "]]", xml);
        Assert.Contains("[[" + DrawIoExporter.LabelPortsKey + "]]22", xml);
        Assert.Contains("[[" + DrawIoExporter.LabelOsKey + "]]Linux", xml);
        Assert.Contains("[[" + DrawIoExporter.LabelNetBiosKey + "]]SRV", xml);
        Assert.Contains("[[" + DrawIoExporter.LabelSnmpKey + "]]snmp-name", xml);

        // The English wording must not survive a localizer that returns none of it.
        Assert.DoesNotContain("Ping Only", xml);
        Assert.DoesNotContain("Ports:", xml);
        Assert.DoesNotContain("NetBIOS:", xml);
    }

    [Fact]
    public void Generate_MacLineOnlyWhenNoOpenPort()
    {
        var withPorts = new List<HostScanResult>
        {
            new("192.168.1.1", null, true, 0,
                [new ServiceResult(22, true, "SSH", null, null, 0)],
                new RoleMatch("SSH Server", 50, []),
                [new RoleMatch("SSH Server", 50, [])],
                MacAddress: "AA-BB-CC-DD-EE-FF")
        };
        var pingOnly = new List<HostScanResult>
        {
            new("192.168.1.2", null, true, 0, [], null, [], MacAddress: "11-22-33-44-55-66")
        };

        Assert.DoesNotContain("[[" + DrawIoExporter.LabelMacKey + "]]",
            DrawIoExporter.Generate(CreateSnapshot(withPorts), Marked));
        Assert.Contains("[[" + DrawIoExporter.LabelMacKey + "]]11-22-33-44-55-66",
            DrawIoExporter.Generate(CreateSnapshot(pingOnly), Marked));
    }

    // -- Palette coherence ---------------------------------------------

    /// <summary>
    /// Every role that earns its own swimlane colour must give its hosts the same
    /// colour. The two tables used to be written separately and disagreed on six
    /// roles: an "SSH Server" lane was green and its hosts were the default grey.
    /// </summary>
    [Fact]
    public void Generate_NodeFillMatchesItsSwimlaneFill()
    {
        string[] colouredRoles =
        [
            "Active Directory",
            "Web Server",
            "Web Server (HTTPS)",
            "Database (MySQL)",
            "Mail Server",
            "Windows RDP",
            "SSH Server",
            "IP Camera (RTSP)",
            "NAS (Synology)",
            "Router/Gateway",
            "Managed Switch",
            "Network Printer",
            "Network Equipment (SNMP)",
        ];

        var mismatches = new List<string>();

        foreach (string role in colouredRoles)
        {
            var snapshot = CreateSnapshot(("192.168.1.1", null, role, [443]));
            string xml = DrawIoExporter.Generate(snapshot, Localize);
            var cells = XDocument.Parse(xml).Descendants("mxCell").ToList();

            var lane = cells.Single(c => c.Attribute("style")?.Value.Contains("swimlane") == true);
            var node = cells.Single(c =>
                c.Attribute("id")?.Value.StartsWith(DrawIoExporter.HostCellPrefix, StringComparison.Ordinal) == true);

            string laneFill = FillColor(lane.Attribute("style")!.Value);
            string nodeFill = FillColor(node.Attribute("style")!.Value);

            if (!string.Equals(laneFill, nodeFill, StringComparison.OrdinalIgnoreCase))
            {
                mismatches.Add($"{role}: lane {laneFill} vs node {nodeFill}");
            }
        }

        Assert.True(mismatches.Count == 0,
            "Swimlane and node fills disagree:\n" + string.Join("\n", mismatches));
    }

    /// <summary>
    /// The coherence test above passes trivially if every role falls back to the
    /// default colour, so each named role must carry a colour of its own: not the
    /// default, and not one another's.
    /// </summary>
    [Fact]
    public void Generate_NamedRolesEachCarryTheirOwnFill()
    {
        string defaultFill = LaneFillFor("No Such Role In The Classifier");

        string[] roles = ["Active Directory", "Web Server", "SSH Server", "Mail Server", "Windows RDP"];

        var fills = new Dictionary<string, string>(StringComparer.Ordinal);
        var usingDefault = new List<string>();

        foreach (string role in roles)
        {
            string fill = LaneFillFor(role);
            fills[role] = fill;

            if (string.Equals(fill, defaultFill, StringComparison.OrdinalIgnoreCase))
            {
                usingDefault.Add(role);
            }
        }

        Assert.True(usingDefault.Count == 0,
            "These roles fell back to the default fill " + defaultFill + ": "
            + string.Join(", ", usingDefault));

        Assert.Equal(roles.Length, fills.Values.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    private static string LaneFillFor(string role)
    {
        string xml = DrawIoExporter.Generate(CreateSnapshot(("192.168.1.1", null, role, [443])), Localize);
        var lane = XDocument.Parse(xml).Descendants("mxCell")
            .Single(c => c.Attribute("style")?.Value.Contains("swimlane") == true);
        return FillColor(lane.Attribute("style")!.Value);
    }

    [Fact]
    public void Generate_PingOnlyNodesAreVisuallyMuted()
    {
        var snapshot = CreateSnapshot(("192.168.1.2", null, null, []));

        string xml = DrawIoExporter.Generate(snapshot, Localize);
        var node = XDocument.Parse(xml).Descendants("mxCell")
            .Single(c => c.Attribute("id")?.Value.StartsWith(DrawIoExporter.HostCellPrefix, StringComparison.Ordinal) == true);

        Assert.Contains("dashed=1", node.Attribute("style")!.Value);
    }

    private static string FillColor(string style)
    {
        var match = Regex.Match(style, @"fillColor=([^;]+)", RegexOptions.CultureInvariant);
        return match.Success ? match.Groups[1].Value : string.Empty;
    }

    // -- XML escaping --------------------------------------------------

    [Fact]
    public void Generate_EscapesSpecialCharactersInHostname()
    {
        var snapshot = CreateSnapshot(("192.168.1.1", "host<>&\"name", null, [22]));

        string xml = DrawIoExporter.Generate(snapshot, Localize);

        // Must be valid XML despite special chars in hostname
        var doc = XDocument.Parse(xml);
        Assert.NotNull(doc.Root);
        Assert.Contains("&lt;", xml);
        Assert.Contains("&amp;", xml);
        Assert.Contains("&gt;", xml);
        Assert.Contains("&quot;", xml);
    }

    // -- Culture independence ------------------------------------------

    /// <summary>
    /// The diagram name carries a timestamp built with a custom format string, in
    /// which ":" is a culture-dependent time separator. A French or Arabic desktop
    /// must still produce the same text.
    /// </summary>
    [Fact]
    public void Generate_TimestampIsCultureInvariant()
    {
        var snapshot = new NetworkScanSnapshot(
            "test",
            new DateTime(2026, 3, 4, 5, 6, 7, DateTimeKind.Utc),
            new ScanProfile("192.168.1.0/24", ScanDepth.Quick, null, 50, 2000, false, false),
            null, TimeSpan.Zero, []);

        var previous = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new CultureInfo("ar-SA");
            string xml = DrawIoExporter.Generate(snapshot, Localize);

            Assert.Contains("2026-03-04 05:06", xml);
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = previous;
        }
    }

    // -- Enrichment data in labels -------------------------------------

    [Fact]
    public void Generate_IncludesManufacturerInLabel()
    {
        var hosts = new List<HostScanResult>
        {
            new("192.168.1.1", null, true, 0,
                [new ServiceResult(22, true, "SSH", null, null, 0)],
                new RoleMatch("SSH Server", 50, []),
                [new RoleMatch("SSH Server", 50, [])],
                MacAddress: "AA-BB-CC-DD-EE-FF",
                Manufacturer: "Cisco")
        };

        string xml = DrawIoExporter.Generate(CreateSnapshot(hosts), Localize);

        Assert.Contains("Cisco", xml);
    }

    [Fact]
    public void Generate_IncludesOsFingerprintInLabel()
    {
        var hosts = new List<HostScanResult>
        {
            new("192.168.1.1", null, true, 0, [],
                null, [],
                OsFingerprint: new OsFingerprint("Linux/Ubuntu", "banner", 85))
        };

        string xml = DrawIoExporter.Generate(CreateSnapshot(hosts), Localize);

        Assert.Contains("Linux/Ubuntu", xml);
    }

    [Fact]
    public void Generate_IncludesExpiredCertWarning()
    {
        var cert = new CertificateInfo(
            "CN=test", "CN=issuer",
            DateTime.UtcNow.AddYears(-2), DateTime.UtcNow.AddDays(-30),
            true, false, "RSA 2048", "SHA256", [], "TLS 1.2", "AABB");

        var hosts = new List<HostScanResult>
        {
            new("192.168.1.1", null, true, 0,
                [new ServiceResult(443, true, "HTTPS", null, null, 0, cert)],
                new RoleMatch("Web Server", 70, []),
                [new RoleMatch("Web Server", 70, [])])
        };

        string xml = DrawIoExporter.Generate(CreateSnapshot(hosts), Localize);

        Assert.Contains("EXPIRED", xml);
    }

    // -- Empty snapshot ------------------------------------------------

    [Fact]
    public void Generate_EmptySnapshot_ProducesValidXml()
    {
        string xml = DrawIoExporter.Generate(CreateSnapshot(new List<HostScanResult>()), Localize);

        var doc = XDocument.Parse(xml);
        Assert.NotNull(doc.Root);
    }

    // -- Helpers -------------------------------------------------------

    private static NetworkScanSnapshot CreateSnapshot(List<HostScanResult> hosts)
    {
        return new NetworkScanSnapshot(
            "test", DateTime.UtcNow,
            new ScanProfile("192.168.1.0/24", ScanDepth.Quick, null, 50, 2000, false, false),
            null, TimeSpan.Zero, hosts);
    }

    private static NetworkScanSnapshot CreateSnapshot(
        params (string Ip, string? Hostname, string? Role, int[] Ports)[] hosts)
    {
        var hostResults = hosts.Select(h =>
        {
            var services = h.Ports.Select(p =>
                new ServiceResult(p, true, null, null, null, 0)).ToList();
            var role = h.Role is not null
                ? new RoleMatch(h.Role, 80, ["test"])
                : null;
            return new HostScanResult(
                h.Ip, h.Hostname, true, 1,
                services, role, role is not null ? [role] : []);
        }).ToList();

        return CreateSnapshot(hostResults);
    }
}
