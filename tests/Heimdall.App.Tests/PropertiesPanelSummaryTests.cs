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
        List<string?> changed = [];
        fixture.ViewModel.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        fixture.ViewModel.SelectSingle(fixture.ServerById("gamma"));

        Assert.Null(fixture.ViewModel.PropertiesFolder);
        Assert.Contains(nameof(ServerListViewModel.PropertiesFolder), changed);
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

    /// <summary>
    /// A search that hides the folder leaves no summary of rows that are gone, and the summary
    /// comes back with the folder, where the cursor mark still is.
    /// </summary>
    [Fact]
    public async Task AFolderTheFilterHides_LeavesThePanelAndComesBackWithIt()
    {
        FakeTimeProvider timeProvider = new();
        await using ServerListSelectionFixture fixture =
            await ServerListSelectionFixture.CreateAsync(timeProvider: timeProvider);
        fixture.LoadServers(
            new AppSettings(),
            CreateServer("alpha", "Alpha", "ops"),
            CreateServer("gamma", "Gamma", "lab"));
        fixture.ViewModel.PropertiesFolder = fixture.FolderByPath("ops");
        List<string?> changed = [];
        fixture.ViewModel.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        fixture.ViewModel.SearchText = "Gamma";
        timeProvider.Advance(ServerListViewModel.SearchFilterDebounceDelay);

        Assert.Null(fixture.ViewModel.PropertiesFolder);
        Assert.Null(fixture.ViewModel.PropertiesSummary);
        Assert.Contains(nameof(ServerListViewModel.PropertiesFolder), changed);

        fixture.ViewModel.SearchText = "";
        timeProvider.Advance(ServerListViewModel.SearchFilterDebounceDelay);

        Assert.Equal("ops", fixture.ViewModel.PropertiesFolder?.FullPath);
        Assert.Equal(1, fixture.ViewModel.PropertiesSummary?.SessionCount);
    }

    /// <summary>A reload builds new folder rows; the summary follows the folder, not the old row.</summary>
    [Fact]
    public async Task AReload_KeepsTheFolderSummary()
    {
        await using ServerListSelectionFixture fixture = await ServerListSelectionFixture.CreateAsync();
        AppSettings settings = new();
        fixture.LoadServers(settings, CreateServer("alpha", "Alpha", "ops"));
        FolderViewModel before = fixture.FolderByPath("ops");
        fixture.ViewModel.PropertiesFolder = before;

        fixture.LoadServers(settings, CreateServer("alpha", "Alpha", "ops"), CreateServer("beta", "Beta", "ops"));

        FolderViewModel after = fixture.FolderByPath("ops");
        Assert.NotSame(before, after);
        Assert.Same(after, fixture.ViewModel.PropertiesFolder);
        Assert.Equal(2, fixture.ViewModel.PropertiesSummary?.SessionCount);
    }

    [Fact]
    public async Task ASessionThatCloses_LowersTheConnectedCount()
    {
        await using ServerListSelectionFixture fixture = await ServerListSelectionFixture.CreateAsync();
        fixture.LoadServers(fixture.ExpandGroups("ops"), CreateServer("alpha", "Alpha", "ops"));
        string sessionId = SessionIdCodec.Create("alpha");
        TransitionSshSessionToConnected(fixture.StateMachine, sessionId);
        fixture.ViewModel.PropertiesFolder = fixture.FolderByPath("ops");
        Assert.Equal(1, fixture.ViewModel.PropertiesSummary!.ConnectedCount);

        fixture.StateMachine.Reset(sessionId);

        Assert.Equal(0, fixture.ViewModel.PropertiesSummary!.ConnectedCount);
    }

    /// <summary>
    /// The panel reads each flag when it is announced. Announced before the selection settled,
    /// a flag would leave the panel on the session block after the selection was cleared.
    /// </summary>
    [Fact]
    public async Task TheModeFlags_AreAnnouncedWithTheirFinalValue()
    {
        await using ServerListSelectionFixture fixture = await ServerListSelectionFixture.CreateAsync();
        fixture.LoadServers(fixture.ExpandGroups("ops"), CreateServer("alpha", "Alpha", "ops"));
        fixture.ViewModel.SelectSingle(fixture.ServerById("alpha"));
        Dictionary<string, bool> announced = [];
        fixture.ViewModel.PropertyChanged += (_, e) =>
        {
            switch (e.PropertyName)
            {
                case nameof(ServerListViewModel.PropertiesPanelShowsSession):
                    announced[e.PropertyName] = fixture.ViewModel.PropertiesPanelShowsSession;
                    break;
                case nameof(ServerListViewModel.PropertiesPanelShowsEmpty):
                    announced[e.PropertyName] = fixture.ViewModel.PropertiesPanelShowsEmpty;
                    break;
            }
        };

        fixture.ViewModel.ClearSelection();

        Assert.False(announced[nameof(ServerListViewModel.PropertiesPanelShowsSession)]);
        Assert.True(announced[nameof(ServerListViewModel.PropertiesPanelShowsEmpty)]);
    }

    /// <summary>
    /// Focus comes back to the row with the cursor mark after a dialog or a switch to another
    /// window. That is not the user choosing the folder again, so a selection made since keeps
    /// the panel.
    /// </summary>
    [Fact]
    public async Task FocusComingBackToTheMarkedFolder_DoesNotTakeThePanel()
    {
        await using ServerListSelectionFixture fixture = await ServerListSelectionFixture.CreateAsync();
        fixture.LoadServers(
            fixture.ExpandGroups("ops"),
            CreateServer("alpha", "Alpha", "ops"),
            CreateServer("beta", "Beta", "ops"));
        Heimdall.App.Services.TreeInteractionState state = new();
        FolderViewModel ops = fixture.FolderByPath("ops");

        MainWindow.MoveFolderCursorAndPanel(state, fixture.ViewModel, ops);
        Assert.Same(ops, fixture.ViewModel.PropertiesFolder);

        fixture.ViewModel.SelectAllVisible();
        MainWindow.MoveFolderCursorAndPanel(state, fixture.ViewModel, ops);

        Assert.Null(fixture.ViewModel.PropertiesFolder);
        Assert.Equal(2, fixture.ViewModel.PropertiesSummary?.SessionCount);
        Assert.Equal(fixture.ViewModel.SelectionCountText, fixture.ViewModel.PropertiesSummary?.Title);

        MainWindow.MoveFolderCursorAndPanel(state, fixture.ViewModel, fixture.ServerById("alpha"));
        Assert.Null(state.CursorFolder);
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

    /// <summary>The summary block reads the summary, not the window's view model.</summary>
    [Fact]
    public void TheSummaryBlock_ReadsTheSummary()
    {
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        string root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "Heimdall.slnx")))
        {
            root = Path.GetDirectoryName(root) ?? throw new DirectoryNotFoundException("Heimdall.slnx");
        }

        XDocument window = XDocument.Load(Path.Combine(root, "src", "Heimdall.App", "MainWindow.xaml"));
        XElement block = Assert.Single(window.Descendants(), e => (string?)e.Attribute(xaml + "Name") == "Mw_PropertiesPanelSummary");

        Assert.Equal("{Binding ServerList.PropertiesSummary}", (string?)block.Attribute("DataContext"));
        Assert.Contains(
            block.Descendants(),
            e => (string?)e.Attribute("ItemsSource") == "{Binding Protocols}");
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
