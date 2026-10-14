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

using Heimdall.App.Services;
using Heimdall.App.ViewModels;

namespace Heimdall.App.Tests;

/// <summary>
/// The tab strip with thirty sessions open: what the tooltip tells apart, where a dragged tab
/// can reach, and which drops pop a tab out into a window of its own.
/// </summary>
public sealed class SessionTabStripUxTests
{
    private const double Viewport = 600d;

    [Theory]
    [InlineData(0d, -TabStripDragAutoScroll.StepWidth)]
    [InlineData(TabStripDragAutoScroll.EdgeZoneWidth - 1d, -TabStripDragAutoScroll.StepWidth)]
    [InlineData(TabStripDragAutoScroll.EdgeZoneWidth, 0d)]
    [InlineData(Viewport / 2d, 0d)]
    [InlineData(Viewport - TabStripDragAutoScroll.EdgeZoneWidth, 0d)]
    [InlineData(Viewport - TabStripDragAutoScroll.EdgeZoneWidth + 1d, TabStripDragAutoScroll.StepWidth)]
    [InlineData(Viewport, TabStripDragAutoScroll.StepWidth)]
    [InlineData(-1d, 0d)]
    [InlineData(Viewport + 1d, 0d)]
    public void AutoScroll_ScrollsOnlyInsideTheEndZones(double pointerX, double expected)
    {
        Assert.Equal(expected, TabStripDragAutoScroll.Step(pointerX, Viewport));
    }

    [Fact]
    public void AutoScroll_AViewportTooNarrowForTwoZones_NeverScrolls()
    {
        Assert.Equal(0d, TabStripDragAutoScroll.Step(1d, TabStripDragAutoScroll.EdgeZoneWidth));
    }

    [Theory]
    // Let go over the session tree or the toolbar while reordering: stays in the strip.
    [InlineData(false, false, true, false, false)]
    // Outside the window: the detach gesture.
    [InlineData(false, false, false, false, true)]
    // Over the session content area: the only outside a maximised single-monitor window has.
    [InlineData(false, false, true, true, true)]
    // Escape, or a drop a target took, never detaches.
    [InlineData(false, true, false, false, false)]
    [InlineData(true, false, false, false, false)]
    public void ShouldDetach_OnlyOutsideTheWindowOrOverTheSessionContent(
        bool accepted,
        bool cancelled,
        bool insideWindow,
        bool overContent,
        bool expected)
    {
        Assert.Equal(expected, TabDropDecision.ShouldDetach(accepted, cancelled, insideWindow, overContent));
    }

    [Fact]
    public void ToolTip_NamesTheEndpointUserAndFolder_SoTwoCutTitlesCanBeToldApart()
    {
        SessionTabViewModel tab = new() { Title = "web-prod-frontend-01" };
        ServerItemViewModel profile = new()
        {
            ConnectionType = "SSH",
            // The port first: the endpoint is written when the host is.
            RemotePort = 22,
            RemoteServer = "web-prod-frontend-01.corp.example",
            Username = "deploy",
            Group = "Prod/Web",
        };

        string tooltip = SessionTabToolTipComposer.Compose(tab, profile, Localize, Format);

        Assert.Equal(
            string.Join(
                Environment.NewLine,
                "web-prod-frontend-01",
                $"SessionTabToolTipEndpoint[web-prod-frontend-01.corp.example:22|{profile.ProtocolDisplayName}]",
                "SessionTabToolTipUser[deploy]",
                "SessionTabToolTipFolder[Prod/Web]"),
            tooltip);
    }

    [Fact]
    public void ToolTip_ADroppedToolTab_SaysItsStateAndNothingItDoesNotKnow()
    {
        SessionTabViewModel tab = new() { Title = "Ping" };
        tab.PrimaryPane.Status = SessionStatusTokens.Disconnected;

        string tooltip = SessionTabToolTipComposer.Compose(tab, profile: null, Localize, Format);

        Assert.Equal(
            string.Join(Environment.NewLine, "Ping", "SessionTabToolTipState[<SessionStatusDisconnected>]"),
            tooltip);
    }

    [Fact]
    public void ToolTip_AFreeTextFailure_ShowsTheReasonAsWritten()
    {
        SessionTabViewModel tab = new() { Title = "srv01" };
        tab.PrimaryPane.Status = "Host key mismatch";

        string tooltip = SessionTabToolTipComposer.Compose(tab, profile: null, Localize, Format);

        Assert.EndsWith("SessionTabToolTipState[Host key mismatch]", tooltip, StringComparison.Ordinal);
    }

    [Fact]
    public void ToolTip_AnEstablishedSession_CarriesNoStateLine()
    {
        SessionTabViewModel tab = new() { Title = "srv01" };
        tab.PrimaryPane.Status = SessionStatusTokens.Connected;

        Assert.Equal("srv01", SessionTabToolTipComposer.Compose(tab, profile: null, Localize, Format));
    }

    private static string Localize(string key) => $"<{key}>";

    private static string Format(string key, object[] args) => $"{key}[{string.Join('|', args)}]";
}
