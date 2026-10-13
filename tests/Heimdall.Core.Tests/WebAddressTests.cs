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

using Heimdall.Core.Configuration;

namespace Heimdall.Core.Tests;

/// <summary>
/// The address a web profile opens (issue #558), built from its host, port, scheme and path.
/// </summary>
public sealed class WebAddressTests
{
    [Theory]
    [InlineData("ilo.example.test", 443, false, null, "https://ilo.example.test/")]
    [InlineData("ilo.example.test", 80, true, null, "http://ilo.example.test/")]
    [InlineData("ilo.example.test", 8443, false, null, "https://ilo.example.test:8443/")]
    [InlineData("ilo.example.test", 443, true, null, "http://ilo.example.test:443/")]
    [InlineData("10.0.0.5", 443, false, "login.html", "https://10.0.0.5/login.html")]
    [InlineData("10.0.0.5", 443, false, "/cgi-bin/main?x=1", "https://10.0.0.5/cgi-bin/main?x=1")]
    [InlineData("  nas.example.test  ", 5001, false, "  ", "https://nas.example.test:5001/")]
    [InlineData("fe80::1", 443, false, null, "https://[fe80::1]/")]
    [InlineData("[fe80::1]", 8080, true, null, "http://[fe80::1]:8080/")]
    public void TryBuild_FormsTheAddress(string host, int port, bool useHttp, string? path, string expected)
    {
        Assert.True(WebAddress.TryBuild(host, port, useHttp, path, out Uri? address));
        Assert.Equal(expected, address!.AbsoluteUri);
    }

    /// <summary>
    /// A URL pasted into the host field is the usual mistake; it is refused rather than turned
    /// into "https://https//ilo/".
    /// </summary>
    [Theory]
    [InlineData("https://ilo.example.test", 443)]
    [InlineData("ilo.example.test/login", 443)]
    [InlineData("admin@ilo.example.test", 443)]
    [InlineData("ilo.example.test:8443", 443)]
    [InlineData("ilo example", 443)]
    [InlineData("", 443)]
    [InlineData(null, 443)]
    [InlineData("ilo.example.test", 0)]
    [InlineData("ilo.example.test", 65536)]
    public void TryBuild_RefusesWhatIsNotAHost(string? host, int port)
    {
        Assert.False(WebAddress.TryBuild(host, port, useHttp: false, path: null, out Uri? address));
        Assert.Null(address);
    }

    [Fact]
    public void From_ReadsTheProfileFields()
    {
        var profile = new ServerProfileDto
        {
            ConnectionType = "WEB",
            RemoteServer = "idrac.example.test",
            WebPort = 8080,
            WebUseHttp = true,
            WebPath = "/start.html",
        };

        Assert.Equal("http://idrac.example.test:8080/start.html", WebAddress.From(profile)!.AbsoluteUri);
    }

    [Fact]
    public void ANewProfile_DefaultsToHttpsOn443()
    {
        var profile = new ServerProfileDto { RemoteServer = "switch.example.test" };

        Assert.Equal("https://switch.example.test/", WebAddress.From(profile)!.AbsoluteUri);
    }

    [Theory]
    [InlineData(false, 443)]
    [InlineData(true, 80)]
    public void DefaultPortFor_FollowsTheScheme(bool useHttp, int expected)
    {
        Assert.Equal(expected, WebAddress.DefaultPortFor(useHttp));
    }

    [Fact]
    public void WebIsAKnownConnectionType_WithADisplayName()
    {
        Assert.True(ConnectionTypeCatalog.IsKnown("WEB"));
        Assert.Equal("WEB", ConnectionTypeCatalog.Canonicalize("web"));
        Assert.Equal("ConnectionTypeWeb", ConnectionTypeCatalog.GetDisplayNameKey("WEB"));
        Assert.True(ConnectionTypeCatalog.RequiresRemoteServer("WEB"));
    }

    [Theory]
    [InlineData("HTTPS", false, 443)]
    [InlineData("HTTP", true, 80)]
    public void AnMRemoteNgWebConnection_ImportsAsAWebProfile(string protocol, bool expectHttp, int expectPort)
    {
        string xml = $"""
            <?xml version="1.0" encoding="utf-8"?>
            <mrng:Connections xmlns:mrng="http://mremoteng.org" Name="Connections" FullFileEncryption="false" ConfVersion="2.6">
              <Node Name="iLO" Type="Connection" Hostname="ilo.example.test" Protocol="{protocol}" Username="admin" />
            </mrng:Connections>
            """;

        var result = MRemoteNgImporter.Parse(xml);

        ServerProfileDto ilo = Assert.Single(result.Servers);
        Assert.Equal("WEB", ilo.ConnectionType);
        Assert.Equal(expectHttp, ilo.WebUseHttp);
        Assert.Equal(expectPort, ilo.WebPort);
        Assert.Equal("admin", ilo.WebUsername);
    }

    [Fact]
    public void AnMRemoteNgWebConnection_KeepsItsExplicitPort()
    {
        const string xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <mrng:Connections xmlns:mrng="http://mremoteng.org" Name="Connections" FullFileEncryption="false" ConfVersion="2.6">
              <Node Name="NAS" Type="Connection" Hostname="nas.example.test" Protocol="HTTPS" Port="5001" />
            </mrng:Connections>
            """;

        ServerProfileDto nas = Assert.Single(MRemoteNgImporter.Parse(xml).Servers);

        Assert.Equal(5001, nas.WebPort);
        Assert.Equal("https://nas.example.test:5001/", WebAddress.From(nas)!.AbsoluteUri);
    }
}
