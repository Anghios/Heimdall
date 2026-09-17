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

using Heimdall.Core.Models;

namespace Heimdall.Core.Tests;

/// <summary>
/// Covers the link a diagram node carries and the checks it must pass before
/// anything acts on it.
/// </summary>
/// <remarks>
/// A .drawio file can come from anywhere, and this link asks Heimdall to reach a
/// machine and offer it credentials. Everything a document can put in one is
/// therefore untrusted: the protocol comes from a fixed list, the host must be an
/// address or a plain host name, and the port must be in range.
/// </remarks>
public sealed class SessionLaunchRequestTests
{
    [Fact]
    public void ToUri_CarriesProtocolHostAndPort()
    {
        var uri = new SessionLaunchRequest("10.0.0.5", 3389, "RDP").ToUri();

        Assert.Equal("heimdall://session/RDP/10.0.0.5:3389", uri.AbsoluteUri);
    }

    [Fact]
    public void ToUri_CarriesTheUsernameWhenThereIsOne()
    {
        var uri = new SessionLaunchRequest("10.0.0.5", 22, "SSH", "root").ToUri();

        Assert.Contains("user=root", uri.AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    public void ToUri_OmitsThePortWhenThereIsNone()
    {
        var uri = new SessionLaunchRequest("host.example", null, "SSH").ToUri();

        Assert.Equal("heimdall://session/SSH/host.example", uri.AbsoluteUri);
    }

    /// <summary>What a node writes must read back as what it meant.</summary>
    [Theory]
    [InlineData("10.0.0.5", 22, "SSH", null)]
    [InlineData("10.0.0.5", 3389, "RDP", "administrator")]
    [InlineData("server-01.lab.example", 5900, "VNC", null)]
    [InlineData("server-01", null, "TELNET", "ops")]
    public void ToUri_AndBack_IsTheSameRequest(string host, int? port, string protocol, string? username)
    {
        var original = new SessionLaunchRequest(host, port, protocol, username);

        Assert.True(SessionLaunchRequest.TryParse(original.ToUri().AbsoluteUri, out var parsed));
        Assert.Equal(original, parsed);
    }

    [Fact]
    public void TryParse_ReadsAWellFormedLink()
    {
        Assert.True(SessionLaunchRequest.TryParse("heimdall://session/SSH/192.168.1.1:2222?user=ops", out var request));

        Assert.Equal("192.168.1.1", request.Host);
        Assert.Equal(2222, request.Port);
        Assert.Equal("SSH", request.Protocol);
        Assert.Equal("ops", request.Username);
    }

    [Fact]
    public void TryParse_AcceptsALowerCaseProtocol()
    {
        Assert.True(SessionLaunchRequest.TryParse("heimdall://session/ssh/10.0.0.1", out var request));

        Assert.Equal("SSH", request.Protocol);
    }

    /// <summary>
    /// Every one of these is a document trying to reach somewhere it should not,
    /// or to name something Heimdall would have to interpret loosely.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("https://example.com")]
    [InlineData("heimdall://elsewhere/SSH/10.0.0.1")]
    [InlineData("heimdall://session/SSH")]
    [InlineData("heimdall://session/SSH/10.0.0.1/extra")]
    [InlineData("heimdall://session/FTP/10.0.0.1")]
    [InlineData("heimdall://session/SHELL/10.0.0.1")]
    [InlineData("heimdall://session/SSH/10.0.0.1:0")]
    [InlineData("heimdall://session/SSH/10.0.0.1:65536")]
    [InlineData("heimdall://session/SSH/10.0.0.1:notaport")]
    [InlineData("heimdall://session/SSH/bad_host!")]
    [InlineData("heimdall://session/SSH/-leading-hyphen")]
    [InlineData("heimdall://session/SSH/host --rm")]
    [InlineData("heimdall://user:pass@session/SSH/10.0.0.1")]
    [InlineData("file:///etc/passwd")]
    [InlineData("javascript:alert(1)")]
    public void TryParse_RefusesAnythingThatIsNotOne(string? candidate)
    {
        Assert.False(SessionLaunchRequest.TryParse(candidate, out _));
    }

    /// <summary>
    /// A host name at the DNS limit is legitimate; one past it is not, and a long
    /// value is exactly what a crafted document would carry.
    /// </summary>
    [Fact]
    public void TryParse_RefusesAnOverlongHost()
    {
        var label = new string('a', 60);
        var tooLong = string.Join('.', Enumerable.Repeat(label, 5));

        Assert.False(SessionLaunchRequest.TryParse($"heimdall://session/SSH/{tooLong}", out _));
    }

    [Fact]
    public void Destination_ReadsAsItWouldInAPrompt()
    {
        Assert.Equal("10.0.0.5:22", new SessionLaunchRequest("10.0.0.5", 22, "SSH").Destination);
        Assert.Equal("10.0.0.5", new SessionLaunchRequest("10.0.0.5", null, "SSH").Destination);
    }

    [Fact]
    public void SupportedProtocols_AreUpperCaseAndDistinct()
    {
        Assert.Equal(
            SessionLaunchRequest.SupportedProtocols.Count,
            SessionLaunchRequest.SupportedProtocols.Distinct(StringComparer.Ordinal).Count());

        Assert.All(
            SessionLaunchRequest.SupportedProtocols,
            protocol => Assert.Equal(protocol.ToUpperInvariant(), protocol));
    }
}
