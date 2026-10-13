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

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Heimdall.App.Services;
using Heimdall.App.Theming;
using Heimdall.App.ViewModels;

namespace Heimdall.App;

/// <summary>
/// Partial of <see cref="MainWindow"/> hosting the session
/// <see cref="TabControl"/> drag-drop handlers: reorder by dragging tab
/// headers, merge into the active session by dropping on the content area,
/// detach to a floating window when the drop falls outside. All transient
/// state lives in the <c>_tabState</c> field
/// (<see cref="Services.TabInteractionState"/>); this file only contains
/// the WPF event handlers that mutate that state and poke
/// <c>SessionTabControl</c> / <c>ContentDropZone</c>.
/// </summary>
public partial class MainWindow
{
    private void OnTabDragStart(object sender, MouseButtonEventArgs e)
    {
        _tabState.DragItem = null;
        _tabState.DragDisplayCandidate = null;

        // Do not initiate drag when clicking the close button
        if (FindAncestor<Button>(e.OriginalSource as DependencyObject) is not null)
            return;

        _tabState.DragStartPoint = e.GetPosition(SessionTabControl);
        var tabItem = FindAncestor<TabItem>(e.OriginalSource as DependencyObject);
        _tabState.DragItem = tabItem?.DataContext as SessionTabViewModel;
        if (DataContext is MainViewModel vm)
        {
            _tabState.DragDisplayCandidate = vm.Connection.ActiveSession;
        }
    }

    private void OnTabDragMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_tabState.DragItem is null)
        {
            _tabState.DragDisplayCandidate = null;
            return;
        }

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            _tabState.DragDisplayCandidate = null;
            return;
        }

        var currentPos = e.GetPosition(SessionTabControl);
        var diff = _tabState.DragStartPoint - currentPos;

        if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
            Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
        {
            var draggedSession = _tabState.DragItem;
            var data = new System.Windows.DataObject("SessionTab", draggedSession);
            var detachVm = DataContext as MainViewModel;
            var result = System.Windows.DragDropEffects.None;
            _tabState.DragWasCancelled = false;

            if (detachVm is not null)
            {
                detachVm.DragDisplaySession = _tabState.DragDisplayCandidate;
            }

            try
            {
                result = DragDrop.DoDragDrop(SessionTabControl, data, System.Windows.DragDropEffects.Move);
            }
            finally
            {
                if (detachVm is not null)
                {
                    detachVm.DragDisplaySession = null;
                }

                _tabState.DragDisplayCandidate = null;
                _tabState.DragItem = null;
                ClearTabDropHighlight();
                ContentDropZone.Visibility = Visibility.Collapsed;
            }

            // If the drop landed outside the drag-drop host (no target accepted it),
            // detach the tab to a floating window. Explicit user cancel (Escape)
            // also yields DragDropEffects.None and must not detach.
            if (result == System.Windows.DragDropEffects.None
                && !_tabState.DragWasCancelled
                && draggedSession is not null
                && detachVm is not null)
            {
                _sessionWindowService.DetachSessionToFloatingWindow(draggedSession, detachVm);
            }

            _tabState.DragWasCancelled = false;
        }
    }

    private void OnTabQueryContinueDrag(object sender, System.Windows.QueryContinueDragEventArgs e)
    {
        if (e.EscapePressed)
        {
            _tabState.DragWasCancelled = true;
        }
    }

    private void ClearTabDropHighlight()
    {
        if (_tabState.LastDropHighlight is not null)
        {
            DropTargetVisualState.SetIsDropTarget(_tabState.LastDropHighlight, false);
            _tabState.LastDropHighlight = null;
        }
    }

    private TabItem? ResolveDropTargetTab(System.Windows.DragEventArgs e)
    {
        var targetTab = FindAncestor<TabItem>(e.OriginalSource as DependencyObject);
        if (targetTab is not null)
        {
            return targetTab;
        }

        var hit = SessionTabControl.InputHitTest(e.GetPosition(SessionTabControl)) as DependencyObject;
        targetTab = FindAncestor<TabItem>(hit);
        if (targetTab is not null)
        {
            return targetTab;
        }

        var position = e.GetPosition(SessionTabControl);
        foreach (var item in SessionTabControl.Items)
        {
            if (SessionTabControl.ItemContainerGenerator.ContainerFromItem(item) is not TabItem container)
            {
                continue;
            }

            var topLeft = container.TranslatePoint(new System.Windows.Point(0, 0), SessionTabControl);
            var bounds = new Rect(topLeft, new System.Windows.Size(container.ActualWidth, container.ActualHeight));
            if (bounds.Contains(position))
            {
                return container;
            }
        }

        return null;
    }

    private void OnTabDragOver(object sender, System.Windows.DragEventArgs e)
    {
        if (!e.Data.GetDataPresent("SessionTab"))
        {
            return;
        }

        ClearTabDropHighlight();

        e.Effects = System.Windows.DragDropEffects.Move;

        var targetTab = ResolveDropTargetTab(e);
        if (targetTab is not null
            && targetTab.DataContext != _tabState.DragItem
            && targetTab.DataContext is SessionTabViewModel hovered
            && _tabState.DragItem is { } draggedTab
            && !TabDropDecision.CanReorderOnto(hovered, draggedTab, IsArrangedByFolder()))
        {
            // A tab of another folder is not a place this tab can go: no highlight, no zone.
            ContentDropZone.Visibility = Visibility.Collapsed;
        }
        else if (targetTab is not null && targetTab.DataContext != _tabState.DragItem)
        {
            DropTargetVisualState.SetIsDropTarget(targetTab, true);
            _tabState.LastDropHighlight = targetTab;
            ContentDropZone.Visibility = Visibility.Collapsed;
        }
        else if (targetTab is null && IsOverTabStrip(e))
        {
            // The empty strip after the last tab moves the tab to the end; it is not a split.
            ContentDropZone.Visibility = Visibility.Collapsed;
        }
        else if (targetTab is null && _tabState.DragItem is not null)
        {
            // Dragging over the content area - show split drop zone
            // Allow merging into already-split sessions (N-pane support)
            if (DataContext is MainViewModel vm
                && vm.DragDisplaySession is not null
                && vm.DragDisplaySession != _tabState.DragItem)
            {
                ContentDropZone.Visibility = Visibility.Visible;
            }
        }

        e.Handled = true;
    }

    private void OnTabDrop(object sender, System.Windows.DragEventArgs e)
    {
        if (!e.Data.GetDataPresent("SessionTab"))
        {
            return;
        }

        ClearTabDropHighlight();
        ContentDropZone.Visibility = Visibility.Collapsed;

        if (DataContext is not MainViewModel vm) return;

        var draggedItem = e.Data.GetData("SessionTab") as SessionTabViewModel;
        if (draggedItem is null) return;

        var dropTarget = ResolveDropTargetTab(e);
        var targetItem = dropTarget?.DataContext as SessionTabViewModel;

        // Whatever the outcome, the drop was taken here: a handled drop never detaches the tab
        // to a floating window, which only a drop outside the host does.
        e.Handled = true;

        switch (TabDropDecision.Decide(
            IsOverTabStrip(e), targetItem, draggedItem, vm.DragDisplaySession, vm.Connection.GroupTabsByFolder))
        {
            case TabDropAction.Reorder:
                // Confined to the dragged tab's pinned group by MoveSession.
                vm.Connection.MoveSession(draggedItem, vm.Connection.ActiveSessions.IndexOf(targetItem!));
                return;

            case TabDropAction.MoveToEnd:
                vm.Connection.MoveSession(draggedItem, vm.Connection.ActiveSessions.Count - 1);
                return;

            case TabDropAction.Merge:
                // Merge with the frozen target session, not the live selected tab that WPF may
                // have switched during the drag. The orientation follows the drop position
                // relative to the content host, not the header-only tab strip.
                var pos = e.GetPosition(SessionContentHost);
                var width = SessionContentHost.ActualWidth;
                var height = SessionContentHost.ActualHeight;

                // If aspect ratio favors horizontal proximity, split horizontal
                var relY = (height > 0) ? pos.Y / height : 0.5;
                var relX = (width > 0) ? pos.X / width : 0.5;
                var distFromHEdge = Math.Min(relY, 1 - relY);
                var distFromVEdge = Math.Min(relX, 1 - relX);
                var orientation = distFromHEdge < distFromVEdge
                    ? Heimdall.Core.Models.SplitOrientation.Horizontal
                    : Heimdall.Core.Models.SplitOrientation.Vertical;

                vm.MergeExistingSession(vm.DragDisplaySession!, draggedItem.ServerId, orientation);
                return;

            default:
                return;
        }
    }

    private bool IsArrangedByFolder() => GetMainVm()?.Connection.GroupTabsByFolder == true;

    /// <summary>
    /// Closes the tab under a middle-click, as browsers and editors do. Goes through the close
    /// command, so a connected session still asks first.
    /// </summary>
    private void OnTabMiddleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle
            || FindAncestor<TabItem>(e.OriginalSource as DependencyObject)?.DataContext is not SessionTabViewModel session
            || GetMainVm() is not { } vm)
        {
            return;
        }

        e.Handled = true;
        if (vm.Connection.CloseSessionCommand.CanExecute(session))
        {
            vm.Connection.CloseSessionCommand.Execute(session);
        }
    }

    /// <summary>True when the drag point is inside the tab strip rather than the content area.</summary>
    private bool IsOverTabStrip(System.Windows.DragEventArgs e)
    {
        var position = e.GetPosition(SessionTabControl);
        return position.X >= 0
            && position.Y >= 0
            && position.X <= SessionTabControl.ActualWidth
            && position.Y <= SessionTabControl.ActualHeight;
    }
}
