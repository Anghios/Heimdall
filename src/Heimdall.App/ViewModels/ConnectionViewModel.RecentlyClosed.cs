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
/// one back the way a browser does.
/// </summary>
/// <remarks>
/// Only a close the user asked for is remembered: not an application exit, a failed session or a
/// reconnect. Tool tabs and ad-hoc connections have no saved profile to reopen and are skipped.
/// Session-lifetime only, like pinning; never persisted.
/// </remarks>
public partial class ConnectionViewModel
{
    /// <summary>How many closed tabs are remembered.</summary>
    internal const int RecentlyClosedCapacity = 10;

    private readonly List<string> _recentlyClosedProfileIds = [];

    /// <summary>The remembered profile ids, oldest first. Exposed for tests.</summary>
    internal IReadOnlyList<string> RecentlyClosedProfileIds => _recentlyClosedProfileIds;

    /// <summary>True when there is a closed tab to reopen.</summary>
    public bool HasRecentlyClosedTab => _recentlyClosedProfileIds.Count > 0;

    /// <summary>
    /// Takes the most recently closed profile that <paramref name="stillExists"/> accepts, dropping
    /// the ones it refuses on the way (a profile deleted since). Null when none is left.
    /// </summary>
    internal string? TakeMostRecentlyClosed(Func<string, bool> stillExists)
    {
        ArgumentNullException.ThrowIfNull(stillExists);

        while (_recentlyClosedProfileIds.Count > 0)
        {
            int last = _recentlyClosedProfileIds.Count - 1;
            string profileId = _recentlyClosedProfileIds[last];
            _recentlyClosedProfileIds.RemoveAt(last);
            if (stillExists(profileId))
            {
                return profileId;
            }
        }

        return null;
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

        _recentlyClosedProfileIds.Add(session.ProfileLookupServerId);
        if (_recentlyClosedProfileIds.Count > RecentlyClosedCapacity)
        {
            _recentlyClosedProfileIds.RemoveAt(0);
        }
    }
}
