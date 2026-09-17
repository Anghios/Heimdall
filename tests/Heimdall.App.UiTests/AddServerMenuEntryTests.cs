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
using Heimdall.App.UiTests.Infrastructure;
using Heimdall.App.Views.Tools;
using Heimdall.Core.Models;

namespace Heimdall.App.UiTests;

/// <summary>
/// The "Add to servers" entry of a discovered host: that it is built only when
/// something can carry it out, and that clicking it hands over the session the host
/// offers.
/// </summary>
/// <remarks>
/// <para>This raises the menu item's own Click event rather than calling the handler,
/// because the handler was never the part that was broken. The entry was built, it
/// was clickable, and the click went to an open-tool callback naming a tool that did
/// not exist. Calling a handler would have proved nothing about that.</para>
/// <para>It still stops short of the real gesture: nothing here opens the cartography
/// grid or right-clicks a row. That the view reaches this builder at all is pinned by
/// a source guard in <c>AddServerRouteTests</c>, and the rest needs the application.</para>
/// </remarks>
[Collection(DesktopUiCollection.Name)]
public sealed class AddServerMenuEntryTests
{
    private const string DiscoveredHost = "10.0.0.5";
    private const string DiscoveredName = "printer-01";

    private static readonly int[] RdpAndSsh = [3389, 22];

    [Fact]
    public async Task WithoutACallback_TheEntryIsNotOffered()
    {
        await WpfTestHost.Dispatcher.InvokeAsync(() =>
        {
            var items = BuildHostMenu(addServerCallback: null);

            Assert.Empty(EntriesLabelled(items, "ToolCtxAddToServers"));
        }).Task;
    }

    [Fact]
    public async Task WithACallback_ClickingTheEntryFilesTheSessionTheHostOffers()
    {
        await WpfTestHost.Dispatcher.InvokeAsync(() =>
        {
            SessionLaunchRequest? filed = null;
            string? filedName = null;

            var items = BuildHostMenu((request, name) =>
            {
                filed = request;
                filedName = name;
            });

            var entry = Assert.Single(EntriesLabelled(items, "ToolCtxAddToServers"));
            entry.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

            Assert.NotNull(filed);

            // A host offering both is a workstation or a Windows server, and the
            // remote desktop is the session meant. Same decision as the connect
            // entry beside it, taken in SessionProtocolChoice.
            Assert.Equal("RDP", filed!.Protocol);
            Assert.Equal(3389, filed.Port);
            Assert.Equal(DiscoveredHost, filed.Host);
            Assert.Equal(DiscoveredName, filedName);
        }).Task;
    }

    /// <summary>
    /// A host with nothing to connect to is still worth recording, so the entry
    /// stays and falls back to SSH on its default port.
    /// </summary>
    [Fact]
    public async Task AHostOfferingNoSession_IsStillOfferedAsASshServer()
    {
        await WpfTestHost.Dispatcher.InvokeAsync(() =>
        {
            SessionLaunchRequest? filed = null;

            var items = ToolContextMenuHelper.BuildHostActions(
                DiscoveredHost,
                DiscoveredName,
                [445],
                WpfTestHost.Localizer,
                (_, _, _) => { },
                openSessionCallback: null,
                addServerCallback: (request, _) => filed = request);

            var entry = Assert.Single(EntriesLabelled(items, "ToolCtxAddToServers"));
            entry.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

            Assert.NotNull(filed);
            Assert.Equal("SSH", filed!.Protocol);
            Assert.Equal(22, filed.Port);
        }).Task;
    }

    private static List<object> BuildHostMenu(Action<SessionLaunchRequest, string?>? addServerCallback)
    {
        return ToolContextMenuHelper.BuildHostActions(
            DiscoveredHost,
            DiscoveredName,
            RdpAndSsh,
            WpfTestHost.Localizer,
            (_, _, _) => { },
            openSessionCallback: null,
            addServerCallback: addServerCallback);
    }

    private static List<MenuItem> EntriesLabelled(IEnumerable<object> items, string localeKey)
    {
        string label = WpfTestHost.Translate(localeKey);

        return items
            .OfType<MenuItem>()
            .Where(item => string.Equals(item.Header as string, label, StringComparison.Ordinal))
            .ToList();
    }
}
