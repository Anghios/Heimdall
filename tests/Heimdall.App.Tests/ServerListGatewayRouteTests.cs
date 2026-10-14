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
using Heimdall.App.ViewModels;
using Heimdall.Core.Configuration;

namespace Heimdall.App.Tests;

/// <summary>
/// A session row names the gateway its connection goes through, as the connection decides it:
/// one inherited from the folder shows, and one bypassed by a direct connection does not.
/// </summary>
/// <remarks>
/// The rows used to read only the profile's own gateway, so a folder default routed every
/// connection through a bastion the tree never mentioned, and a profile set to connect directly
/// still announced the gateway it no longer used.
/// </remarks>
public sealed partial class ServerListSelectionTests
{
    private const string RouteGatewayId = "gw-route";
    private const string RouteGatewayName = "Folder bastion";
    private const string RouteFolder = "PROD";

    [Fact]
    public async Task Row_AGatewayInheritedFromTheFolder_ShowsTheBadge()
    {
        await using var fixture = await ServerListSelectionFixture.CreateAsync();
        fixture.LoadServers(FolderGatewaySettings(), ServerInFolder("alpha"));

        ServerItemViewModel row = fixture.ServerById("alpha");

        Assert.True(row.HasGateway);
        Assert.True(row.IsGatewayBadgeVisible);
        Assert.Contains(RouteGatewayName, row.GatewayBadgeText);
        Assert.Equal(RouteGatewayName, row.GatewayDetailText);
    }

    [Fact]
    public async Task Row_ADirectConnection_ShowsNoBadge_WhateverGatewayItNames()
    {
        await using var fixture = await ServerListSelectionFixture.CreateAsync();
        ServerProfileDto ownGateway = ServerInFolder("own");
        ownGateway.SshGatewayId = RouteGatewayId;
        ownGateway.UseDirectConnection = true;
        ServerProfileDto missingGateway = ServerInFolder("missing");
        missingGateway.SshGatewayId = "gw-deleted";
        missingGateway.UseDirectConnection = true;
        ServerProfileDto inherited = ServerInFolder("inherited");
        inherited.UseDirectConnection = true;
        fixture.LoadServers(FolderGatewaySettings(), ownGateway, missingGateway, inherited);

        foreach (string id in new[] { "own", "missing", "inherited" })
        {
            ServerItemViewModel row = fixture.ServerById(id);

            // A gateway the connection bypasses is neither shown nor reported missing: a missing
            // badge stays visible even when badges are hidden, and this session connects fine.
            Assert.False(row.HasGateway, id);
            Assert.False(row.IsGatewayMissing, id);
            Assert.False(row.IsGatewayBadgeVisible, id);
            Assert.Equal("", row.GatewayDetailText);
        }
    }

    [Fact]
    public async Task Row_AFolderDefaultAddedInTheSettings_ReachesTheRowAndRaisesItsChange()
    {
        await using var fixture = await ServerListSelectionFixture.CreateAsync();
        AppSettings withoutDefault = FolderGatewaySettings();
        withoutDefault.GroupDefaults.Clear();
        fixture.LoadServers(withoutDefault, ServerInFolder("alpha"));
        ServerItemViewModel row = fixture.ServerById("alpha");
        Assert.False(row.HasGateway);
        List<string?> changed = [];
        ((INotifyPropertyChanged)row).PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        await fixture.ConfigManager.SaveSettingsAsync(FolderGatewaySettings());

        Assert.Same(row, fixture.ServerById("alpha"));
        Assert.True(row.HasGateway);
        Assert.Contains(RouteGatewayName, row.GatewayBadgeText);
        Assert.Contains(nameof(ServerItemViewModel.GatewayBadgeText), changed);
        Assert.Contains(nameof(ServerItemViewModel.IsGatewayBadgeVisible), changed);
    }

    [Fact]
    public async Task Row_AFolderDefaultRemovedInTheSettings_TakesTheBadgeAway()
    {
        await using var fixture = await ServerListSelectionFixture.CreateAsync();
        fixture.LoadServers(FolderGatewaySettings(), ServerInFolder("alpha"));
        Assert.True(fixture.ServerById("alpha").HasGateway);

        AppSettings withoutDefault = FolderGatewaySettings();
        withoutDefault.GroupDefaults.Clear();
        await fixture.ConfigManager.SaveSettingsAsync(withoutDefault);

        ServerItemViewModel row = fixture.ServerById("alpha");
        Assert.False(row.HasGateway);
        Assert.False(row.IsGatewayBadgeVisible);
    }

    private static AppSettings FolderGatewaySettings()
    {
        AppSettings settings = new();
        settings.SshGateways.Add(new SshGatewayDto
        {
            Id = RouteGatewayId,
            Name = RouteGatewayName,
            Host = "127.0.0.1",
            Port = 22,
            User = "bastion"
        });
        settings.GroupDefaults[RouteFolder] = new GroupDefaultsDto { SshGatewayId = RouteGatewayId };
        return settings;
    }

    private static ServerProfileDto ServerInFolder(string id)
    {
        ServerProfileDto server = CreateServer(id, id, "ops");
        server.ConnectionType = "SSH";
        server.Group = RouteFolder;
        server.SshGatewayId = null;
        server.UseDirectConnection = false;
        return server;
    }
}
