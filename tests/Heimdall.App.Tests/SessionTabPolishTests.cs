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
using Heimdall.Core.Configuration;
using Heimdall.Core.Localization;

namespace Heimdall.App.Tests;

/// <summary>
/// The tab audit's small points: reopening a closed tab, a drop onto another folder, closing a
/// folder's tabs.
/// </summary>
public sealed class SessionTabPolishTests
{
    [Fact]
    public async Task ClosingATabTheUserAskedToClose_RemembersItsProfile()
    {
        ConnectionViewModel sut = CreateViewModel();
        SessionTabViewModel tab = Open(sut, "profile-a", "SSH");

        await sut.CloseSessionAsync(tab, DisconnectReason.TabClose, confirm: false);

        Assert.Equal(new[] { "profile-a" }, sut.RecentlyClosedProfileIds);
        Assert.True(sut.HasRecentlyClosedTab);
    }

    [Fact]
    public async Task ASilentCloseAFailureAToolOrAnAdHocTab_AreNotRemembered()
    {
        ConnectionViewModel sut = CreateViewModel();

        await sut.CloseSessionAsync(Open(sut, "exit", "SSH"), DisconnectReason.UserAction, confirm: false, intent: CloseIntent.Silent);
        await sut.CloseSessionAsync(Open(sut, "failed", "SSH"), DisconnectReason.FailedSession, confirm: false);
        await sut.CloseSessionAsync(Open(sut, "reconnect", "SSH"), DisconnectReason.ReconnectInitiated, confirm: false);
        await sut.CloseSessionAsync(Open(sut, "tool", "TOOL:HASH"), DisconnectReason.TabClose, confirm: false);
        SessionTabViewModel adHoc = Open(sut, "adhoc", "SSH");
        adHoc.MarkAsAdHoc(new ServerProfileDto { Id = "adhoc" });
        await sut.CloseSessionAsync(adHoc, DisconnectReason.TabClose, confirm: false);

        Assert.Empty(sut.RecentlyClosedProfileIds);
        Assert.False(sut.HasRecentlyClosedTab);
    }

    [Fact]
    public async Task OnlyTheLastTenClosedTabsAreKept()
    {
        ConnectionViewModel sut = CreateViewModel();
        for (int index = 0; index < ConnectionViewModel.RecentlyClosedCapacity + 2; index++)
        {
            await sut.CloseSessionAsync(Open(sut, $"p{index}", "SSH"), DisconnectReason.TabClose, confirm: false);
        }

        Assert.Equal(ConnectionViewModel.RecentlyClosedCapacity, sut.RecentlyClosedProfileIds.Count);
        Assert.Equal("p2", sut.RecentlyClosedProfileIds[0]);
        Assert.Equal("p11", sut.RecentlyClosedProfileIds[^1]);
    }

    [Fact]
    public async Task TakeMostRecentlyClosed_ReturnsTheLatest_SkippingProfilesDeletedSince()
    {
        ConnectionViewModel sut = CreateViewModel();
        await sut.CloseSessionAsync(Open(sut, "kept", "SSH"), DisconnectReason.TabClose, confirm: false);
        await sut.CloseSessionAsync(Open(sut, "deleted", "SSH"), DisconnectReason.TabClose, confirm: false);

        string? reopened = sut.TakeMostRecentlyClosed(id => id != "deleted");

        Assert.Equal("kept", reopened);
        Assert.Empty(sut.RecentlyClosedProfileIds);
        Assert.Null(sut.TakeMostRecentlyClosed(static _ => true));
    }

    [Theory]
    [InlineData(true, "Ops", "Ops", true)]
    [InlineData(true, "Ops", "ops", true)]
    [InlineData(true, "Ops", "Lab", false)]
    [InlineData(true, null, "Lab", false)]
    [InlineData(false, "Ops", "Lab", true)]
    public void DropOntoATab_WithTabsArrangedByFolder_StaysInsideTheFolder(
        bool arranged,
        string? draggedFolder,
        string? targetFolder,
        bool reorders)
    {
        SessionTabViewModel dragged = new() { Title = "dragged", TabGroupKey = draggedFolder };
        SessionTabViewModel target = new() { Title = "target", TabGroupKey = targetFolder };

        Assert.Equal(
            reorders ? TabDropAction.Reorder : TabDropAction.None,
            TabDropDecision.Decide(true, target, dragged, displayed: null, arranged));
    }

    [Fact]
    public void SessionsInFolder_IsEveryTabOfTheFolder_InTabOrder()
    {
        SessionTabViewModel a = new() { Title = "a", TabGroupKey = "Ops" };
        SessionTabViewModel b = new() { Title = "b", TabGroupKey = "Lab" };
        SessionTabViewModel c = new() { Title = "c", TabGroupKey = "ops" };
        SessionTabViewModel d = new() { Title = "d" };

        Assert.Equal(new[] { a, c }, SessionTabContextMenuFactory.SessionsInFolder([a, b, c, d], "Ops"));
    }

    private static ConnectionViewModel CreateViewModel() => new(
        new LocalizationManager(),
        new ConnectionViewModelCloseTests.TrackingDialogService(true),
        new ConnectionViewModelCloseTests.TrackingSplitService { CloseAllPanesResult = true },
        new PaneCloseArbiter(),
        new SessionWindowService(static (_, _) => { }));

    private static SessionTabViewModel Open(ConnectionViewModel viewModel, string profileId, string connectionType)
    {
        SessionTabViewModel tab = viewModel.AddSession($"{profileId}-session", profileId, connectionType);
        tab.OriginalServerId = profileId;
        return tab;
    }
}
