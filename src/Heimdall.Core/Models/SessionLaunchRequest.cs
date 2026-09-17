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

using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace Heimdall.Core.Models;

/// <summary>
/// A request to open a session against a host that is not a saved server.
/// </summary>
/// <remarks>
/// <para>This is the payload a generated document can carry: a node of an exported
/// network diagram links to <c>heimdall://session/ssh/10.0.0.5:22?user=root</c>, and
/// activating it asks the shell for that session. The same record travels both ways,
/// so the writer and the reader cannot disagree about the format.</para>
/// <para><b>Everything here is untrusted.</b> A .drawio file can come from anywhere
/// and a link inside it is an instruction to reach a machine, so <see cref="TryParse"/>
/// admits only a known protocol, a host that is an address or a plain host name, and a
/// port in range. Whether to act on a valid request is still the caller's decision.</para>
/// </remarks>
/// <param name="Host">Address or host name to reach.</param>
/// <param name="Port">Port to reach, or null for the protocol's default.</param>
/// <param name="Protocol">One of <see cref="SupportedProtocols"/>, upper case.</param>
/// <param name="Username">User name to offer, or null.</param>
public sealed record SessionLaunchRequest(
    string Host,
    int? Port,
    string Protocol,
    string? Username = null)
{
    /// <summary>The URI scheme reserved for Heimdall's own links.</summary>
    public const string Scheme = "heimdall";

    /// <summary>The single host segment under <see cref="Scheme"/> that carries a session.</summary>
    public const string SessionAuthority = "session";

    /// <summary>Query key carrying the user name.</summary>
    public const string UsernameQueryKey = "user";

    private const int MinPort = 1;
    private const int MaxPort = 65535;

    /// <summary>Longest host name this accepts, matching the DNS limit.</summary>
    private const int MaxHostLength = 253;

    /// <summary>
    /// Protocols a link may ask for. A protocol absent from this list is refused
    /// rather than passed on, so a crafted document cannot reach a code path by
    /// naming something the shell would interpret loosely.
    /// </summary>
    public static readonly IReadOnlyList<string> SupportedProtocols =
        ["SSH", "RDP", "VNC", "SFTP", "TELNET"];

    /// <summary>
    /// A host name: dot-separated labels of letters, digits and hyphens, no leading
    /// or trailing hyphen. Addresses are recognised separately by <see cref="IPAddress"/>.
    /// </summary>
    private static readonly Regex HostNamePattern = new(
        @"^(?!-)[A-Za-z0-9-]{1,63}(?<!-)(\.(?!-)[A-Za-z0-9-]{1,63}(?<!-))*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Builds the link a document carries for this request.</summary>
    public Uri ToUri()
    {
        var builder = new UriBuilder
        {
            Scheme = Scheme,
            Host = SessionAuthority,
            Path = $"/{Protocol.ToUpperInvariant()}/{Host}"
        };

        if (Port is { } port)
        {
            builder.Path += ":" + port.ToString(CultureInfo.InvariantCulture);
        }

        if (!string.IsNullOrWhiteSpace(Username))
        {
            builder.Query = $"{UsernameQueryKey}={Uri.EscapeDataString(Username)}";
        }

        // UriBuilder.Port defaults to -1 and would otherwise render as ":-1".
        builder.Port = -1;
        return builder.Uri;
    }

    /// <summary>
    /// Reads a link back into a request, refusing anything that is not exactly one.
    /// </summary>
    /// <param name="candidate">The link text, from a document or a message.</param>
    /// <param name="request">The request, when this returns true.</param>
    /// <returns>True when <paramref name="candidate"/> is a well-formed session link.</returns>
    public static bool TryParse(string? candidate, out SessionLaunchRequest request)
    {
        request = null!;

        if (string.IsNullOrWhiteSpace(candidate)
            || !Uri.TryCreate(candidate, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, Scheme, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(uri.Host, SessionAuthority, StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrEmpty(uri.UserInfo))
        {
            return false;
        }

        // "/SSH/10.0.0.5:22" -> ["SSH", "10.0.0.5:22"]
        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length != 2)
        {
            return false;
        }

        var protocol = Uri.UnescapeDataString(segments[0]).ToUpperInvariant();
        if (!SupportedProtocols.Contains(protocol, StringComparer.Ordinal))
        {
            return false;
        }

        if (!TrySplitHostAndPort(Uri.UnescapeDataString(segments[1]), out var host, out var port))
        {
            return false;
        }

        request = new SessionLaunchRequest(host, port, protocol, ReadUsername(uri));
        return true;
    }

    private static bool TrySplitHostAndPort(string authority, out string host, out int? port)
    {
        host = string.Empty;
        port = null;

        if (string.IsNullOrWhiteSpace(authority))
        {
            return false;
        }

        var separator = authority.LastIndexOf(':');
        var hostPart = authority;

        // A bare IPv6 address also holds colons, so only a trailing all-digit run counts.
        if (separator > 0
            && authority[(separator + 1)..].All(char.IsAsciiDigit)
            && separator + 1 < authority.Length)
        {
            if (!int.TryParse(
                    authority[(separator + 1)..],
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var parsedPort)
                || parsedPort < MinPort
                || parsedPort > MaxPort)
            {
                return false;
            }

            port = parsedPort;
            hostPart = authority[..separator];
        }

        if (!IsAcceptableHost(hostPart))
        {
            return false;
        }

        host = hostPart;
        return true;
    }

    private static bool IsAcceptableHost(string candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate) || candidate.Length > MaxHostLength)
        {
            return false;
        }

        // An address in brackets is how a URI carries IPv6.
        var unbracketed = candidate.StartsWith('[') && candidate.EndsWith(']')
            ? candidate[1..^1]
            : candidate;

        return IPAddress.TryParse(unbracketed, out _)
            || HostNamePattern.IsMatch(candidate);
    }

    private static string? ReadUsername(Uri uri)
    {
        if (string.IsNullOrEmpty(uri.Query))
        {
            return null;
        }

        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var equals = pair.IndexOf('=');
            if (equals <= 0)
            {
                continue;
            }

            if (string.Equals(pair[..equals], UsernameQueryKey, StringComparison.OrdinalIgnoreCase))
            {
                var value = Uri.UnescapeDataString(pair[(equals + 1)..]);
                return string.IsNullOrWhiteSpace(value) ? null : value;
            }
        }

        return null;
    }

    /// <summary>The host and port as they read in a prompt: "10.0.0.5:22", or "10.0.0.5".</summary>
    public string Destination => Port is { } port
        ? $"{Host}:{port.ToString(CultureInfo.InvariantCulture)}"
        : Host;
}
