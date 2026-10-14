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

using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using Heimdall.App.Services;
using Heimdall.App.Services.Handlers;
using Heimdall.Core.Configuration;
using Heimdall.Core.Localization;
using Heimdall.Core.Models;
using Heimdall.Core.Ssh;
using Heimdall.Core.StateMachine;
using Heimdall.Ssh;
using Renci.SshNet;

namespace Heimdall.App.Tests;

/// <summary>
/// The health monitor and the code that opens the route agree on which profiles go through an
/// SSH gateway.
/// </summary>
/// <remarks>
/// The monitor once marked a profile with a gateway and <see cref="ServerProfileDto.UseDirectConnection"/>
/// as behind its gateway while the connection opened direct. Here the tunnel service is the oracle:
/// its manager fails every tunnel, so a profile it routes comes back failed and one it leaves
/// direct comes back on its own host.
/// </remarks>
public sealed class SshGatewayRouteAgreementTests : IDisposable
{
    private const string GatewayId = "6f3d2b9e-1a4c-4e8b-b7d5-2c9a0e4f8b13";
    private const string TargetHost = "target.example.test";

    private readonly string _keyFilePath;

    public SshGatewayRouteAgreementTests()
    {
        _keyFilePath = Path.Combine(Path.GetTempPath(), $"heimdall-route-{Guid.NewGuid():N}.pem");
        File.WriteAllText(_keyFilePath, "not parsed: the SSH client factory is replaced in these tests");
    }

    public void Dispose()
    {
        try
        {
            File.Delete(_keyFilePath);
        }
        catch (IOException)
        {
        }
    }

    public static TheoryData<string, string, string?, bool, bool> Cases() => new()
    {
        // name, connection type, own gateway, direct flag, expected through the gateway
        { "own gateway", "SSH", GatewayId, false, true },
        { "own gateway, direct", "SSH", GatewayId, true, false },
        { "group gateway", "RDP", null, false, true },
        { "group gateway, direct", "RDP", null, true, false },
        { "own gateway, protocol never tunnels", "VNC", GatewayId, false, false },
        { "group gateway, protocol never tunnels", "TELNET", null, false, false },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task TheMonitorSaysBehindGateway_ExactlyWhenTheTunnelServiceRoutes(
        string name,
        string connectionType,
        string? ownGatewayId,
        bool useDirectConnection,
        bool expectedThroughGateway)
    {
        AppSettings settings = CreateSettings();
        ServerProfileDto server = new()
        {
            Id = "server-" + name,
            ConnectionType = connectionType,
            RemoteServer = TargetHost,
            RemotePort = 22,
            Group = "PROD/Linux",
            SshGatewayId = ownGatewayId,
            UseDirectConnection = useDirectConnection,
        };
        using TunnelManager tunnelManager = CreateManagerThatFails();
        TunnelService tunnelService = new TunnelService(
            tunnelManager,
            new HostKeyStore(),
            new HostKeyTrustService(new HostKeyStore()),
            new ConnectionStateMachine(),
            await LoadLocalizerAsync(),
            RejectingHostKeyVerifier.Instance);

        bool monitorBehindGateway = SessionHealthMonitor.UnprobedReason(server, settings) == "behind-gateway";
        TunnelSetupOutcome outcome = await tunnelService.SetupTunnelIfNeededAsync(
            server,
            22,
            settings,
            CancellationToken.None);
        bool tunnelServiceRouted = !(outcome.Success && outcome.Host == TargetHost);

        Assert.Equal(expectedThroughGateway, monitorBehindGateway);
        Assert.Equal(expectedThroughGateway, tunnelServiceRouted);
    }

    [Fact]
    public void TheProtocolsThatTunnel_AreExactlyTheHandlersHoldingTheTunnelService()
    {
        // The shared rule names the tunnelling protocols through ProtocolCapabilities. A handler
        // that starts or stops dialling the tunnel without that list following would put the
        // monitor and the connection out of step again.
        Type[] handlerTypes = typeof(WebHandler).Assembly.GetTypes()
            .Where(type => typeof(IProtocolHandler).IsAssignableFrom(type) && type is { IsClass: true, IsAbstract: false })
            .ToArray();

        Assert.NotEmpty(handlerTypes);
        foreach (Type handlerType in handlerTypes)
        {
            IProtocolHandler handler = (IProtocolHandler)RuntimeHelpers.GetUninitializedObject(handlerType);
            bool holdsTunnelService = handlerType
                .GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .Any(ctor => ctor.GetParameters().Any(p => p.ParameterType == typeof(ITunnelService)));

            Assert.True(
                holdsTunnelService == ProtocolCapabilities.SupportsSshGateway(handler.Protocol),
                $"{handlerType.Name} ({handler.Protocol}): holds the tunnel service = {holdsTunnelService}");
        }
    }

    private AppSettings CreateSettings()
    {
        AppSettings settings = new()
        {
            SshGateways =
            [
                new SshGatewayDto
                {
                    Id = GatewayId,
                    Name = "Bastion",
                    Host = "gw.example.test",
                    Port = 22,
                    User = "ssh-user",
                    KeyPath = _keyFilePath,
                },
            ],
        };
        settings.GroupDefaults["PROD"] = new GroupDefaultsDto { SshGatewayId = GatewayId };
        return settings;
    }

    private static async Task<LocalizationManager> LoadLocalizerAsync()
    {
        LocalizationManager localizer = new LocalizationManager();
        await localizer.LoadAsync(Path.Combine(AppContext.BaseDirectory, "locales"), "en");
        return localizer;
    }

    private static TunnelManager CreateManagerThatFails()
    {
        return new TunnelManager(
            static (connectionParams, verificationHost, verificationPort, hostKeyStore, verifier, cancellationToken) =>
                Task.FromResult(new PinnedFingerprintVerifier(verificationHost, verificationPort, "SHA256:pinned")),
            static connectionParams => new SshClient(new ConnectionInfo(
                connectionParams.Host,
                connectionParams.Port,
                connectionParams.Username,
                new NoneAuthenticationMethod(connectionParams.Username))),
            static (client, verificationHost, verificationPort, pinnedVerifier, cancellationToken, cancelLogMessage) =>
                throw new OperationCanceledException());
    }
}
