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

/// <summary>
/// Writes the tooltip of a session tab: the full title, then where the session goes, as whom,
/// from which folder, and its state when it needs a look.
/// </summary>
/// <remarks>
/// The title is cut at the strip's width, so five tabs named "web-prod-..." used to hover to
/// the same cut name. The endpoint is what tells them apart. Composed when the tooltip opens,
/// because the profile behind a tab is renamed or edited while the tab stays open.
/// </remarks>
internal static class SessionTabToolTipComposer
{
    /// <param name="session">The tab.</param>
    /// <param name="profile">The profile behind it, or null for a tool tab or a deleted profile.</param>
    /// <param name="localize">Looks a key up in the active catalogue.</param>
    /// <param name="format">Formats a key from the active catalogue.</param>
    internal static string Compose(
        SessionTabViewModel session,
        ServerItemViewModel? profile,
        Func<string, string> localize,
        Func<string, object[], string> format)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(localize);
        ArgumentNullException.ThrowIfNull(format);

        List<string> lines = [session.HeaderToolTip];

        if (profile is not null)
        {
            if (!string.IsNullOrWhiteSpace(profile.Endpoint))
            {
                lines.Add(format("SessionTabToolTipEndpoint", [profile.Endpoint, profile.ProtocolDisplayName]));
            }

            if (!string.IsNullOrWhiteSpace(profile.Username))
            {
                lines.Add(format("SessionTabToolTipUser", [profile.Username]));
            }

            if (!string.IsNullOrWhiteSpace(profile.Group))
            {
                lines.Add(format("SessionTabToolTipFolder", [profile.Group]));
            }
        }

        if (StateText(session, localize) is { } state)
        {
            lines.Add(format("SessionTabToolTipState", [state]));
        }

        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>
    /// The profile behind a tab, searched in the whole inventory rather than the tree's filtered
    /// view: with a search typed in the tree, the profile of an open tab is usually filtered out.
    /// </summary>
    internal static ServerItemViewModel? FindProfile(MainViewModel vm, SessionTabViewModel session)
    {
        ArgumentNullException.ThrowIfNull(vm);
        ArgumentNullException.ThrowIfNull(session);

        string lookupId = session.ProfileLookupServerId;
        return string.IsNullOrEmpty(lookupId)
            ? null
            : vm.ServerList.AllServers.FirstOrDefault(
                (ServerItemViewModel server) => string.Equals(server.Id, lookupId, StringComparison.Ordinal));
    }

    /// <summary>
    /// The state in words, or null for an established tab. A failure written as free text is
    /// shown as written: the tooltip is read once, on demand, and the reason is the useful part.
    /// </summary>
    private static string? StateText(SessionTabViewModel session, Func<string, string> localize)
    {
        SessionTabHeaderState state = session.HeaderState;
        if (state == SessionTabHeaderState.Established)
        {
            return null;
        }

        if (state == SessionTabHeaderState.Failed
            && SessionStatusDisplay.ResolveKey(session.Status) is null
            && !string.IsNullOrWhiteSpace(session.Status))
        {
            return session.Status;
        }

        return SessionTabHeaderStates.ResolveKey(state, session.Status) is { } key ? localize(key) : null;
    }
}
