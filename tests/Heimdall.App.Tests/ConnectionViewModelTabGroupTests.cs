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
using Heimdall.Core.Localization;
using Heimdall.Core.Models;

namespace Heimdall.App.Tests;

/// <summary>
/// Session tabs grouped by the folder of their profile: one strip, each folder's tabs side by
/// side behind a chip, each group collapsible.
/// </summary>
public sealed class ConnectionViewModelTabGroupTests
{
    private const string Ops = "Ops";
    private const string Lab = "Lab";

    private readonly Dictionary<string, string> _folderByProfile = new(StringComparer.Ordinal)
    {
        ["ops-1"] = Ops,
        ["ops-2"] = Ops,
        ["ops-3"] = Ops,
        ["lab-1"] = Lab,
        ["lab-2"] = Lab,
    };

    [Fact]
    public void GroupingOff_LeavesTheStripInOpeningOrderAndUngrouped()
    {
        ConnectionViewModel sut = CreateViewModel(groupByFolder: false);

        SessionTabViewModel a = Open(sut, "ops-1");
        SessionTabViewModel b = Open(sut, "lab-1");
        SessionTabViewModel c = Open(sut, "ops-2");

        Assert.Equal(new[] { a, b, c }, sut.ActiveSessions);
        Assert.All(sut.ActiveSessions, tab => Assert.False(tab.HasTabGroup));
    }

    [Fact]
    public void GroupingOn_GathersEachFolderWhereItsFirstTabStands()
    {
        ConnectionViewModel sut = CreateViewModel(groupByFolder: true);

        SessionTabViewModel ops1 = Open(sut, "ops-1");
        SessionTabViewModel loose = Open(sut, "no-folder");
        SessionTabViewModel lab1 = Open(sut, "lab-1");
        SessionTabViewModel ops2 = Open(sut, "ops-2");

        Assert.Equal(new[] { ops1, ops2, loose, lab1 }, sut.ActiveSessions);
        Assert.Equal(Ops, ops2.TabGroupKey);
        Assert.Equal("Ops label", ops2.TabGroupLabel);
        Assert.Equal("#112233", ops2.TabGroupColor);
        Assert.False(loose.HasTabGroup);
        Assert.Same(ops2, sut.ActiveSession);
    }

    [Fact]
    public void GroupingOn_OnlyTheFirstTabOfAGroupHeadsIt()
    {
        ConnectionViewModel sut = CreateViewModel(groupByFolder: true);

        SessionTabViewModel ops1 = Open(sut, "ops-1");
        SessionTabViewModel ops2 = Open(sut, "ops-2");
        SessionTabViewModel lab1 = Open(sut, "lab-1");

        Assert.True(ops1.IsTabGroupHead);
        Assert.False(ops2.IsTabGroupHead);
        Assert.True(lab1.IsTabGroupHead);
    }

    [Fact]
    public void PinnedTab_StaysOutOfItsFolderGroupAndInFront()
    {
        ConnectionViewModel sut = CreateViewModel(groupByFolder: true);
        SessionTabViewModel ops1 = Open(sut, "ops-1");
        SessionTabViewModel lab1 = Open(sut, "lab-1");
        SessionTabViewModel ops2 = Open(sut, "ops-2");

        sut.SetPinned(ops2, true);

        Assert.Equal(new[] { ops2, ops1, lab1 }, sut.ActiveSessions);
        Assert.False(ops2.HasTabGroup);
        Assert.True(ops1.IsTabGroupHead);

        sut.SetPinned(ops2, false);

        Assert.Equal(new[] { ops2, ops1, lab1 }, sut.ActiveSessions);
        Assert.Equal(Ops, ops2.TabGroupKey);
        Assert.True(ops2.IsTabGroupHead);
        Assert.False(ops1.IsTabGroupHead);
    }

    [Fact]
    public void ProfileNamedAfterTheTabJoinedTheStrip_StillLandsInItsGroup()
    {
        ConnectionViewModel sut = CreateViewModel(groupByFolder: true);
        SessionTabViewModel ops1 = Open(sut, "ops-1");
        SessionTabViewModel lab1 = Open(sut, "lab-1");
        SessionTabViewModel late = sut.AddSession("session-scoped-id", "late", "SSH");

        late.OriginalServerId = "ops-2";

        Assert.Equal(new[] { ops1, late, lab1 }, sut.ActiveSessions);
        Assert.Equal(Ops, late.TabGroupKey);
    }

    [Fact]
    public void Collapse_FoldsAwayAllButTheHead_AndMovesTheSelectionOutOfTheGroup()
    {
        ConnectionViewModel sut = CreateViewModel(groupByFolder: true);
        SessionTabViewModel ops1 = Open(sut, "ops-1");
        SessionTabViewModel ops2 = Open(sut, "ops-2");
        SessionTabViewModel lab1 = Open(sut, "lab-1");
        sut.ActiveSession = ops2;

        sut.ToggleTabGroupCollapsedCommand.Execute(ops1);

        Assert.True(ops1.IsTabGroupCollapsed);
        Assert.False(ops1.IsHiddenInCollapsedGroup);
        Assert.True(ops2.IsHiddenInCollapsedGroup);
        Assert.False(lab1.IsHiddenInCollapsedGroup);
        Assert.Same(lab1, sut.ActiveSession);
        Assert.Equal("SessionTabGroupChipCollapsedA11y", ops1.TabGroupAccessibleName);
    }

    [Fact]
    public void Collapse_WhenEveryVisibleTabIsInTheGroup_LeavesItOpen()
    {
        ConnectionViewModel sut = CreateViewModel(groupByFolder: true);
        SessionTabViewModel ops1 = Open(sut, "ops-1");
        SessionTabViewModel ops2 = Open(sut, "ops-2");

        sut.ToggleTabGroupCollapsedCommand.Execute(ops1);

        Assert.False(ops1.IsTabGroupCollapsed);
        Assert.False(ops2.IsHiddenInCollapsedGroup);
        Assert.Same(ops2, sut.ActiveSession);
        Assert.Empty(sut.CollapsedTabGroups);
    }

    [Fact]
    public void Collapse_GroupWithoutTheSelection_KeepsTheSelection()
    {
        ConnectionViewModel sut = CreateViewModel(groupByFolder: true);
        SessionTabViewModel ops1 = Open(sut, "ops-1");
        Open(sut, "ops-2");
        SessionTabViewModel lab1 = Open(sut, "lab-1");

        sut.ToggleTabGroupCollapsedCommand.Execute(ops1);

        Assert.True(ops1.IsTabGroupCollapsed);
        Assert.Same(lab1, sut.ActiveSession);
    }

    [Fact]
    public void SelectingATabOfACollapsedGroup_ExpandsTheGroup()
    {
        ConnectionViewModel sut = CreateViewModel(groupByFolder: true);
        SessionTabViewModel ops1 = Open(sut, "ops-1");
        SessionTabViewModel ops2 = Open(sut, "ops-2");
        Open(sut, "lab-1");
        sut.ToggleTabGroupCollapsedCommand.Execute(ops1);

        sut.ActiveSession = ops2;

        Assert.False(ops1.IsTabGroupCollapsed);
        Assert.False(ops2.IsHiddenInCollapsedGroup);
        Assert.Empty(sut.CollapsedTabGroups);
    }

    [Fact]
    public void ToggleTwice_ExpandsAgain()
    {
        ConnectionViewModel sut = CreateViewModel(groupByFolder: true);
        SessionTabViewModel ops1 = Open(sut, "ops-1");
        SessionTabViewModel ops2 = Open(sut, "ops-2");
        Open(sut, "lab-1");

        sut.ToggleTabGroupCollapsedCommand.Execute(ops2);
        sut.ToggleTabGroupCollapsedCommand.Execute(ops1);

        Assert.False(ops1.IsTabGroupCollapsed);
        Assert.False(ops2.IsHiddenInCollapsedGroup);
        Assert.Equal("SessionTabGroupChipExpandedA11y", ops1.TabGroupAccessibleName);
    }

    [Fact]
    public void ClosingTheHeadOfACollapsedGroup_HandsTheChipToTheNextTab()
    {
        ConnectionViewModel sut = CreateViewModel(groupByFolder: true);
        SessionTabViewModel ops1 = Open(sut, "ops-1");
        SessionTabViewModel ops2 = Open(sut, "ops-2");
        Open(sut, "lab-1");
        sut.ToggleTabGroupCollapsedCommand.Execute(ops1);

        sut.RemoveFromStrip(ops1);

        Assert.True(ops2.IsTabGroupHead);
        Assert.True(ops2.IsTabGroupCollapsed);
        Assert.False(ops2.IsHiddenInCollapsedGroup);
    }

    [Fact]
    public void TurningGroupingOff_UngroupsEveryTabAndForgetsCollapsedGroups()
    {
        ConnectionViewModel sut = CreateViewModel(groupByFolder: true);
        SessionTabViewModel ops1 = Open(sut, "ops-1");
        SessionTabViewModel ops2 = Open(sut, "ops-2");
        Open(sut, "lab-1");
        sut.ToggleTabGroupCollapsedCommand.Execute(ops1);

        sut.GroupTabsByFolder = false;

        Assert.All(sut.ActiveSessions, tab =>
        {
            Assert.False(tab.HasTabGroup);
            Assert.False(tab.IsTabGroupHead);
            Assert.False(tab.IsHiddenInCollapsedGroup);
        });
        Assert.Empty(sut.CollapsedTabGroups);
        Assert.False(ops2.IsHiddenInCollapsedGroup);
    }

    [Fact]
    public void TurningGroupingOn_RegroupsTheTabsAlreadyOpen()
    {
        ConnectionViewModel sut = CreateViewModel(groupByFolder: false);
        SessionTabViewModel ops1 = Open(sut, "ops-1");
        SessionTabViewModel lab1 = Open(sut, "lab-1");
        SessionTabViewModel ops2 = Open(sut, "ops-2");

        sut.GroupTabsByFolder = true;

        Assert.Equal(new[] { ops1, ops2, lab1 }, sut.ActiveSessions);
        Assert.Same(ops2, sut.ActiveSession);
    }

    [Fact]
    public void RefreshTabGroups_ProfileMovedToAnotherFolder_FollowsTheTree()
    {
        ConnectionViewModel sut = CreateViewModel(groupByFolder: true);
        SessionTabViewModel ops1 = Open(sut, "ops-1");
        SessionTabViewModel ops2 = Open(sut, "ops-2");
        SessionTabViewModel lab1 = Open(sut, "lab-1");

        _folderByProfile["ops-1"] = Lab;
        sut.RefreshTabGroups();

        Assert.Equal(new[] { ops1, lab1, ops2 }, sut.ActiveSessions);
        Assert.Equal(Lab, ops1.TabGroupKey);
        Assert.True(ops2.IsTabGroupHead);
    }

    [Fact]
    public void MoveSession_DraggedIntoAnotherGroup_ReturnsToItsOwn()
    {
        ConnectionViewModel sut = CreateViewModel(groupByFolder: true);
        SessionTabViewModel ops1 = Open(sut, "ops-1");
        SessionTabViewModel ops2 = Open(sut, "ops-2");
        SessionTabViewModel lab1 = Open(sut, "lab-1");
        SessionTabViewModel lab2 = Open(sut, "lab-2");

        sut.MoveSession(ops2, 2);

        Assert.Equal(new[] { ops1, ops2, lab1, lab2 }, sut.ActiveSessions);
    }

    [Fact]
    public void OrderForStrip_WithoutGroups_IsThePinnedPartition()
    {
        SessionTabViewModel a = new() { Title = "a" };
        SessionTabViewModel b = new() { Title = "b", IsPinned = true };
        SessionTabViewModel c = new() { Title = "c" };
        SessionTabViewModel d = new() { Title = "d", IsPinned = true };

        Assert.Equal(
            ConnectionViewModel.OrderByPinned([a, b, c, d]),
            ConnectionViewModel.OrderForStrip([a, b, c, d]));
    }

    [Fact]
    public void NearestTabOutsideGroup_PrefersTheTabAfterTheGroup_ThenTheOneBefore()
    {
        SessionTabViewModel before = new() { Title = "before" };
        SessionTabViewModel member = new() { Title = "member", TabGroupKey = Ops };
        SessionTabViewModel after = new() { Title = "after" };

        Assert.Same(after, ConnectionViewModel.NearestTabOutsideGroup([before, member, after], Ops));
        Assert.Same(before, ConnectionViewModel.NearestTabOutsideGroup([before, member], Ops));
        Assert.Null(ConnectionViewModel.NearestTabOutsideGroup([member], Ops));
    }

    [Fact]
    public void SelectAdjacentTab_SkipsACollapsedFolderWhole_AndLeavesItCollapsed()
    {
        ConnectionViewModel sut = CreateViewModel(groupByFolder: true);
        SessionTabViewModel loose = Open(sut, "no-folder");
        SessionTabViewModel ops1 = Open(sut, "ops-1");
        Open(sut, "ops-2");
        SessionTabViewModel lab1 = Open(sut, "lab-1");
        sut.ToggleTabGroupCollapsedCommand.Execute(ops1);
        sut.ActiveSession = loose;

        sut.SelectAdjacentTab(1);

        Assert.Same(lab1, sut.ActiveSession);
        Assert.True(ops1.IsTabGroupCollapsed);

        sut.SelectAdjacentTab(1);
        Assert.Same(loose, sut.ActiveSession);

        sut.SelectAdjacentTab(-1);
        Assert.Same(lab1, sut.ActiveSession);
        Assert.True(ops1.IsTabGroupCollapsed);
    }

    [Fact]
    public void SelectAdjacentTab_ExpandedFolder_IsWalkedTabByTab()
    {
        ConnectionViewModel sut = CreateViewModel(groupByFolder: true);
        SessionTabViewModel ops1 = Open(sut, "ops-1");
        SessionTabViewModel ops2 = Open(sut, "ops-2");
        SessionTabViewModel lab1 = Open(sut, "lab-1");
        sut.ActiveSession = ops1;

        sut.SelectAdjacentTab(1);
        Assert.Same(ops2, sut.ActiveSession);

        sut.SelectAdjacentTab(1);
        Assert.Same(lab1, sut.ActiveSession);
    }

    [Fact]
    public void NextVisibleTab_NoOtherReachableTab_ReturnsNull()
    {
        SessionTabViewModel only = new() { Title = "only" };
        SessionTabViewModel folded = new() { Title = "folded", IsTabGroupCollapsed = true };

        Assert.Null(ConnectionViewModel.NextVisibleTab([only, folded], only, 1));
        Assert.Null(ConnectionViewModel.NextVisibleTab([only], only, -1));
        Assert.Null(ConnectionViewModel.NextVisibleTab([only], new SessionTabViewModel(), 1));
    }

    [Fact]
    public void CollapseOtherTabGroups_KeepsThisFolderOpen_FoldsTheOthers_AndMovesTheSelectionIn()
    {
        ConnectionViewModel sut = CreateViewModel(groupByFolder: true);
        SessionTabViewModel ops1 = Open(sut, "ops-1");
        SessionTabViewModel ops2 = Open(sut, "ops-2");
        SessionTabViewModel lab1 = Open(sut, "lab-1");
        SessionTabViewModel lab2 = Open(sut, "lab-2");
        SessionTabViewModel loose = Open(sut, "no-folder");
        sut.ActiveSession = lab2;

        sut.CollapseOtherTabGroupsCommand.Execute(ops2);

        Assert.False(ops1.IsTabGroupCollapsed);
        Assert.False(ops2.IsHiddenInCollapsedGroup);
        Assert.True(lab1.IsTabGroupCollapsed);
        Assert.True(lab2.IsHiddenInCollapsedGroup);
        Assert.False(loose.IsHiddenInCollapsedGroup);
        Assert.Same(ops2, sut.ActiveSession);
    }

    [Fact]
    public void CollapseOtherTabGroups_SelectionAlreadyInTheFolder_StaysPut()
    {
        ConnectionViewModel sut = CreateViewModel(groupByFolder: true);
        SessionTabViewModel ops1 = Open(sut, "ops-1");
        SessionTabViewModel ops2 = Open(sut, "ops-2");
        SessionTabViewModel lab1 = Open(sut, "lab-1");
        sut.ActiveSession = ops1;

        sut.CollapseOtherTabGroupsCommand.Execute(ops2);

        Assert.Same(ops1, sut.ActiveSession);
        Assert.True(lab1.IsTabGroupCollapsed);
    }

    [Fact]
    public void CollapseOtherTabGroups_ReopensThisFolderWhenItWasCollapsed()
    {
        ConnectionViewModel sut = CreateViewModel(groupByFolder: true);
        SessionTabViewModel ops1 = Open(sut, "ops-1");
        Open(sut, "ops-2");
        SessionTabViewModel lab1 = Open(sut, "lab-1");
        sut.ToggleTabGroupCollapsedCommand.Execute(ops1);
        Assert.True(ops1.IsTabGroupCollapsed);

        sut.CollapseOtherTabGroupsCommand.Execute(ops1);

        Assert.False(ops1.IsTabGroupCollapsed);
        Assert.True(lab1.IsTabGroupCollapsed);
        Assert.Same(ops1, sut.ActiveSession);
    }

    [Fact]
    public void HasOtherTabGroup_OnlyWhenAnotherFolderIsInTheStrip()
    {
        ConnectionViewModel sut = CreateViewModel(groupByFolder: true);
        SessionTabViewModel ops1 = Open(sut, "ops-1");
        SessionTabViewModel loose = Open(sut, "no-folder");

        Assert.False(sut.HasOtherTabGroup(ops1));
        Assert.False(sut.HasOtherTabGroup(loose));

        Open(sut, "lab-1");
        Assert.True(sut.HasOtherTabGroup(ops1));
        Assert.False(sut.HasOtherTabGroup(loose));
    }

    private ConnectionViewModel CreateViewModel(bool groupByFolder)
    {
        ConnectionViewModel viewModel = new(
            new LocalizationManager(),
            new ConnectionViewModelCloseTests.TrackingDialogService(true),
            new ConnectionViewModelCloseTests.TrackingSplitService(),
            new PaneCloseArbiter(),
            new SessionWindowService(static (_, _) => { }))
        {
            GroupTabsByFolder = groupByFolder,
        };

        viewModel.TabGroupResolver = profileId => _folderByProfile.TryGetValue(profileId, out string? folder)
            ? new SessionTabGroupInfo(folder, folder + " label", folder == Ops ? "#112233" : "")
            : null;
        return viewModel;
    }

    private static SessionTabViewModel Open(ConnectionViewModel viewModel, string profileId)
        => viewModel.AddSession(profileId, profileId, "SSH");
}
