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

using System.Windows.Input;
using Heimdall.App.Services;
using Heimdall.App.ViewModels;
using Heimdall.Core.Configuration;
using Heimdall.Core.Localization;

namespace Heimdall.App.Tests;

/// <summary>
/// The strip with forty tabs open, driven from the keyboard: the browser keys to move between
/// tabs, to move one, to jump to one, and to bring back what a bulk close took.
/// </summary>
public sealed class TabKeyboardNavigationTests
{
    [Theory]
    [InlineData(Key.PageDown, ModifierKeys.Control, "next")]
    [InlineData(Key.PageUp, ModifierKeys.Control, "previous")]
    [InlineData(Key.PageDown, ModifierKeys.Control | ModifierKeys.Shift, "move right")]
    [InlineData(Key.PageUp, ModifierKeys.Control | ModifierKeys.Shift, "move left")]
    public void PageKeys_MoveBetweenTabsAndMoveTheTab_FromATerminalToo(Key key, ModifierKeys modifiers, string expected)
    {
        Recorder recorder = new();
        KeyboardShortcutService service = recorder.Register(terminalFocused: true);

        Assert.True(service.TryHandle(key, modifiers));
        Assert.Equal([expected], recorder.Calls);
    }

    [Theory]
    [InlineData(Key.D1, 1)]
    [InlineData(Key.D5, 5)]
    [InlineData(Key.D8, 8)]
    [InlineData(Key.D9, 9)]
    public void CtrlDigit_SelectsThatOrdinal(Key key, int ordinal)
    {
        Recorder recorder = new();
        KeyboardShortcutService service = recorder.Register(terminalFocused: false);

        Assert.True(service.TryHandle(key, ModifierKeys.Control));
        Assert.Equal([$"ordinal {ordinal}"], recorder.Calls);
    }

    /// <summary>A terminal sends Ctrl+3 as Escape and Ctrl+2 as NUL: they stay the terminal's.</summary>
    [Theory]
    [InlineData(Key.D2, ModifierKeys.Control)]
    [InlineData(Key.D3, ModifierKeys.Control)]
    [InlineData(Key.R, ModifierKeys.Control | ModifierKeys.Shift)]
    public void AFocusedTerminal_KeepsTheDigitsAndTheReopenKey(Key key, ModifierKeys modifiers)
    {
        Recorder recorder = new();
        KeyboardShortcutService service = recorder.Register(terminalFocused: true);

        Assert.False(service.TryHandle(key, modifiers));
        Assert.Empty(recorder.Calls);
    }

    [Fact]
    public void CtrlShiftR_ReopensTheLastClose()
    {
        Recorder recorder = new();
        KeyboardShortcutService service = recorder.Register(terminalFocused: false);

        Assert.True(service.TryHandle(Key.R, ModifierKeys.Control | ModifierKeys.Shift));
        Assert.Equal(["reopen"], recorder.Calls);
    }

    /// <summary>
    /// The window's own key handling never sees a key typed in a WebView2 terminal: the page keys
    /// reach the strip only through the low-level hook, which leaves them to a focused RDP view.
    /// </summary>
    [Theory]
    [InlineData(Key.PageUp, ModifierKeys.Control, true)]
    [InlineData(Key.PageDown, ModifierKeys.Control, true)]
    [InlineData(Key.PageUp, ModifierKeys.Control | ModifierKeys.Shift, true)]
    [InlineData(Key.PageDown, ModifierKeys.Control | ModifierKeys.Shift, true)]
    [InlineData(Key.PageDown, ModifierKeys.None, false)]
    [InlineData(Key.PageDown, ModifierKeys.Control | ModifierKeys.Alt, false)]
    [InlineData(Key.PageDown, ModifierKeys.Shift, false)]
    [InlineData(Key.D1, ModifierKeys.Control, false)]
    [InlineData(Key.R, ModifierKeys.Control | ModifierKeys.Shift, false)]
    [InlineData(Key.F4, ModifierKeys.Control, true)]
    public void TheHook_RoutesThePageKeysOutOfATerminal(Key key, ModifierKeys modifiers, bool expected)
    {
        Assert.Equal(expected, LowLevelGlobalShortcutRouter.ShouldRouteUnlessRdpFocused(key, modifiers));
    }

    [Theory]
    [InlineData(1, "t1")]
    [InlineData(3, "t3")]
    [InlineData(9, "t5")]
    public void TabAtOrdinal_CountsTheReachableTabs_AndNineIsTheLast(int ordinal, string expected)
    {
        SessionTabViewModel[] tabs = Tabs(5);

        Assert.Equal(expected, ConnectionViewModel.TabAtOrdinal(tabs, ordinal)?.Title);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(10)]
    public void TabAtOrdinal_PastTheStrip_IsNothing(int ordinal)
    {
        Assert.Null(ConnectionViewModel.TabAtOrdinal(Tabs(5), ordinal));
    }

    /// <summary>A folded folder is skipped by Ctrl+Tab, so the numbers count what Ctrl+Tab reaches.</summary>
    [Fact]
    public void TabAtOrdinal_SkipsATabFoldedInACollapsedGroup()
    {
        SessionTabViewModel[] tabs = Tabs(3);
        tabs[0].IsTabGroupCollapsed = true;

        Assert.Equal("t2", ConnectionViewModel.TabAtOrdinal(tabs, 1)?.Title);
    }

    [Fact]
    public void MoveActiveTab_MovesItOnePlace_AndKeepsItSelected()
    {
        ConnectionViewModel sut = CreateViewModel();
        SessionTabViewModel first = Open(sut, "a");
        SessionTabViewModel second = Open(sut, "b");
        Open(sut, "c");
        sut.ActiveSession = first;

        sut.MoveActiveTab(1);

        Assert.Same(second, sut.ActiveSessions[0]);
        Assert.Same(first, sut.ActiveSessions[1]);
        Assert.Same(first, sut.ActiveSession);
    }

    [Fact]
    public void SelectTabAtOrdinal_SelectsIt()
    {
        ConnectionViewModel sut = CreateViewModel();
        Open(sut, "a");
        SessionTabViewModel second = Open(sut, "b");
        Open(sut, "c");

        sut.SelectTabAtOrdinal(2);

        Assert.Same(second, sut.ActiveSession);
    }

    /// <summary>
    /// "Close others" over twenty-five tabs left ten single entries, the last ten: the other
    /// fifteen could not be brought back at all.
    /// </summary>
    [Fact]
    public async Task ABulkClose_IsRememberedWhole_AndCountsAsOneGesture()
    {
        ConnectionViewModel sut = CreateViewModel();
        List<SessionTabViewModel> tabs = [];
        for (int index = 0; index < 25; index++)
        {
            tabs.Add(Open(sut, $"p{index}"));
        }

        await sut.CloseSessionsAsync(tabs, DisconnectReason.UserAction);

        Assert.Equal(1, sut.RecentlyClosedGroupCount);
        Assert.Equal(25, sut.MostRecentlyClosedCount);
        IReadOnlyList<string> reopened = sut.TakeMostRecentlyClosed(static _ => true);
        Assert.Equal(tabs.Select(tab => tab.ProfileLookupServerId), reopened);
        Assert.False(sut.HasRecentlyClosedTab);
    }

    [Fact]
    public async Task CloseAllTabs_IsOneGestureToo()
    {
        ConnectionViewModel sut = CreateViewModel();
        Open(sut, "a");
        Open(sut, "b");

        await sut.CloseAllSessionsCommand.ExecuteAsync(null);

        Assert.Equal(1, sut.RecentlyClosedGroupCount);
        Assert.Equal(2, sut.MostRecentlyClosedCount);
    }

    [Fact]
    public async Task SingleClosesAfterABulkOne_StayTheirOwnGestures()
    {
        ConnectionViewModel sut = CreateViewModel();
        SessionTabViewModel a = Open(sut, "a");
        SessionTabViewModel b = Open(sut, "b");
        await sut.CloseSessionsAsync([a, b], DisconnectReason.UserAction);

        await sut.CloseSessionAsync(Open(sut, "c"), DisconnectReason.TabClose, confirm: false);

        Assert.Equal(2, sut.RecentlyClosedGroupCount);
        Assert.Equal(["c"], sut.TakeMostRecentlyClosed(static _ => true));
        Assert.Equal(["a", "b"], sut.TakeMostRecentlyClosed(static _ => true));
    }

    private static SessionTabViewModel[] Tabs(int count) =>
        Enumerable.Range(1, count).Select(index => new SessionTabViewModel { Title = $"t{index}" }).ToArray();

    private static ConnectionViewModel CreateViewModel() => new(
        new LocalizationManager(),
        new ConnectionViewModelCloseTests.TrackingDialogService(true),
        new ConnectionViewModelCloseTests.TrackingSplitService { CloseAllPanesResult = true },
        new PaneCloseArbiter(),
        new SessionWindowService(static (_, _) => { }));

    private static SessionTabViewModel Open(ConnectionViewModel viewModel, string profileId)
    {
        SessionTabViewModel tab = viewModel.AddSession($"{profileId}-session", profileId, "SSH");
        tab.OriginalServerId = profileId;
        return tab;
    }

    private sealed class Recorder
    {
        public List<string> Calls { get; } = [];

        public KeyboardShortcutService Register(bool terminalFocused)
        {
            KeyboardShortcutService service = new();
            MainWindow.RegisterTabNavigationShortcuts(
                service,
                () => terminalFocused,
                step => Calls.Add(step > 0 ? "next" : "previous"),
                step => Calls.Add(step > 0 ? "move right" : "move left"),
                ordinal => Calls.Add($"ordinal {ordinal}"),
                () => Calls.Add("reopen"));
            return service;
        }
    }
}
