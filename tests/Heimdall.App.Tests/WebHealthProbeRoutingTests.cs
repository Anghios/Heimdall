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
using Heimdall.App.Services;
using Heimdall.Core.Configuration;
using Heimdall.Core.Localization;
using Heimdall.Core.SessionHealth;

namespace Heimdall.App.Tests;

/// <summary>
/// Which probe a profile gets, and what the tooltip says of the web probe's verdicts.
/// </summary>
public sealed class WebHealthProbeRoutingTests
{
    /// <summary>The probe asks for the page the tab opens, path included.</summary>
    [Fact]
    public void AWebProfile_IsProbedAtTheAddressItsTabOpens()
    {
        ServerProfileDto dto = new()
        {
            ConnectionType = "WEB",
            RemoteServer = "ilo.example.test",
            WebPort = 8443,
            WebPath = "/login",
        };

        Assert.Equal(new Uri("https://ilo.example.test:8443/login"), SessionHealthMonitor.ResolveWebProbeAddress(dto));
    }

    [Theory]
    [InlineData("SSH")]
    [InlineData("RDP")]
    [InlineData("")]
    public void OtherTypes_HaveNoWebAddress(string connectionType)
    {
        ServerProfileDto dto = new() { ConnectionType = connectionType, RemoteServer = "host.example.test", WebPort = 443 };

        Assert.Null(SessionHealthMonitor.ResolveWebProbeAddress(dto));
    }

    [Fact]
    public void AWebProfileWithoutAnAddress_FallsBackToTheTcpProbe()
    {
        ServerProfileDto dto = new() { ConnectionType = "WEB", RemoteServer = "http://pasted.example.test/", WebPort = 443 };

        Assert.Null(SessionHealthMonitor.ResolveWebProbeAddress(dto));
    }

    [Fact]
    public async Task AServerError_IsNamedWithItsCode()
    {
        LocalizationManager localizer = await EnglishAsync();
        HealthState state = new(HealthStatus.Down, DateTime.UtcNow, null, "http-503");

        string tooltip = HealthReasonLocalizer.FormatTooltip(state, localizer);

        Assert.Contains(localizer.Format("HealthReasonHttpError", 503), tooltip, StringComparison.Ordinal);
        Assert.Contains("503", tooltip, StringComparison.Ordinal);
    }

    [Fact]
    public void AFailedHandshake_HasItsOwnReason()
    {
        Assert.Equal("HealthReasonTlsFailed", HealthReasonLocalizer.ReasonKey(HttpHealthProbe.TlsFailedReason));
    }

    private static async Task<LocalizationManager> EnglishAsync()
    {
        LocalizationManager localizer = new();
        await localizer.LoadAsync(Path.Combine(AppContext.BaseDirectory, "locales"), "en");
        return localizer;
    }
}

/// <summary>The monitor sends a web console to the web probe and the rest to TCP.</summary>
public partial class SessionHealthMonitorTests
{
    [Fact]
    public async Task AWebProfile_GoesToTheWebProbeAndTheRestToTcp()
    {
        RecordingProbes probes = new();
        await using MonitorFixture fixture = new(
            new FakeConfigManager(
            [
                new ServerProfileDto { Id = "web", ConnectionType = "WEB", RemoteServer = "ilo.example.test", WebPort = 443 },
                new ServerProfileDto { Id = "ssh", ConnectionType = "SSH", RemoteServer = "ssh.example.test", SshPort = 22 },
            ]),
            probes,
            probes);

        await fixture.RunCycleAsync();

        Assert.Equal([new Uri("https://ilo.example.test/")], probes.WebAddresses);
        Assert.Equal(["ssh.example.test:22"], probes.TcpTargets);
        Assert.Equal("http-503", fixture.Monitor.GetState("web")?.Reason);
    }

    /// <summary>Without a web probe, a web console keeps the TCP probe it had.</summary>
    [Fact]
    public async Task WithoutAWebProbe_AWebProfileKeepsTheTcpProbe()
    {
        RecordingProbes probes = new();
        await using MonitorFixture fixture = new(
            new FakeConfigManager(
            [
                new ServerProfileDto { Id = "web", ConnectionType = "WEB", RemoteServer = "ilo.example.test", WebPort = 443 },
            ]),
            probes);

        await fixture.RunCycleAsync();

        Assert.Empty(probes.WebAddresses);
        Assert.Equal(["ilo.example.test:443"], probes.TcpTargets);
    }

    private sealed class RecordingProbes : IHealthProbe, IWebHealthProbe
    {
        private readonly object _lock = new();

        public List<string> TcpTargets { get; } = [];

        public List<Uri> WebAddresses { get; } = [];

        public Task<HealthState> ProbeAsync(string host, int port, int timeoutMs, CancellationToken ct)
        {
            lock (_lock)
            {
                TcpTargets.Add($"{host}:{port}");
            }

            return Task.FromResult(new HealthState(HealthStatus.Up, DateTime.UtcNow, 1, null));
        }

        public Task<HealthState> ProbeAsync(Uri address, int timeoutMs, CancellationToken ct)
        {
            lock (_lock)
            {
                WebAddresses.Add(address);
            }

            return Task.FromResult(new HealthState(HealthStatus.Down, DateTime.UtcNow, null, "http-503"));
        }
    }
}
