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

using System.IO;
using System.Xml.Linq;
using Heimdall.App.Services;
using Heimdall.App.ViewModels;
using Heimdall.Core.Configuration;

namespace Heimdall.App.Tests;

/// <summary>
/// The properties panel under the session tree (issue #557): where it sits, what its
/// authentication line says, and that it shows the same fields as the detail pane.
/// </summary>
public sealed class SessionPropertiesPanelTests
{
    private static readonly XNamespace PresentationNamespace =
        "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    private static readonly XNamespace XamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    public void ThePanel_ShowsOnlyWhenAskedForAndTheSessionsTabIsShown(
        bool enabled,
        bool toolsTabActive,
        bool expected)
    {
        Assert.Equal(expected, SessionPropertiesPanelLayout.IsShown(enabled, toolsTabActive));
    }

    [Theory]
    [InlineData(10d, SessionPropertiesPanelLayout.MinHeight)]
    [InlineData(300d, 300d)]
    [InlineData(99999d, SessionPropertiesPanelLayout.MaxHeight)]
    [InlineData(double.NaN, SessionPropertiesPanelLayout.DefaultHeight)]
    [InlineData(double.PositiveInfinity, SessionPropertiesPanelLayout.DefaultHeight)]
    public void ASavedHeight_IsBroughtBackInsideTheRange(double saved, double expected)
    {
        Assert.Equal(expected, SessionPropertiesPanelLayout.ClampHeight(saved));
    }

    /// <summary>
    /// The window clamps with its own constants and the settings loader with the attribute; a
    /// height one accepts and the other refuses would be saved, then moved on the next start.
    /// </summary>
    [Fact]
    public void TheLayoutBounds_AreTheBoundsTheSettingsFileAccepts()
    {
        SettingRange range = SettingRanges.Of(nameof(AppSettings.SessionPropertiesPanelHeight));

        Assert.Equal(SessionPropertiesPanelLayout.MinHeight, range.Min);
        Assert.Equal(SessionPropertiesPanelLayout.MaxHeight, range.Max);
        Assert.Equal(SessionPropertiesPanelLayout.DefaultHeight, new AppSettings().SessionPropertiesPanelHeight);
    }

    [Fact]
    public void AHeightSavedOnATallerWindow_LeavesTheTreeItsMinimum()
    {
        double fitted = SessionPropertiesPanelLayout.FitHeight(900d, 500d);

        Assert.Equal(500d - SessionPropertiesPanelLayout.TreeMinHeight, fitted);
    }

    [Fact]
    public void AHeightThatFits_IsKeptAsSaved()
    {
        Assert.Equal(250d, SessionPropertiesPanelLayout.FitHeight(250d, 800d));
    }

    [Fact]
    public void ASidebarNotYetMeasured_KeepsTheSavedHeight()
    {
        Assert.Equal(250d, SessionPropertiesPanelLayout.FitHeight(250d, 0d));
    }

    [Fact]
    public void ACrampedSidebar_StillGivesThePanelItsMinimum()
    {
        Assert.Equal(SessionPropertiesPanelLayout.MinHeight, SessionPropertiesPanelLayout.FitHeight(250d, 150d));
    }

    [Theory]
    [InlineData("RDP", false, "Credential provider")]
    [InlineData("RDP", true, "Password")]
    [InlineData("SSH", false, "SSH agent + Credential provider")]
    [InlineData("SSH", true, "Password")]
    [InlineData("FTP", false, "Credential provider")]
    public void TheCredentialProvider_IsNamedWhereTheConnectionWouldAskIt(
        string connectionType,
        bool hasPassword,
        string expected)
    {
        ServerItemViewModel row = ServerItemViewModel.FromDto(Server(connectionType, hasPassword));

        row.UsesCredentialProvider = true;

        Assert.Equal(expected, row.AuthSummary);
    }

    [Theory]
    [InlineData("admin", "Username + Credential provider")]
    [InlineData(null, "No saved credentials")]
    public void TheCredentialProvider_IsNamedForAWebConsoleOnlyWhenItSignsIn(string? username, string expected)
    {
        ServerItemViewModel row = ServerItemViewModel.FromDto(new ServerProfileDto
        {
            Id = "w",
            DisplayName = "iLO",
            ConnectionType = "WEB",
            RemoteServer = "ilo.example.test",
            WebUsername = username,
        });

        row.UsesCredentialProvider = true;

        Assert.Equal(expected, row.AuthSummary);
    }

    [Fact]
    public void TheCredentialProvider_IsNotNamedForAWinRmSessionRunningAsTheCurrentUser()
    {
        ServerProfileDto dto = Server("WINRM", hasPassword: false);
        dto.WinRmIdentityMode = WinRmIdentityMode.CurrentUser;
        ServerItemViewModel row = ServerItemViewModel.FromDto(dto);

        row.UsesCredentialProvider = true;

        Assert.Equal("Current user", row.AuthSummary);
    }

    [Fact]
    public void SwitchingTheCredentialProvider_AnnouncesTheNewAuthenticationLine()
    {
        ServerItemViewModel row = ServerItemViewModel.FromDto(Server("RDP", hasPassword: false));
        List<string?> changed = [];
        row.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        row.UsesCredentialProvider = true;
        Assert.Contains(nameof(ServerItemViewModel.AuthSummary), changed);
        Assert.Equal("Credential provider", row.AuthSummary);

        changed.Clear();
        row.UsesCredentialProvider = false;
        Assert.Contains(nameof(ServerItemViewModel.AuthSummary), changed);
        Assert.Equal("No saved credentials", row.AuthSummary);
    }

    /// <summary>
    /// The detail pane and the panel host one control, so neither can list a field the other
    /// lacks. A copy of the rows in either place is what the issue asked to avoid.
    /// </summary>
    [Fact]
    public void TheDetailPaneAndThePanel_HostTheSameControl()
    {
        XDocument window = LoadXaml("MainWindow.xaml");

        AssertHostsPropertiesView(FindNamed(window, "SessionDetailPanel"));
        AssertHostsPropertiesView(FindNamed(window, "PropertiesPanel"));

        // The rows the pane used to carry inline must not come back beside the control.
        Assert.Null(window.Descendants().FirstOrDefault(e => (string?)e.Attribute(XamlNamespace + "Name") == "Mw_DetailAuthLabel"));
    }

    [Fact]
    public void TheAuthenticationLine_ComesFirst()
    {
        XDocument control = LoadXaml(Path.Combine("Controls", "SessionPropertiesView.xaml"));
        XElement auth = FindNamed(control, "Spv_Auth");

        Assert.Equal("0", (string?)auth.Attribute("Grid.Row"));
        Assert.Equal("{Binding AuthSummary}", (string?)auth.Attribute("Text"));
    }

    /// <summary>
    /// RemotePort is the RDP port whatever the profile's type, so an SSH session on 22 was shown
    /// on 3389. Endpoint carries the port of the session's own protocol.
    /// </summary>
    [Fact]
    public void TheEndpointLine_ShowsTheProtocolsOwnPort()
    {
        XDocument control = LoadXaml(Path.Combine("Controls", "SessionPropertiesView.xaml"));

        Assert.Equal("{Binding Endpoint}", (string?)FindNamed(control, "Spv_Endpoint").Attribute("Text"));
        Assert.DoesNotContain(
            control.Descendants().Attributes(),
            attribute => attribute.Value.Contains("RemotePort", StringComparison.Ordinal));

        ServerItemViewModel row = ServerItemViewModel.FromDto(new ServerProfileDto
        {
            Id = "ssh",
            DisplayName = "ssh",
            ConnectionType = "SSH",
            RemoteServer = "host.example.test",
            SshPort = 2222,
        });
        Assert.Equal("host.example.test:2222", row.Endpoint);
    }

    [Fact]
    public void AValue_IsCopiedAsShown()
    {
        var clipboard = new RecordingClipboard();

        Assert.Equal(
            Controls.SessionPropertiesView.CopyOutcome.Copied,
            Controls.SessionPropertiesView.TryCopy(clipboard, "ilo.example.test:443"));
        Assert.Equal(["ilo.example.test:443"], clipboard.Texts);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AnEmptyValue_TouchesNotTheClipboard(string? value)
    {
        var clipboard = new RecordingClipboard();

        Assert.Equal(
            Controls.SessionPropertiesView.CopyOutcome.NothingToCopy,
            Controls.SessionPropertiesView.TryCopy(clipboard, value));
        Assert.Empty(clipboard.Texts);
    }

    /// <summary>Another program holding the clipboard is reported, not thrown out of a click.</summary>
    [Fact]
    public void ABusyClipboard_IsReported()
    {
        var clipboard = new RecordingClipboard { Throw = true };

        Assert.Equal(
            Controls.SessionPropertiesView.CopyOutcome.ClipboardBusy,
            Controls.SessionPropertiesView.TryCopy(clipboard, "admin"));
    }

    /// <summary>
    /// Every value copies exactly what it shows: the button's Tag, which is what is copied, binds
    /// the same property as the text inside it, and the click goes to the copy handler.
    /// </summary>
    [Theory]
    [InlineData("Spv_EndpointCopy", "Endpoint")]
    [InlineData("Spv_UsernameCopy", "Username")]
    [InlineData("Spv_GatewayCopy", "GatewayDetailText")]
    [InlineData("Spv_GroupCopy", "Group")]
    [InlineData("Spv_EnvCopy", "Environment")]
    [InlineData("Spv_TagsCopy", "Tags")]
    public void EveryValue_CopiesWhatItShows(string buttonName, string property)
    {
        XDocument control = LoadXaml(Path.Combine("Controls", "SessionPropertiesView.xaml"));
        XElement button = FindNamed(control, buttonName);

        Assert.Equal("Button", button.Name.LocalName);
        Assert.Equal($"{{Binding {property}}}", (string?)button.Attribute("Tag"));
        Assert.Equal("OnCopyValueClick", (string?)button.Attribute("Click"));
        Assert.Equal("{StaticResource SpvCopyValueStyle}", (string?)button.Attribute("Style"));
        Assert.Contains(
            button.Descendants(),
            element => element.Attributes().Any(a => a.Value.StartsWith($"{{Binding {property}", StringComparison.Ordinal)));
    }

    private sealed class RecordingClipboard : IClipboardService
    {
        public List<string> Texts { get; } = [];

        public bool Throw { get; init; }

        public void SetText(string text)
        {
            if (Throw)
            {
                throw new System.Runtime.InteropServices.COMException("OpenClipboard failed", unchecked((int)0x800401D0));
            }

            Texts.Add(text);
        }
    }

    private static void AssertHostsPropertiesView(XElement host)
    {
        Assert.Contains(
            host.Descendants(),
            element => element.Name.LocalName == "SessionPropertiesView");
    }

    private static ServerProfileDto Server(string connectionType, bool hasPassword)
    {
        var dto = new ServerProfileDto
        {
            Id = connectionType,
            DisplayName = connectionType,
            ConnectionType = connectionType,
            RemoteServer = "host.example.test",
        };

        string? password = hasPassword ? "encrypted" : null;
        dto.RdpPasswordEncrypted = password;
        dto.SshPasswordEncrypted = password;
        dto.FtpPasswordEncrypted = password;
        dto.WinRmPasswordEncrypted = password;
        return dto;
    }

    private static XElement FindNamed(XDocument document, string name) =>
        Assert.Single(document.Descendants(), e => (string?)e.Attribute(XamlNamespace + "Name") == name);

    private static XDocument LoadXaml(string relativePath) =>
        XDocument.Load(Path.Combine(FindRepositoryRoot(), "src", "Heimdall.App", relativePath));

    private static string FindRepositoryRoot()
    {
        string? directory = AppContext.BaseDirectory;
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory, "Heimdall.slnx")))
            {
                return directory;
            }

            directory = Path.GetDirectoryName(directory);
        }

        throw new DirectoryNotFoundException(
            $"Cannot find repository root containing Heimdall.slnx from {AppContext.BaseDirectory}.");
    }
}
