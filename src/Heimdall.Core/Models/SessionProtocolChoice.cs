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

namespace Heimdall.Core.Models;

/// <summary>
/// Picks the session a discovered host is most likely to want, from the ports
/// found open on it.
/// </summary>
/// <remarks>
/// Two surfaces answer this question about the same scan: the cartography
/// context menu, and the exported network diagram whose nodes link to a
/// session. They must agree, so the decision lives here rather than being
/// spelled out at each site.
/// </remarks>
public static class SessionProtocolChoice
{
    /// <summary>
    /// Ports in the order they are preferred, each with the protocol it implies.
    /// A remote desktop beats a shell: a host offering both is usually a
    /// workstation or a Windows server, and that is the session the user means.
    /// </summary>
    private static readonly (int Port, string Protocol)[] Preference =
    [
        (DefaultPorts.Rdp, "RDP"),
        (DefaultPorts.Ssh, "SSH"),
        (DefaultPorts.Vnc, "VNC"),
        (DefaultPorts.Telnet, "TELNET"),
    ];

    /// <summary>
    /// Chooses a protocol and port for <paramref name="openPorts"/>.
    /// </summary>
    /// <param name="openPorts">The ports found open, in any order.</param>
    /// <returns>
    /// The protocol and the port it was chosen for, or null when nothing among
    /// the open ports offers a session Heimdall can open.
    /// </returns>
    public static (string Protocol, int Port)? Choose(IEnumerable<int>? openPorts)
    {
        if (openPorts is null)
        {
            return null;
        }

        var ports = openPorts as IReadOnlyCollection<int> ?? openPorts.ToList();
        if (ports.Count == 0)
        {
            return null;
        }

        foreach (var (port, protocol) in Preference)
        {
            if (ports.Contains(port))
            {
                return (protocol, port);
            }
        }

        return null;
    }

    /// <summary>
    /// Builds the session a host's open ports call for.
    /// </summary>
    /// <param name="host">Address or host name of the discovered machine.</param>
    /// <param name="openPorts">The ports found open on it.</param>
    /// <returns>The request, or null when no supported session is on offer.</returns>
    public static SessionLaunchRequest? ForHost(string host, IEnumerable<int>? openPorts)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return null;
        }

        return Choose(openPorts) is not { } choice
            ? null
            : new SessionLaunchRequest(host, choice.Port, choice.Protocol);
    }
}
