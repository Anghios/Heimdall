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
using Heimdall.Core.StateMachine;

namespace Heimdall.App.ViewModels;

/// <summary>
/// What the properties panel shows for several sessions at once: a folder under the tree cursor,
/// or a multi-selection.
/// </summary>
/// <remarks>
/// A snapshot. The list rebuilds it when the set or a session state changes, so the panel never
/// holds a subscription to rows that a reload has replaced.
/// </remarks>
public sealed class PropertiesSummaryViewModel
{
    private PropertiesSummaryViewModel(
        string title,
        string path,
        int sessionCount,
        int connectedCount,
        IReadOnlyList<ProtocolCountRow> protocols)
    {
        Title = title;
        Path = path;
        SessionCount = sessionCount;
        ConnectedCount = connectedCount;
        Protocols = protocols;
    }

    /// <summary>The folder name, or the selection size spelled out.</summary>
    public string Title { get; }

    /// <summary>The folder's full path, or empty: a selection has none, nor has the root group.</summary>
    public string Path { get; }

    /// <summary>How many sessions the set holds.</summary>
    public int SessionCount { get; }

    /// <summary>How many of them have a session open.</summary>
    public int ConnectedCount { get; }

    /// <summary><see cref="SessionCount"/> for the current culture.</summary>
    public string SessionCountText => SessionCount.ToString(CultureInfo.CurrentCulture);

    /// <summary><see cref="ConnectedCount"/> for the current culture.</summary>
    public string ConnectedCountText => ConnectedCount.ToString(CultureInfo.CurrentCulture);

    /// <summary>One line per protocol, the most used first.</summary>
    public IReadOnlyList<ProtocolCountRow> Protocols { get; }

    /// <summary>
    /// Summarizes <paramref name="servers"/>.
    /// </summary>
    /// <param name="title">The folder name, or the selection size spelled out.</param>
    /// <param name="path">The folder's full path, or empty.</param>
    /// <param name="servers">The sessions of the set; a row listed twice counts once.</param>
    public static PropertiesSummaryViewModel Build(
        string title,
        string path,
        IEnumerable<ServerItemViewModel> servers)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(servers);

        List<ServerItemViewModel> distinct = servers.Distinct(ReferenceEqualityComparer.Instance)
            .Cast<ServerItemViewModel>()
            .ToList();

        // Ties are broken by name so the order does not move when the set is rebuilt.
        List<ProtocolCountRow> protocols = distinct
            .GroupBy(server => server.ProtocolDisplayName, StringComparer.Ordinal)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.CurrentCulture)
            .Select(group => new ProtocolCountRow(
                group.Key,
                group.Count().ToString(CultureInfo.CurrentCulture)))
            .ToList();

        return new PropertiesSummaryViewModel(
            title,
            path,
            distinct.Count,
            distinct.Count(server => ConnectionStateSets.IsConnected(server.ConnectionState)),
            protocols);
    }
}

/// <summary>One protocol of a <see cref="PropertiesSummaryViewModel"/> and how many sessions use it.</summary>
/// <param name="Protocol">The protocol as the tree names it.</param>
/// <param name="CountText">The count for the current culture.</param>
public sealed record ProtocolCountRow(string Protocol, string CountText);
