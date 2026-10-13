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

using System.Diagnostics;
using Heimdall.App.Services;
using Heimdall.App.Services.Handlers;
using Heimdall.Core.Configuration;
using Heimdall.Core.Localization;
using Heimdall.Core.Models;
using Heimdall.Core.StateMachine;

namespace Heimdall.App.Tests;

/// <summary>
/// A web console reached through an SSH gateway: one SOCKS tunnel per gateway, and a browser
/// environment that goes through it with the console's real name.
/// </summary>
public sealed class WebThroughGatewayTests
{
    [Fact]
    public async Task AConsoleBehindAGateway_OpensTheGatewaysSocksTunnel()
    {
        var tunnels = new RecordingTunnelService();
        var ports = new WebGatewayProxyPorts(() => 41001);
        WebHandler handler = CreateHandler(tunnels, ports);
        ServerProfileDto profile = Profile(gatewayId: "gw-1");

        ConnectionResult result = await handler.ConnectAsync(profile, new AppSettings(), CancellationToken.None);

        Assert.True(result.Success);
        WebSessionResult session = Assert.IsType<WebSessionResult>(result.Session);
        Assert.Equal("gw-1", session.GatewayId);
        Assert.Equal(41001, session.ProxyPort);

        // The browser goes through SOCKS with the console's real name; the tunnel's own target
        // is a constant, so every console behind this gateway reuses the same tunnel.
        Assert.Equal("https://ilo.example.test/", session.Address.AbsoluteUri);
        (ServerProfileDto tunnelProfile, int remotePort) = Assert.Single(tunnels.Requests);
        Assert.Equal(WebHandler.GatewayTunnelTargetHost, tunnelProfile.RemoteServer);
        Assert.Equal(41001, tunnelProfile.SocksProxyPort);
        Assert.Equal(DefaultPorts.Ssh, remotePort);
        Assert.Equal(profile.Id, tunnelProfile.Id);
        Assert.Equal("gw-1", tunnelProfile.SshGatewayId);

        // The profile itself is left alone: the tunnel works on a copy.
        Assert.Equal("ilo.example.test", profile.RemoteServer);
        Assert.Equal(0, profile.SocksProxyPort);
    }

    [Fact]
    public async Task TwoConsolesBehindOneGateway_AskForTheSameTunnel()
    {
        var tunnels = new RecordingTunnelService();
        int allocations = 0;
        var ports = new WebGatewayProxyPorts(() => 41000 + ++allocations);
        WebHandler handler = CreateHandler(tunnels, ports);

        await handler.ConnectAsync(Profile(gatewayId: "gw-1", id: "a", host: "ilo-a.example.test"), new AppSettings(), CancellationToken.None);
        await handler.ConnectAsync(Profile(gatewayId: "gw-1", id: "b", host: "ilo-b.example.test"), new AppSettings(), CancellationToken.None);

        Assert.Equal(1, allocations);
        Assert.Equal(2, tunnels.Requests.Count);
        Assert.All(tunnels.Requests, request =>
        {
            Assert.Equal(WebHandler.GatewayTunnelTargetHost, request.Profile.RemoteServer);
            Assert.Equal(41001, request.Profile.SocksProxyPort);
            Assert.Equal(DefaultPorts.Ssh, request.RemotePort);
        });
    }

    [Fact]
    public void EachGateway_GetsItsOwnPort_StableForTheRun()
    {
        int next = 50000;
        var ports = new WebGatewayProxyPorts(() => ++next);

        int first = ports.GetOrAllocate("gw-1");
        int second = ports.GetOrAllocate("gw-2");

        Assert.NotEqual(first, second);
        Assert.Equal(first, ports.GetOrAllocate("gw-1"));
    }

    [Fact]
    public async Task AFailedTunnel_OpensNothing()
    {
        var tunnels = new RecordingTunnelService { Fail = "Gateway refused the key" };
        WebHandler handler = CreateHandler(tunnels, new WebGatewayProxyPorts(() => 41001));

        ConnectionResult result = await handler.ConnectAsync(Profile(gatewayId: "gw-1"), new AppSettings(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Null(result.Session);
        Assert.Equal("Gateway refused the key", result.ErrorMessage);
    }

    [Fact]
    public async Task ADirectConsole_OpensNoTunnel()
    {
        var tunnels = new RecordingTunnelService();
        WebHandler handler = CreateHandler(tunnels, new WebGatewayProxyPorts(() => 41001));
        ServerProfileDto profile = Profile(gatewayId: "gw-1");
        profile.UseDirectConnection = true;

        ConnectionResult result = await handler.ConnectAsync(profile, new AppSettings(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Empty(tunnels.Requests);
        WebSessionResult session = Assert.IsType<WebSessionResult>(result.Session);
        Assert.Null(session.ProxyPort);
        Assert.Null(session.GatewayId);
    }

    /// <summary>An external program would not go through the tunnel; refusing says so.</summary>
    [Fact]
    public async Task OpeningExternally_ThroughAGateway_IsRefusedBeforeAnything()
    {
        var tunnels = new RecordingTunnelService();
        int launches = 0;
        var handler = new WebHandler(
            Initialized(),
            new LocalizationManager(),
            _ => { launches++; return null; },
            tunnels,
            new WebGatewayProxyPorts(() => 41001));
        ServerProfileDto profile = Profile(gatewayId: "gw-1");
        profile.WebOpenExternally = true;

        ConnectionResult result = await handler.ConnectAsync(profile, new AppSettings(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("ErrorWebExternalWithGateway", result.ErrorMessage);
        Assert.Empty(tunnels.Requests);
        Assert.Equal(0, launches);
    }

    [Theory]
    [InlineData(null, null, "")]
    [InlineData("gw-1", null, "")]
    [InlineData(null, 41001, "")]
    [InlineData("gw-1", 41001, "gw-1:41001")]
    public void TheEnvironment_IsSharedPerRoute(string? gatewayId, int? port, string expected)
    {
        Assert.Equal(expected, WebView2Helper.WebConsoleRouteKey(gatewayId, port));
    }

    [Theory]
    [InlineData(null, null, "WebView2-Web")]
    [InlineData("2f1c0d3e-aaaa-bbbb-cccc-0123456789ab", 41001, "WebView2-Web-gw-2f1c0d3e-aaaa-bbbb-cccc-0123456789ab")]
    [InlineData(@"..\..\evil", 41001, "WebView2-Web-gw-evil")]
    [InlineData(@"\/:*", 41001, "WebView2-Web-gw")]
    public void AGatewaysProfileFolder_StaysInsideTheDataDirectory(string? gatewayId, int? port, string expected)
    {
        Assert.Equal(expected, WebView2Helper.WebConsoleProfileFolderFor(gatewayId, port));
    }

    [Fact]
    public void TheGatewayBrowser_GoesThroughTheSocksProxy()
    {
        Assert.Equal("--proxy-server=socks5://127.0.0.1:41001", WebView2Helper.WebConsoleBrowserArguments(41001));
        Assert.Null(WebView2Helper.WebConsoleBrowserArguments(null));
    }

    [Fact]
    public void TheTab_OffersAWayOutOnlyForADirectConsole()
    {
        var direct = new WebSessionResult("a", new Uri("https://ilo.example.test/"));
        var viaGateway = new WebSessionResult("b", new Uri("https://ilo.example.test/")) { GatewayId = "gw-1", ProxyPort = 41001 };

        Assert.True(direct.CanOpenExternally);
        Assert.False(viaGateway.CanOpenExternally);
    }

    [Fact]
    public void OpeningFromTheTab_UsesTheConsolesCurrentPage()
    {
        var session = new WebSessionResult("a", new Uri("https://ilo.example.test/"));

        var info = WebExternalLaunch.TryCreateForSession(session, "https://ilo.example.test/html/summary.html", out string? error);

        Assert.Null(error);
        Assert.Equal("https://ilo.example.test/html/summary.html", info!.FileName);
    }

    /// <summary>A page the console redirected elsewhere is not handed to the program.</summary>
    [Theory]
    [InlineData("https://sso.example.test/login")]
    [InlineData("http://ilo.example.test/")]
    [InlineData("https://ilo.example.test:8443/")]
    [InlineData("about:blank")]
    [InlineData(null)]
    public void OpeningFromTheTab_FallsBackToTheProfileAddressOffTheConsole(string? currentPage)
    {
        var session = new WebSessionResult("a", new Uri("https://ilo.example.test/index.html"));

        var info = WebExternalLaunch.TryCreateForSession(session, currentPage, out _);

        Assert.Equal("https://ilo.example.test/index.html", info!.FileName);
    }

    [Fact]
    public void OpeningFromTheTab_UsesTheProfilesProgramAndArguments()
    {
        var session = new WebSessionResult("a", new Uri("https://ilo.example.test:8443/"))
        {
            ExternalProgram = @"C:\Legacy\firefox.exe",
            ExternalArguments = "{Url} {Host}:{Port}",
            DisplayName = "iLO",
        };

        var info = WebExternalLaunch.TryCreateForSession(session, null, out _);

        Assert.Equal(@"C:\Legacy\firefox.exe", info!.FileName);
        Assert.Equal("https://ilo.example.test:8443/ ilo.example.test:8443", info.Arguments);
    }

    [Fact]
    public void OpeningFromTheTab_StillRefusesACommandInterpreter()
    {
        var session = new WebSessionResult("a", new Uri("https://ilo.example.test/")) { ExternalProgram = "cmd.exe" };

        Assert.Null(WebExternalLaunch.TryCreateForSession(session, null, out string? error));
        Assert.Equal("ErrorWebExternalShellRefused", error);
    }

    /// <summary>
    /// A web console behind a gateway goes through a SOCKS proxy: the dialog used to describe,
    /// validate and offer to edit a forwarded local port that does not exist for it.
    /// </summary>
    [Fact]
    public void TheDialog_DescribesTheSocksRouteForAWebConsole()
    {
        var dto = new ServerProfileDto
        {
            Id = "w",
            DisplayName = "iLO",
            ConnectionType = "WEB",
            RemoteServer = "ilo.example.test",
            SshGatewayId = "gw-1",
            UseDirectConnection = false,
        };

        Heimdall.App.ViewModels.Dialogs.ServerDialogViewModel vm = Heimdall.App.ViewModels.Dialogs.ServerDialogViewModel.FromDto(dto);

        Assert.True(vm.UsesGateway);
        Assert.False(vm.UsesLocalTunnelPort);
        Assert.False(vm.CanEditTunnelPort);
        Assert.Equal("ServerDialogClientNodeSocks", vm.ClientNodeCaption);
        Assert.Equal("ServerDialogTunnelSummarySocks", vm.TunnelSummary);

        dto.ConnectionType = "RDP";
        Heimdall.App.ViewModels.Dialogs.ServerDialogViewModel rdp = Heimdall.App.ViewModels.Dialogs.ServerDialogViewModel.FromDto(dto);
        Assert.True(rdp.UsesLocalTunnelPort);
    }

    /// <summary>
    /// Every reason reads as a sentence in every language: the raw status names, "Unknown" for a
    /// refused connection, told the user nothing.
    /// </summary>
    [Theory]
    [InlineData("en")]
    [InlineData("fr")]
    [InlineData("es")]
    public async Task EveryErrorReason_IsTranslated(string locale)
    {
        var localizer = new LocalizationManager();
        await localizer.LoadAsync(System.IO.Path.Combine(AppContext.BaseDirectory, "locales"), locale);

        Assert.All(WebConsolePolicy.ErrorReasonKeys, key => Assert.True(localizer.HasKey(key), $"{key} missing in {locale}"));
    }

    [Theory]
    [InlineData("Unknown", "WebErrorNoAnswer")]
    [InlineData("CannotConnect", "WebErrorNoAnswer")]
    [InlineData("ConnectionReset", "WebErrorNoAnswer")]
    [InlineData("HostNameNotResolved", "WebErrorNameNotResolved")]
    [InlineData("Timeout", "WebErrorTimeout")]
    [InlineData("CertificateExpired", "WebErrorCertificate")]
    [InlineData("ValidProxyAuthenticationRequired", "WebErrorProxy")]
    [InlineData("RedirectFailed", "WebErrorOther")]
    [InlineData(null, "WebErrorOther")]
    public void ABrowserStatus_ReadsAsAReason(string? status, string expected)
    {
        string key = WebConsolePolicy.ErrorReasonKey(status);

        Assert.Equal(expected, key);
        Assert.Contains(key, WebConsolePolicy.ErrorReasonKeys);
    }

    private static WebHandler CreateHandler(ITunnelService tunnels, WebGatewayProxyPorts ports)
        => new(Initialized(), new LocalizationManager(), _ => throw new InvalidOperationException("no launch expected"), tunnels, ports);

    /// <summary>The connect path moves rows to Initializing before any handler runs.</summary>
    private static ConnectionStateMachine Initialized()
    {
        var stateMachine = new ConnectionStateMachine();
        foreach (string id in new[] { "web-test", "a", "b" })
        {
            Assert.True(stateMachine.TryTransition(id, ConnectionState.Initializing));
        }

        return stateMachine;
    }

    private static ServerProfileDto Profile(string gatewayId, string id = "web-test", string host = "ilo.example.test") => new()
    {
        Id = id,
        DisplayName = "iLO",
        ConnectionType = "WEB",
        RemoteServer = host,
        WebPort = DefaultPorts.HttpsStd,
        SshGatewayId = gatewayId,
        UseDirectConnection = false,
    };

    private sealed class RecordingTunnelService : ITunnelService
    {
        public List<(ServerProfileDto Profile, int RemotePort)> Requests { get; } = [];

        public string? Fail { get; init; }

        public Task<TunnelSetupOutcome> SetupTunnelIfNeededAsync(
            ServerProfileDto server,
            int remotePort,
            AppSettings settings,
            CancellationToken ct,
            bool preferDistinctLoopback = false)
        {
            Requests.Add((server, remotePort));
            return Task.FromResult(Fail is null
                ? new TunnelSetupOutcome(true, true, "127.0.0.1", 52000, null, null)
                : new TunnelSetupOutcome(false, false, string.Empty, 0, Fail, null));
        }

        public void UpdateSettings(AppSettings settings)
        {
        }

        public Heimdall.Ssh.TunnelForwardedPortFailure? GetRecentForwardedPortFailure(int localPort) => null;

        public void ReleaseTunnelReference(int localPort)
        {
        }
    }
}
