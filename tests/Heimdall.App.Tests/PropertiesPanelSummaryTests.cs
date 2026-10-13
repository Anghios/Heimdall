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
using Heimdall.App.ViewModels;
using Heimdall.Core.Codecs;
using Heimdall.Core.Configuration;
using Microsoft.Extensions.Time.Testing;

namespace Heimdall.App.Tests;

/// <summary>
/// The summary the properties panel shows for a folder or a multi-selection, as built.
/// </summary>
public sealed class PropertiesSummaryBuildTests
{
    [Fact]
    public void TheSummary_CountsSessionsConnectionsAndProtocols()
    {
        PropertiesSummaryViewModel summary = PropertiesSummaryViewModel.Build("ops", "ops", [
            Row("a", "SSH", "Connected"),
            Row("b", "SSH", "Disconnected"),
            Row("c", "RDP", "Connected"),
            Row("d", "SSH", "Error"),
        ]);

        Assert.Equal(4, summary.SessionCount);
        Assert.Equal(2, summary.ConnectedCount);
        Assert.Equal(
            [new ProtocolCountRow("SSH", "3"), new ProtocolCountRow("RDP", "1")],
            summary.Protocols);
    }

    /// <summary>Equal counts keep one order, so the lines do not swap on every rebuild.</summary>
    [Fact]
    public void ProtocolsWithTheSameCount_AreOrderedByName()
    {
        PropertiesSummaryViewModel summary = PropertiesSummaryViewModel.Build("ops", "", [
            Row("a", "VNC", "Disconnected"),
            Row("b", "RDP", "Disconnected"),
        ]);

        Assert.Equal(["RDP", "VNC"], summary.Protocols.Select(row => row.Protocol));
    }

    [Fact]
    public void ARowListedTwice_CountsOnce()
    {
        ServerItemViewModel row = Row("a", "SSH", "Connected");

        PropertiesSummaryViewModel summary = PropertiesSummaryViewModel.Build("ops", "", [row, row]);

        Assert.Equal(1, summary.SessionCount);
        Assert.Equal(1, summary.ConnectedCount);
    }

    private static ServerItemViewModel Row(string id, string connectionType, string state) =>
        ServerItemViewModel.FromDto(
            new ServerProfileDto
            {
                Id = id,
                DisplayName = id,
                ConnectionType = connectionType,
                RemoteServer = "host.example.test",
            },
            connectionState: state);
}

/// <summary>
/// What the properties panel shows, seen through the list: a session, a summary, or its hint.
/// </summary>
public sealed partial class ServerListSelectionTests
{
    [Fact]
    public async Task AFolderUnderTheCursor_IsSummarizedWithItsSubfolders()
    {
        await using ServerListSelectionFixture fixture = await ServerListSelectionFixture.CreateAsync();
        fixture.LoadServers(
            new AppSettings(),
            CreateServer("alpha", "Alpha", "ops"),
            CreateServer("beta", "Beta", "ops/db"),
            CreateServer("gamma", "Gamma", "lab"));
        fixture.ViewModel.SelectSingle(fixture.ServerById("gamma"));

        fixture.ViewModel.PropertiesFolder = fixture.FolderByPath("ops");

        PropertiesSummaryViewModel summary = Assert.IsType<PropertiesSummaryViewModel>(fixture.ViewModel.PropertiesSummary);
        Assert.Equal("ops", summary.Title);
        Assert.Equal("ops", summary.Path);
        Assert.Equal(2, summary.SessionCount);
        AssertPanelShows(fixture.ViewModel, PanelMode.Summary);
    }

    /// <summary>
    /// The cursor can stay on a folder while a session is picked from the palette or a search;
    /// the panel follows the pick, not the mark left behind.
    /// </summary>
    [Fact]
    public async Task ChoosingASession_TakesThePanelBackFromTheFolder()
    {
        await using ServerListSelectionFixture fixture = await ServerListSelectionFixture.CreateAsync();
        fixture.LoadServers(
            new AppSettings(),
            CreateServer("alpha", "Alpha", "ops"),
            CreateServer("gamma", "Gamma", "lab"));
        fixture.ViewModel.PropertiesFolder = fixture.FolderByPath("ops");

        fixture.ViewModel.SelectSingle(fixture.ServerById("gamma"));

        Assert.Null(fixture.ViewModel.PropertiesFolder);
        Assert.Null(fixture.ViewModel.PropertiesSummary);
        AssertPanelShows(fixture.ViewModel, PanelMode.Session);
    }

    [Fact]
    public async Task SeveralSelectedSessions_AreSummarized()
    {
        await using ServerListSelectionFixture fixture = await ServerListSelectionFixture.CreateAsync();
        fixture.LoadServers(
            fixture.ExpandGroups("ops"),
            CreateServer("alpha", "Alpha", "ops"),
            CreateServer("beta", "Beta", "ops"),
            CreateServer("gamma", "Gamma", "ops"));
        fixture.ViewModel.SelectSingle(fixture.ServerById("alpha"));
        AssertPanelShows(fixture.ViewModel, PanelMode.Session);

        fixture.ViewModel.SelectAllVisible();

        PropertiesSummaryViewModel summary = Assert.IsType<PropertiesSummaryViewModel>(fixture.ViewModel.PropertiesSummary);
        Assert.Equal(fixture.ViewModel.SelectionCountText, summary.Title);
        Assert.Equal("", summary.Path);
        Assert.Equal(3, summary.SessionCount);
        AssertPanelShows(fixture.ViewModel, PanelMode.Summary);

        fixture.ViewModel.ClearSelection();
        Assert.Null(fixture.ViewModel.PropertiesSummary);
        AssertPanelShows(fixture.ViewModel, PanelMode.Empty);
    }

    [Fact]
    public async Task ASessionThatConnects_MovesTheConnectedCount()
    {
        await using ServerListSelectionFixture fixture = await ServerListSelectionFixture.CreateAsync();
        fixture.LoadServers(
            fixture.ExpandGroups("ops"),
            CreateServer("alpha", "Alpha", "ops"),
            CreateServer("beta", "Beta", "ops"));
        fixture.ViewModel.PropertiesFolder = fixture.FolderByPath("ops");
        Assert.Equal(0, fixture.ViewModel.PropertiesSummary!.ConnectedCount);
        List<string?> changed = [];
        fixture.ViewModel.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        TransitionSshSessionToConnected(fixture.StateMachine, SessionIdCodec.Create("alpha"));

        Assert.Equal(1, fixture.ViewModel.PropertiesSummary!.ConnectedCount);
        Assert.Contains(nameof(ServerListViewModel.PropertiesSummary), changed);
    }

    /// <summary>
    /// A search that hides the selected session moves the selection by itself; the user did not
    /// leave the folder.
    /// </summary>
    [Fact]
    public async Task ASearchHidingTheSelection_KeepsTheFolder()
    {
        FakeTimeProvider timeProvider = new();
        await using ServerListSelectionFixture fixture =
            await ServerListSelectionFixture.CreateAsync(timeProvider: timeProvider);
        fixture.LoadServers(
            fixture.ExpandGroups("ops", "lab"),
            CreateServer("alpha", "Alpha", "ops"),
            CreateServer("gamma", "Gamma", "lab"));
        fixture.ViewModel.SelectSingle(fixture.ServerById("gamma"));
        FolderViewModel ops = fixture.FolderByPath("ops");
        fixture.ViewModel.PropertiesFolder = ops;

        fixture.ViewModel.SearchText = "Alpha";
        timeProvider.Advance(ServerListViewModel.SearchFilterDebounceDelay);

        Assert.Null(fixture.ViewModel.SelectedServer);
        Assert.Same(ops, fixture.ViewModel.PropertiesFolder);
        AssertPanelShows(fixture.ViewModel, PanelMode.Summary);
    }

    /// <summary>A search that hides the folder leaves no summary of rows that are gone.</summary>
    [Fact]
    public async Task AFolderTheFilterHides_LeavesThePanel()
    {
        FakeTimeProvider timeProvider = new();
        await using ServerListSelectionFixture fixture =
            await ServerListSelectionFixture.CreateAsync(timeProvider: timeProvider);
        fixture.LoadServers(
            new AppSettings(),
            CreateServer("alpha", "Alpha", "ops"),
            CreateServer("gamma", "Gamma", "lab"));
        fixture.ViewModel.PropertiesFolder = fixture.FolderByPath("ops");

        fixture.ViewModel.SearchText = "Gamma";
        timeProvider.Advance(ServerListViewModel.SearchFilterDebounceDelay);

        Assert.Null(fixture.ViewModel.PropertiesFolder);
        Assert.Null(fixture.ViewModel.PropertiesSummary);
    }

    /// <summary>
    /// The panel's three blocks bind the three modes, not the detail pane's flag: bound to that
    /// one, the session block would show under the summary.
    /// </summary>
    [Theory]
    [InlineData("Mw_PropertiesPanelSession", "{Binding ServerList.PropertiesPanelShowsSession, Converter={StaticResource BoolToVisibilityConverter}}")]
    [InlineData("Mw_PropertiesPanelEmpty", "{Binding ServerList.PropertiesPanelShowsEmpty, Converter={StaticResource BoolToVisibilityConverter}}")]
    [InlineData("Mw_PropertiesPanelSummary", "{Binding DataContext.ServerList.PropertiesPanelShowsSummary, RelativeSource={RelativeSource AncestorType=Window}, Converter={StaticResource BoolToVisibilityConverter}}")]
    public void EachPanelBlock_FollowsItsMode(string name, string visibility)
    {
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        string root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "Heimdall.slnx")))
        {
            root = Path.GetDirectoryName(root) ?? throw new DirectoryNotFoundException("Heimdall.slnx");
        }

        XDocument window = XDocument.Load(Path.Combine(root, "src", "Heimdall.App", "MainWindow.xaml"));
        XElement block = Assert.Single(window.Descendants(), e => (string?)e.Attribute(xaml + "Name") == name);

        Assert.Equal(visibility, (string?)block.Attribute("Visibility"));
    }

    private enum PanelMode
    {
        Session,
        Summary,
        Empty,
    }

    /// <summary>Exactly one of the three blocks is on screen.</summary>
    private static void AssertPanelShows(ServerListViewModel viewModel, PanelMode mode)
    {
        Assert.Equal(
            (mode == PanelMode.Session, mode == PanelMode.Summary, mode == PanelMode.Empty),
            (viewModel.PropertiesPanelShowsSession, viewModel.PropertiesPanelShowsSummary, viewModel.PropertiesPanelShowsEmpty));
    }
}
