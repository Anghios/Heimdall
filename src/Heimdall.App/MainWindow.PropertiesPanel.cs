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

using System.ComponentModel;
using System.Windows;
using System.Windows.Controls.Primitives;
using Heimdall.App.Services;
using Heimdall.App.ViewModels;
using Heimdall.App.ViewModels.Sidebar;

namespace Heimdall.App;

/// <summary>
/// The optional properties panel docked under the session tree.
/// </summary>
/// <remarks>
/// The rows are sized here rather than bound: a <see cref="GridSplitter"/> drag writes the row
/// height as a local value, which would silently replace any binding after the first drag, and a
/// hidden panel must give its whole row back to the tree rather than leave the dragged height empty.
/// </remarks>
public partial class MainWindow
{
    private const double PropertiesPanelSplitterHeight = 4d;

    private PropertyChangedEventHandler? _propertiesPanelServerListHandler;
    private PropertyChangedEventHandler? _propertiesPanelSidebarHandler;

    private void WirePropertiesPanel(MainViewModel viewModel)
    {
        _propertiesPanelServerListHandler = (_, e) =>
        {
            if (string.Equals(e.PropertyName, nameof(ServerListViewModel.ShowPropertiesPanel), StringComparison.Ordinal))
            {
                Dispatcher.BeginInvoke(() => ApplyPropertiesPanelLayout(viewModel));
            }
        };
        viewModel.ServerList.PropertyChanged += _propertiesPanelServerListHandler;

        _propertiesPanelSidebarHandler = (_, e) =>
        {
            if (string.Equals(e.PropertyName, nameof(SidebarViewModel.IsToolsTabActive), StringComparison.Ordinal))
            {
                Dispatcher.BeginInvoke(() => ApplyPropertiesPanelLayout(viewModel));
            }
        };
        viewModel.Sidebar.PropertyChanged += _propertiesPanelSidebarHandler;
    }

    private void UnwirePropertiesPanel(MainViewModel viewModel)
    {
        if (_propertiesPanelServerListHandler is not null)
        {
            viewModel.ServerList.PropertyChanged -= _propertiesPanelServerListHandler;
        }

        if (_propertiesPanelSidebarHandler is not null)
        {
            viewModel.Sidebar.PropertyChanged -= _propertiesPanelSidebarHandler;
        }
    }

    private void ApplyPropertiesPanelLayout(MainViewModel viewModel)
    {
        bool shown = SessionPropertiesPanelLayout.IsShown(
            viewModel.ServerList.ShowPropertiesPanel,
            viewModel.Sidebar.IsToolsTabActive);

        Visibility visibility = shown ? Visibility.Visible : Visibility.Collapsed;
        PropertiesPanelSplitter.Visibility = visibility;
        PropertiesPanel.Visibility = visibility;

        if (!shown)
        {
            PropertiesSplitterRow.Height = new GridLength(0d);
            PropertiesPanelRow.Height = new GridLength(0d);
            PropertiesPanelRow.MinHeight = 0d;
            return;
        }

        double height = SessionPropertiesPanelLayout.FitHeight(
            viewModel.ServerList.PropertiesPanelHeight,
            SidebarContentGrid.ActualHeight);
        PropertiesSplitterRow.Height = new GridLength(PropertiesPanelSplitterHeight);
        PropertiesPanelRow.MinHeight = SessionPropertiesPanelLayout.MinHeight;
        PropertiesPanelRow.Height = new GridLength(height);
    }

    private void OnPropertiesPanelSplitterDragCompleted(object sender, DragCompletedEventArgs e)
    {
        if (DataContext is MainViewModel vm && PropertiesPanel.ActualHeight > 0d)
        {
            vm.ServerList.CommitPropertiesPanelHeight(PropertiesPanel.ActualHeight);
        }
    }

    private void OnPropertiesPanelCloseClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.ServerList.ShowPropertiesPanel = false;
        }
    }
}
