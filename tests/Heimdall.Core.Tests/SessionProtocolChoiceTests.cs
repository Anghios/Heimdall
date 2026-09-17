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
/// Covers which session a discovered host is offered.
/// </summary>
/// <remarks>
/// Two surfaces ask this about the same scan: the cartography context menu and
/// the exported diagram's clickable nodes. The answer used to be spelled out
/// inside a click handler, where only one of them could reach it.
/// </remarks>
public sealed class SessionProtocolChoiceTests
{
    [Theory]
    [InlineData(new[] { 22 }, "SSH", 22)]
    [InlineData(new[] { 3389 }, "RDP", 3389)]
    [InlineData(new[] { 5900 }, "VNC", 5900)]
    [InlineData(new[] { 23 }, "TELNET", 23)]
    public void Choose_TakesTheProtocolTheOpenPortImplies(int[] ports, string protocol, int port)
    {
        Assert.Equal((protocol, port), SessionProtocolChoice.Choose(ports));
    }

    /// <summary>
    /// A host offering both is usually a Windows machine, and the desktop is the
    /// session the user means.
    /// </summary>
    [Fact]
    public void Choose_PrefersRemoteDesktopOverAShell()
    {
        Assert.Equal(("RDP", 3389), SessionProtocolChoice.Choose([22, 3389, 5900]));
    }

    [Fact]
    public void Choose_PrefersAShellOverScreenSharing()
    {
        Assert.Equal(("SSH", 22), SessionProtocolChoice.Choose([5900, 22]));
    }

    [Fact]
    public void Choose_IgnoresTheOrderTheyWereFoundIn()
    {
        Assert.Equal(SessionProtocolChoice.Choose([3389, 22]), SessionProtocolChoice.Choose([22, 3389]));
    }

    [Theory]
    [InlineData(new int[0])]
    [InlineData(new[] { 80, 443, 9100 })]
    public void Choose_OffersNothingWhenNoSessionIsOnOffer(int[] ports)
    {
        Assert.Null(SessionProtocolChoice.Choose(ports));
    }

    [Fact]
    public void Choose_OnNoPortsAtAll_OffersNothing()
    {
        Assert.Null(SessionProtocolChoice.Choose(null));
    }

    [Fact]
    public void ForHost_BuildsARequestTheLinkFormatAccepts()
    {
        var request = SessionProtocolChoice.ForHost("10.0.0.5", [3389]);

        Assert.NotNull(request);
        Assert.True(SessionLaunchRequest.TryParse(request!.ToUri().AbsoluteUri, out var parsed));
        Assert.Equal(request, parsed);
    }

    [Fact]
    public void ForHost_WithoutAHost_OffersNothing()
    {
        Assert.Null(SessionProtocolChoice.ForHost("   ", [22]));
    }

    /// <summary>
    /// Every protocol this can choose must be one a link may carry, or a node
    /// would be drawn with a link that never passes validation.
    /// </summary>
    [Fact]
    public void EveryChoosableProtocol_IsOneALinkMayCarry()
    {
        int[] everyPreferredPort = [22, 3389, 5900, 23];

        foreach (var port in everyPreferredPort)
        {
            var choice = SessionProtocolChoice.Choose([port]);

            Assert.NotNull(choice);
            Assert.Contains(choice!.Value.Protocol, SessionLaunchRequest.SupportedProtocols);
        }
    }
}
