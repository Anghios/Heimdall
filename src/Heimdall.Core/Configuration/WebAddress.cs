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
using Heimdall.Core.Models;

namespace Heimdall.Core.Configuration;

/// <summary>
/// The address a web profile opens, built from its host, port, scheme and path.
/// </summary>
/// <remarks>
/// A web profile keeps its host in <see cref="ServerProfileDto.RemoteServer"/> like every other
/// type, so the tree, the search, the health probe and the external tools read the same field.
/// The address is derived, never stored, so it cannot disagree with the host it was built from.
/// </remarks>
public static class WebAddress
{
    public const string HttpsScheme = "https";
    public const string HttpScheme = "http";

    /// <summary>The scheme a profile uses.</summary>
    public static string SchemeOf(bool useHttp) => useHttp ? HttpScheme : HttpsScheme;

    /// <summary>The port a scheme uses when the profile names none.</summary>
    public static int DefaultPortFor(bool useHttp) => useHttp ? DefaultPorts.HttpStd : DefaultPorts.HttpsStd;

    /// <summary>The address of <paramref name="profile"/>, or null when it cannot form one.</summary>
    public static Uri? From(ServerProfileDto profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return TryBuild(profile.RemoteServer, profile.WebPort, profile.WebUseHttp, profile.WebPath, out Uri? address)
            ? address
            : null;
    }

    /// <summary>
    /// Builds an absolute http(s) address. Refuses a host that already carries a scheme, a path
    /// or credentials, so a URL pasted into the host field is reported rather than mangled.
    /// </summary>
    public static bool TryBuild(string? host, int port, bool useHttp, string? path, out Uri? address)
    {
        address = null;
        string trimmedHost = host?.Trim() ?? string.Empty;
        if (trimmedHost.Length == 0
            || trimmedHost.IndexOfAny(['/', '\\', '@', '?', '#', ' ']) >= 0
            || port is < 1 or > 65535)
        {
            return false;
        }

        // An IPv6 literal needs brackets in an address; a bare host:port is not a host.
        bool isIpv6 = trimmedHost.Contains(':', StringComparison.Ordinal);
        if (isIpv6 && !trimmedHost.StartsWith('['))
        {
            trimmedHost = $"[{trimmedHost}]";
        }

        string scheme = SchemeOf(useHttp);
        string portPart = port == DefaultPortFor(useHttp)
            ? string.Empty
            : ":" + port.ToString(CultureInfo.InvariantCulture);
        string pathPart = NormalizePath(path);

        if (!Uri.TryCreate($"{scheme}://{trimmedHost}{portPart}{pathPart}", UriKind.Absolute, out Uri? candidate)
            || !string.Equals(candidate.Scheme, scheme, StringComparison.Ordinal)
            || !string.IsNullOrEmpty(candidate.UserInfo)
            || candidate.Port != port)
        {
            return false;
        }

        address = candidate;
        return true;
    }

    private static string NormalizePath(string? path)
    {
        string trimmed = path?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            return "/";
        }

        return trimmed[0] is '/' or '?' or '#' ? trimmed : "/" + trimmed;
    }
}
