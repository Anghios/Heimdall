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

using Heimdall.App.ViewModels;

namespace Heimdall.App.Services;

/// <summary>What dropping a dragged session tab does.</summary>
internal enum TabDropAction
{
    /// <summary>Nothing: the tab was dropped on itself, or there is nothing to merge into.</summary>
    None,

    /// <summary>Move the tab to the place of the tab it was dropped on.</summary>
    Reorder,

    /// <summary>Move the tab to the end of the strip.</summary>
    MoveToEnd,

    /// <summary>Split the displayed session and put the dragged one beside it.</summary>
    Merge,
}

/// <summary>
/// Decides what a session tab drop does from where it landed.
/// </summary>
/// <remarks>
/// The strip and the content area are two drop zones with two meanings. A drop on the strip
/// that misses every tab - the empty space after the last one, where a user drags a tab to send
/// it to the end - used to fall through to the content-area rule and split the displayed
/// session instead.
/// </remarks>
internal static class TabDropDecision
{
    /// <param name="overTabStrip">The drop point is inside the tab strip.</param>
    /// <param name="targetTab">The tab under the drop point, or null.</param>
    /// <param name="dragged">The tab being dragged.</param>
    /// <param name="displayed">The session shown in the content area while dragging, or null.</param>
    /// <param name="tabsArrangedByFolder">The strip arranges the tabs by folder.</param>
    internal static TabDropAction Decide(
        bool overTabStrip,
        SessionTabViewModel? targetTab,
        SessionTabViewModel dragged,
        SessionTabViewModel? displayed,
        bool tabsArrangedByFolder = false)
    {
        ArgumentNullException.ThrowIfNull(dragged);

        if (targetTab is not null)
        {
            return ReferenceEquals(targetTab, dragged) || !CanReorderOnto(targetTab, dragged, tabsArrangedByFolder)
                ? TabDropAction.None
                : TabDropAction.Reorder;
        }

        if (overTabStrip)
        {
            return TabDropAction.MoveToEnd;
        }

        return displayed is null || ReferenceEquals(displayed, dragged)
            ? TabDropAction.None
            : TabDropAction.Merge;
    }

    /// <summary>
    /// Whether a drag that no drop target took detaches the tab to a floating window.
    /// </summary>
    /// <remarks>
    /// Any refused drop used to detach, so a tab let go over the session tree or the toolbar,
    /// while reordering, popped out into a window of its own. Outside the window is the gesture,
    /// and so is the session content area, which is the only "outside" a maximised window on a
    /// single monitor has.
    /// </remarks>
    /// <param name="accepted">A drop target took the tab.</param>
    /// <param name="cancelled">The user pressed Escape.</param>
    /// <param name="releasedInsideWindow">The pointer was released over the main window.</param>
    /// <param name="releasedOverSessionContent">The pointer was released over the session content area.</param>
    internal static bool ShouldDetach(
        bool accepted,
        bool cancelled,
        bool releasedInsideWindow,
        bool releasedOverSessionContent)
    {
        return !accepted
            && !cancelled
            && (!releasedInsideWindow || releasedOverSessionContent);
    }

    /// <summary>
    /// Whether a tab may be dropped onto <paramref name="target"/> to take its place. With the
    /// tabs arranged by folder, only within its own folder: the arrangement puts a tab moved into
    /// another folder straight back, so the drop used to land and then undo itself unexplained.
    /// A drop target refused here is not highlighted either.
    /// </summary>
    internal static bool CanReorderOnto(
        SessionTabViewModel target,
        SessionTabViewModel dragged,
        bool tabsArrangedByFolder)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(dragged);

        return !tabsArrangedByFolder
            || string.Equals(target.TabGroupKey, dragged.TabGroupKey, StringComparison.OrdinalIgnoreCase);
    }
}
