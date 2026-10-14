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

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Heimdall.App.ViewModels;

/// <summary>
/// Folder groups in the session tab strip: one strip, the tabs of a folder kept side by side
/// behind a chip carrying the folder's name and colour, each group collapsible like a browser's.
/// </summary>
/// <remarks>
/// <para>A group is derived, never stored: it is the folder the tab's profile lives in, read
/// from the session tree every time. A profile moved to another folder, a folder renamed or
/// recoloured, and a workspace restored on the next launch therefore land in the right group
/// with nothing to migrate or persist.</para>
/// <para>Pinned tabs stay out of every group, as in a browser: they keep the front of the strip
/// and a group may not reach into it.</para>
/// <para>Collapsing is a view state for the lifetime of the window, like pinning.</para>
/// </remarks>
public partial class ConnectionViewModel
{
    private const string TabGroupChipExpandedKey = "SessionTabGroupChipExpandedA11y";
    private const string TabGroupChipCollapsedKey = "SessionTabGroupChipCollapsedA11y";
    private const string TabGroupChipLoneKey = "SessionTabGroupChipLoneA11y";

    private readonly HashSet<string> _collapsedTabGroups = new(StringComparer.OrdinalIgnoreCase);

    private Func<string, SessionTabGroupInfo?>? _tabGroupResolver;

    /// <summary>
    /// True when the session tabs are grouped by the folder of their profile.
    /// </summary>
    [ObservableProperty]
    private bool _groupTabsByFolder;

    /// <summary>
    /// Answers the folder group of a profile id, or null when the profile is in no folder or is
    /// not in the inventory. Supplied by the shell, which owns the session tree.
    /// </summary>
    internal Func<string, SessionTabGroupInfo?>? TabGroupResolver
    {
        get => _tabGroupResolver;
        set
        {
            _tabGroupResolver = value;
            RefreshTabGroups();
        }
    }

    /// <summary>The folder groups currently collapsed. Exposed for tests.</summary>
    internal IReadOnlyCollection<string> CollapsedTabGroups => _collapsedTabGroups;

    partial void OnGroupTabsByFolderChanged(bool value) => RefreshTabGroups();

    // A tab that becomes the selected one is never left folded away: selecting it, from the
    // keyboard, the tree or a new connection, opens its group.
    partial void OnActiveSessionChanged(SessionTabViewModel? value)
    {
        if (value?.TabGroupKey is { } key && _collapsedTabGroups.Remove(key))
        {
            RefreshTabGroupFlags();
        }
    }

    /// <summary>
    /// Re-reads every tab's folder group and puts the strip back in group order. Called when the
    /// session tree changes underneath the strip (a profile moved, a folder renamed or recoloured).
    /// </summary>
    public void RefreshTabGroups()
    {
        foreach (SessionTabViewModel session in ActiveSessions)
        {
            AssignTabGroup(session);
        }

        ApplyPinnedOrdering();
    }

    /// <summary>
    /// Collapses the folder group of <paramref name="session"/>, or expands it when it is
    /// collapsed. Collapsing the group that holds the selected tab first selects the nearest tab
    /// outside it; when there is none, the group stays open, because a strip with nothing to
    /// select would leave the content area showing a tab the strip hides.
    /// </summary>
    [RelayCommand]
    private void ToggleTabGroupCollapsed(SessionTabViewModel? session)
    {
        if (session?.TabGroupKey is not { } key || !ActiveSessions.Contains(session))
        {
            return;
        }

        if (_collapsedTabGroups.Remove(key))
        {
            RefreshTabGroupFlags();
            return;
        }

        if (ActiveSession is { } selected && IsInTabGroup(selected, key))
        {
            SessionTabViewModel? fallback = NearestTabOutsideGroup(ActiveSessions, key);
            if (fallback is null)
            {
                return;
            }

            ActiveSession = fallback;
        }

        _collapsedTabGroups.Add(key);
        RefreshTabGroupFlags();
    }

    /// <summary>
    /// Collapses every folder group except the one of <paramref name="session"/>, which is
    /// expanded, so the strip shows that folder's tabs and the tabs outside any folder: switching
    /// from one set of machines to another in one gesture, as mRemoteNG's per-folder panels do.
    /// The selection moves into the folder first when it was elsewhere, so no collapsed folder
    /// is left holding the open tab.
    /// </summary>
    [RelayCommand]
    private void CollapseOtherTabGroups(SessionTabViewModel? session)
    {
        if (session?.TabGroupKey is not { } key || !ActiveSessions.Contains(session))
        {
            return;
        }

        // Selecting a tab of a collapsed folder unfolds it, and a folder holding the open tab is
        // never collapsed, so once the selection is inside this folder it is open.
        if (ActiveSession is not { } selected || !IsInTabGroup(selected, key))
        {
            ActiveSession = session;
        }

        foreach (SessionTabViewModel tab in ActiveSessions)
        {
            if (tab.TabGroupKey is { } other && !IsInTabGroup(tab, key))
            {
                _collapsedTabGroups.Add(other);
            }
        }

        RefreshTabGroupFlags();
    }

    /// <summary>True when the strip holds a folder group other than the one of <paramref name="session"/>.</summary>
    internal bool HasOtherTabGroup(SessionTabViewModel session)
    {
        ArgumentNullException.ThrowIfNull(session);

        return session.TabGroupKey is { } key
            && ActiveSessions.Any(tab => tab.TabGroupKey is not null && !IsInTabGroup(tab, key));
    }

    /// <summary>
    /// Selects the next tab (<paramref name="step"/> = 1) or the previous one (-1), wrapping round
    /// the strip. A collapsed folder is skipped whole, its chip included, as a browser skips a
    /// collapsed tab group: landing on any of its tabs would unfold it and undo the collapse.
    /// </summary>
    public void SelectAdjacentTab(int step)
    {
        if (ActiveSession is not { } current
            || NextVisibleTab(ActiveSessions, current, step) is not { } next)
        {
            return;
        }

        ActiveSession = next;
    }

    /// <summary>
    /// The tab <paramref name="step"/> places away from <paramref name="current"/>, wrapping round
    /// and skipping every tab of a collapsed folder. Null when there is no other tab to reach.
    /// </summary>
    internal static SessionTabViewModel? NextVisibleTab(
        IReadOnlyList<SessionTabViewModel> ordered,
        SessionTabViewModel current,
        int step)
    {
        int start = -1;
        for (int index = 0; index < ordered.Count; index++)
        {
            if (ReferenceEquals(ordered[index], current))
            {
                start = index;
                break;
            }
        }

        if (start < 0 || step == 0)
        {
            return null;
        }

        int direction = Math.Sign(step);
        for (int offset = 1; offset < ordered.Count; offset++)
        {
            int index = ((start + (direction * offset)) % ordered.Count + ordered.Count) % ordered.Count;
            if (!ordered[index].IsTabGroupCollapsed)
            {
                return ordered[index];
            }
        }

        return null;
    }

    /// <summary>The ordinal that means the last tab rather than the ninth, as in a browser.</summary>
    internal const int LastTabOrdinal = 9;

    /// <summary>
    /// Selects the tab at <paramref name="ordinal"/> (1-based) among those Ctrl+Tab can reach;
    /// <see cref="LastTabOrdinal"/> selects the last of them. With forty tabs open, reaching the
    /// thirty-fifth took thirty-four presses of Ctrl+Tab.
    /// </summary>
    public void SelectTabAtOrdinal(int ordinal)
    {
        if (TabAtOrdinal(ActiveSessions, ordinal) is { } target)
        {
            ActiveSession = target;
        }
    }

    /// <summary>
    /// The tab at <paramref name="ordinal"/> among those not folded in a collapsed group; the last
    /// of them for <see cref="LastTabOrdinal"/>. Null when the strip holds fewer.
    /// </summary>
    internal static SessionTabViewModel? TabAtOrdinal(IReadOnlyList<SessionTabViewModel> ordered, int ordinal)
    {
        List<SessionTabViewModel> reachable = ordered.Where(tab => !tab.IsTabGroupCollapsed).ToList();
        if (reachable.Count == 0 || ordinal < 1 || ordinal > LastTabOrdinal)
        {
            return null;
        }

        if (ordinal == LastTabOrdinal)
        {
            return reachable[^1];
        }

        return ordinal <= reachable.Count ? reachable[ordinal - 1] : null;
    }

    /// <summary>
    /// Moves the active tab one place left (<paramref name="step"/> = -1) or right (1). Kept inside
    /// its pinned block and its folder by <see cref="MoveSession"/>, as a drag is.
    /// </summary>
    public void MoveActiveTab(int step)
    {
        if (ActiveSession is not { } current || step == 0)
        {
            return;
        }

        int index = ActiveSessions.IndexOf(current);
        if (index < 0)
        {
            return;
        }

        MoveSession(current, index + Math.Sign(step));
        ActiveSession = current;
    }

    /// <summary>
    /// Stable order for the strip: pinned tabs first, then the rest with the tabs of each folder
    /// group gathered where the group's first tab stands. Ungrouped tabs keep their place
    /// relative to the groups. With no group assigned this is exactly the pinned partition.
    /// </summary>
    internal static IReadOnlyList<SessionTabViewModel> OrderForStrip(
        IReadOnlyList<SessionTabViewModel> sessions)
    {
        var pinned = new List<SessionTabViewModel>();
        var blocks = new List<List<SessionTabViewModel>>();
        var blocksByKey = new Dictionary<string, List<SessionTabViewModel>>(StringComparer.OrdinalIgnoreCase);

        foreach (SessionTabViewModel session in sessions)
        {
            if (session.IsPinned)
            {
                pinned.Add(session);
                continue;
            }

            if (session.TabGroupKey is not { } key)
            {
                blocks.Add([session]);
                continue;
            }

            if (!blocksByKey.TryGetValue(key, out List<SessionTabViewModel>? block))
            {
                block = [];
                blocksByKey[key] = block;
                blocks.Add(block);
            }

            block.Add(session);
        }

        foreach (List<SessionTabViewModel> block in blocks)
        {
            pinned.AddRange(block);
        }

        return pinned;
    }

    /// <summary>
    /// The tab to select when the group <paramref name="key"/> folds away: the first visible tab
    /// after the group, else the last visible one before it. Null when every visible tab is in
    /// the group.
    /// </summary>
    internal static SessionTabViewModel? NearestTabOutsideGroup(
        IReadOnlyList<SessionTabViewModel> ordered,
        string key)
    {
        int last = -1;
        int first = -1;
        for (int index = 0; index < ordered.Count; index++)
        {
            if (IsInTabGroup(ordered[index], key))
            {
                first = first < 0 ? index : first;
                last = index;
            }
        }

        if (first < 0)
        {
            return null;
        }

        for (int index = last + 1; index < ordered.Count; index++)
        {
            if (!IsInTabGroup(ordered[index], key) && !ordered[index].IsHiddenInCollapsedGroup)
            {
                return ordered[index];
            }
        }

        for (int index = first - 1; index >= 0; index--)
        {
            if (!IsInTabGroup(ordered[index], key) && !ordered[index].IsHiddenInCollapsedGroup)
            {
                return ordered[index];
            }
        }

        return null;
    }

    private static bool IsInTabGroup(SessionTabViewModel session, string key)
        => string.Equals(session.TabGroupKey, key, StringComparison.OrdinalIgnoreCase);

    private void AssignTabGroup(SessionTabViewModel session)
    {
        string lookupId = session.ProfileLookupServerId;
        SessionTabGroupInfo? group = GroupTabsByFolder
            && !session.IsPinned
            && _tabGroupResolver is not null
            && !string.IsNullOrWhiteSpace(lookupId)
                ? _tabGroupResolver(lookupId)
                : null;

        session.TabGroupKey = group?.Key;
        session.TabGroupLabel = group?.Label ?? string.Empty;
        session.TabGroupColor = group?.Color ?? string.Empty;
    }

    /// <summary>
    /// Recomputes which tab heads each group, which are folded away, and what the chip announces.
    /// Touches no collection, so it is safe from inside a collection-change notification.
    /// </summary>
    private void RefreshTabGroupFlags()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (SessionTabViewModel session in ActiveSessions)
        {
            if (session.TabGroupKey is not { } key)
            {
                session.IsTabGroupHead = false;
                session.IsTabGroupCollapsed = false;
                session.IsHiddenInCollapsedGroup = false;
                session.TabGroupAccessibleName = string.Empty;
                continue;
            }

            bool head = seen.Add(key);
            bool collapsed = _collapsedTabGroups.Contains(key);
            session.IsTabGroupHead = head;
            session.IsTabGroupCollapsed = collapsed;
            session.IsHiddenInCollapsedGroup = collapsed && !head;
            // The same test the toggle makes: with no tab left to select outside the group, a
            // click on the chip leaves it open, and the chip used to give no reason why.
            string chipKey = collapsed
                ? TabGroupChipCollapsedKey
                : NearestTabOutsideGroup(ActiveSessions, key) is null ? TabGroupChipLoneKey : TabGroupChipExpandedKey;
            session.TabGroupAccessibleName = _localizer.Format(chipKey, key);
        }

        // A group that no longer has a tab forgets it was collapsed: the next tab of that folder
        // opens it afresh rather than arriving folded away.
        _collapsedTabGroups.RemoveWhere(key => !seen.Contains(key));
    }
}
