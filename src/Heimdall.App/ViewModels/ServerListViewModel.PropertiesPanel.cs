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

namespace Heimdall.App.ViewModels;

/// <summary>
/// What the properties panel under the tree shows: one session, a summary of several, or
/// nothing.
/// </summary>
/// <remarks>
/// A folder is never selected; the tree marks the folder under its cursor instead. The panel
/// follows whichever the user touched last: the window hands the cursor folder over as it moves,
/// and any change of selection takes it back, so picking a session from the palette or a search
/// is not hidden behind a folder the cursor left on the screen.
/// </remarks>
public partial class ServerListViewModel
{
    private FolderViewModel? _propertiesFolder;
    private PropertiesSummaryViewModel? _propertiesSummary;

    /// <summary>
    /// The folder under the tree cursor, handed over by the window; null when the cursor is on a
    /// session, or once the selection has changed since.
    /// </summary>
    public FolderViewModel? PropertiesFolder
    {
        get => _propertiesFolder;
        set
        {
            if (ReferenceEquals(_propertiesFolder, value))
            {
                return;
            }

            _propertiesFolder = value;
            OnPropertyChanged();
            RefreshPropertiesSummary();
        }
    }

    /// <summary>
    /// The summary the panel shows for a folder or a multi-selection; null when it shows one
    /// session or nothing.
    /// </summary>
    public PropertiesSummaryViewModel? PropertiesSummary => _propertiesSummary;

    /// <summary>Whether the panel shows a summary.</summary>
    public bool PropertiesPanelShowsSummary => _propertiesSummary is not null;

    /// <summary>Whether the panel shows the properties of the selected session.</summary>
    public bool PropertiesPanelShowsSession => _propertiesSummary is null && ShowSessionDetail;

    /// <summary>Whether the panel shows its hint that nothing is selected.</summary>
    public bool PropertiesPanelShowsEmpty => _propertiesSummary is null && !ShowSessionDetail;

    /// <summary>
    /// Rebuilds the summary from the current folder or selection, and tells the panel.
    /// </summary>
    /// <remarks>
    /// Called when the folder, the selection, the visible tree or a session state changes. The
    /// failure paths that write <c>Error</c> straight onto a row leave the connected count alone:
    /// a launch that never connected was not counted as connected.
    /// </remarks>
    internal void RefreshPropertiesSummary()
    {
        // A reload or a filter can take the folder off the tree; its summary would describe rows
        // that are no longer there.
        if (_propertiesFolder is { } shown && !IsFolderOnScreen(GroupedServers, shown))
        {
            _propertiesFolder = null;
            OnPropertyChanged(nameof(PropertiesFolder));
        }

        PropertiesSummaryViewModel? summary = null;
        if (_propertiesFolder is { } folder)
        {
            summary = PropertiesSummaryViewModel.Build(
                folder.Name,
                folder.FullPath,
                EnumerateFolderSessions(folder));
        }
        else if (HasMultiSelection)
        {
            summary = PropertiesSummaryViewModel.Build(
                SelectionCountText,
                "",
                SelectedItems);
        }

        if (summary is null && _propertiesSummary is null)
        {
            return;
        }

        _propertiesSummary = summary;
        OnPropertyChanged(nameof(PropertiesSummary));
        NotifyPropertiesPanelMode();
    }

    private void NotifyPropertiesPanelMode()
    {
        OnPropertyChanged(nameof(PropertiesPanelShowsSummary));
        OnPropertyChanged(nameof(PropertiesPanelShowsSession));
        OnPropertyChanged(nameof(PropertiesPanelShowsEmpty));
    }

    /// <summary>
    /// The selection moved: the panel follows it rather than a folder the cursor left behind.
    /// </summary>
    private void OnPropertiesPanelSelectionChanged()
    {
        // A filter or a collapse moving the selection is the view, not the user moving on.
        if (_propertiesFolder is not null && !_synchronizingSelection)
        {
            _propertiesFolder = null;
            OnPropertyChanged(nameof(PropertiesFolder));
        }

        RefreshPropertiesSummary();
        NotifyPropertiesPanelMode();
    }

    private static bool IsFolderOnScreen(IEnumerable<FolderViewModel> folders, FolderViewModel target)
    {
        foreach (FolderViewModel folder in folders)
        {
            if (ReferenceEquals(folder, target) || IsFolderOnScreen(folder.SubFolders, target))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The sessions a folder holds on screen, its subfolders included.</summary>
    private static IEnumerable<ServerItemViewModel> EnumerateFolderSessions(FolderViewModel folder)
    {
        foreach (ServerItemViewModel server in folder.Servers)
        {
            yield return server;
        }

        foreach (FolderViewModel subFolder in folder.SubFolders)
        {
            foreach (ServerItemViewModel server in EnumerateFolderSessions(subFolder))
            {
                yield return server;
            }
        }
    }
}
