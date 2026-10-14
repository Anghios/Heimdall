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

using Heimdall.Core.Configuration;

namespace Heimdall.Core.Tests.Configuration;

/// <summary>
/// The one decision of whether a profile goes through an SSH gateway, and which one.
/// </summary>
public sealed class SshGatewayRouteTests
{
    private const string OwnGateway = "gw-own";
    private const string GroupGateway = "gw-group";

    [Fact]
    public void AGatewayOnTheProfile_IsTheRoute()
    {
        ServerProfileDto server = Profile("SSH", gatewayId: OwnGateway);

        Assert.Equal(OwnGateway, SshGatewayRoute.GatewayIdFor(server, groupDefaults: null));
    }

    [Fact]
    public void UseDirectConnection_WinsOverTheProfileGateway()
    {
        ServerProfileDto server = Profile("SSH", gatewayId: OwnGateway);
        server.UseDirectConnection = true;

        Assert.Null(SshGatewayRoute.GatewayIdFor(server, groupDefaults: null));
        Assert.False(SshGatewayRoute.IsThroughGateway(server, groupDefaults: null));
    }

    [Fact]
    public void AGatewayInheritedFromAnAncestorGroup_IsTheRoute()
    {
        ServerProfileDto server = Profile("RDP", group: "PROD/Linux");

        Assert.Equal(GroupGateway, SshGatewayRoute.GatewayIdFor(server, GroupDefaults("PROD")));
    }

    [Fact]
    public void UseDirectConnection_WinsOverAnInheritedGateway()
    {
        ServerProfileDto server = Profile("RDP", group: "PROD");
        server.UseDirectConnection = true;

        Assert.Null(SshGatewayRoute.GatewayIdFor(server, GroupDefaults("PROD")));
    }

    [Fact]
    public void TheProfileGateway_WinsOverTheInheritedOne()
    {
        ServerProfileDto server = Profile("SSH", gatewayId: OwnGateway, group: "PROD");

        Assert.Equal(OwnGateway, SshGatewayRoute.GatewayIdFor(server, GroupDefaults("PROD")));
    }

    [Fact]
    public void ABlankProfileGateway_StillInheritsTheGroupOne()
    {
        ServerProfileDto server = Profile("SSH", gatewayId: "  ", group: "PROD");

        Assert.Equal(GroupGateway, SshGatewayRoute.GatewayIdFor(server, GroupDefaults("PROD")));
    }

    [Fact]
    public void AGroupWithoutAGateway_LeavesTheProfileDirect()
    {
        ServerProfileDto server = Profile("SSH", group: "PROD");
        Dictionary<string, GroupDefaultsDto> defaults = new()
        {
            ["PROD"] = new GroupDefaultsDto { SshUsername = "admin" },
        };

        Assert.Null(SshGatewayRoute.GatewayIdFor(server, defaults));
    }

    [Fact]
    public void AnotherGroupsGateway_IsNotInherited()
    {
        ServerProfileDto server = Profile("SSH", group: "LAB");

        Assert.Null(SshGatewayRoute.GatewayIdFor(server, GroupDefaults("PROD")));
    }

    [Theory]
    [InlineData("VNC")]
    [InlineData("TELNET")]
    [InlineData("FTP")]
    [InlineData("LOCAL")]
    [InlineData("CITRIX")]
    public void AProtocolWhoseHandlerNeverTunnels_GoesDirect_WhateverItNames(string connectionType)
    {
        ServerProfileDto own = Profile(connectionType, gatewayId: OwnGateway);
        ServerProfileDto inherited = Profile(connectionType, group: "PROD");

        Assert.Null(SshGatewayRoute.GatewayIdFor(own, groupDefaults: null));
        Assert.Null(SshGatewayRoute.GatewayIdFor(inherited, GroupDefaults("PROD")));
    }

    [Theory]
    [InlineData("RDP")]
    [InlineData("ssh")]
    [InlineData("SFTP")]
    [InlineData("WINRM")]
    [InlineData("WEB")]
    public void EveryProtocolWhoseHandlerTunnels_FollowsTheGateway(string connectionType)
    {
        Assert.Equal(OwnGateway, SshGatewayRoute.GatewayIdFor(Profile(connectionType, gatewayId: OwnGateway), null));
    }

    [Fact]
    public void ApplyingTheGroupDefaultsFirst_GivesTheSameAnswer()
    {
        Dictionary<string, GroupDefaultsDto> defaults = GroupDefaults("PROD");
        ServerProfileDto[] cases =
        [
            Profile("SSH", group: "PROD"),
            Profile("SSH", gatewayId: "  ", group: "PROD"),
            Profile("SSH", gatewayId: OwnGateway, group: "PROD"),
            Profile("VNC", group: "PROD"),
            Direct(Profile("RDP", group: "PROD")),
        ];

        foreach (ServerProfileDto server in cases)
        {
            string? before = SshGatewayRoute.GatewayIdFor(server, defaults);
            GroupDefaultsDto.ApplyInherited(server, defaults);

            Assert.Equal(before, SshGatewayRoute.GatewayIdFor(server, groupDefaults: null));
        }
    }

    private static ServerProfileDto Profile(string connectionType, string? gatewayId = null, string? group = null)
        => new() { Id = "s", ConnectionType = connectionType, SshGatewayId = gatewayId, Group = group };

    private static ServerProfileDto Direct(ServerProfileDto server)
    {
        server.UseDirectConnection = true;
        return server;
    }

    private static Dictionary<string, GroupDefaultsDto> GroupDefaults(string group)
        => new() { [group] = new GroupDefaultsDto { SshGatewayId = GroupGateway } };
}
