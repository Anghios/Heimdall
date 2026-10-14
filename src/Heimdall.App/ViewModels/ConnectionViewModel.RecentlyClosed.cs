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
using Heimdall.Core.Configuration;

namespace Heimdall.App.ViewModels;

/// <summary>
/// The profiles of the tabs the user closed, most recent last, so "Reopen closed tab" can bring
/// them back the way a browser does.
/// </summary>
/// <remarks>
/// <para>Only a close the user asked for is remembered: not an application exit, a failed session
/// or a reconnect. Tool tabs and ad-hoc connections have no saved profile to reopen and are
/// skipped. Session-lifetime only, like pinning; never persisted.</para>
/// <para>One gesture is one entry. "Close others" over twenty-five tabs used to leave ten single
/// entries, the last ten, to bring back one right-click at a time: the other fifteen were gone.
/// A bulk close is now remembered whole and reopened whole.</para>
/// </remarks>
public partial class ConnectionViewModel
{
    /// <summary>How many close gestures are remembered.</summary>
    internal const int RecentlyClosedCapacity = 10;

    private readonly List<List<string>> _recentlyClosedGroups = [];

    /// <summary>Collects the tabs of a bulk close into one entry while it runs.</summary>
    private List<string>? _recentlyClosedBatch;

    /// <summary>The remembered profile ids, oldest first. Exposed for tests.</summary>
    internal IReadOnlyList<string> RecentlyClosedProfileIds => _recentlyClosedGroups.SelectMany(group => group).ToList();

    /// <summary>How many close gestures are remembered. Exposed for tests.</summary>
    internal int RecentlyClosedGroupCount => _recentlyClosedGroups.Count;

    /// <summary>True when there is a closed tab to reopen.</summary>
    public bool HasRecentlyClosedTab => _recentlyClosedGroups.Count > 0;

    /// <summary>How many tabs the most recent close gesture closed; 0 when none is remembered.</summary>
    public int MostRecentlyClosedCount => _recentlyClosedGroups.Count > 0 ? _recentlyClosedGroups[^1].Count : 0;

    /// <summary>
    /// Takes the profiles of the most recent close gesture that <paramref name="stillExists"/>
    /// accepts, in the order they were closed. A gesture whose profiles were all deleted since is
    /// dropped and the one before it is taken. Empty when none is left.
    /// </summary>
    internal IReadOnlyList<string> TakeMostRecentlyClosed(Func<string, bool> stillExists)
    {
        ArgumentNullException.ThrowIfNull(stillExists);

        while (_recentlyClosedGroups.Count > 0)
        {
            List<string> group = _recentlyClosedGroups[^1];
            _recentlyClosedGroups.RemoveAt(_recentlyClosedGroups.Count - 1);
            List<string> kept = group.Where(stillExists).ToList();
            if (kept.Count > 0)
            {
                return kept;
            }
        }

        return [];
    }

    /// <summary>
    /// Runs a bulk close so that every tab it closes is remembered as one entry.
    /// </summary>
    private async Task RememberAsOneGestureAsync(Func<Task> closeAll)
    {
        if (_recentlyClosedBatch is not null)
        {
            await closeAll();
            return;
        }

        _recentlyClosedBatch = [];
        try
        {
            await closeAll();
        }
        finally
        {
            List<string> batch = _recentlyClosedBatch;
            _recentlyClosedBatch = null;
            if (batch.Count > 0)
            {
                AddRecentlyClosedGroup(batch);
            }
        }
    }

    private void RememberClosedTab(SessionTabViewModel session, CloseRequest request)
    {
        if (!request.IsInteractive
            || request.Reason is not (DisconnectReason.TabClose or DisconnectReason.UserAction)
            || session.IsAdHoc
            || ConnectionTypeCatalog.IsToolConnectionType(session.ConnectionType)
            || string.IsNullOrWhiteSpace(session.ProfileLookupServerId))
        {
            return;
        }

        if (_recentlyClosedBatch is not null)
        {
            _recentlyClosedBatch.Add(session.ProfileLookupServerId);
            return;
        }

        AddRecentlyClosedGroup([session.ProfileLookupServerId]);
    }

    private void AddRecentlyClosedGroup(List<string> group)
    {
        _recentlyClosedGroups.Add(group);
        if (_recentlyClosedGroups.Count > RecentlyClosedCapacity)
        {
            _recentlyClosedGroups.RemoveAt(0);
        }
    }
}
