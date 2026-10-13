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
using System.Diagnostics;
using System.IO;
using Heimdall.Core.Configuration;
using Heimdall.Core.Localization;
using Heimdall.Core.Models;
using Heimdall.Core.StateMachine;

namespace Heimdall.App.Services.Handlers;

/// <summary>
/// Opens a web console: in an embedded browser tab, or with an external program when the
/// profile asks for one.
/// </summary>
internal sealed class WebHandler : IProtocolHandler
{
    private readonly ConnectionStateMachine _connectionSm;
    private readonly LocalizationManager _localizer;
    private readonly Func<ProcessStartInfo, Process?> _startProcess;

    public WebHandler(ConnectionStateMachine connectionSm, LocalizationManager localizer)
        : this(connectionSm, localizer, Process.Start)
    {
    }

    internal WebHandler(
        ConnectionStateMachine connectionSm,
        LocalizationManager localizer,
        Func<ProcessStartInfo, Process?> startProcess)
    {
        _connectionSm = connectionSm;
        _localizer = localizer;
        _startProcess = startProcess;
    }

    public string Protocol => "WEB";

    public Task<ConnectionResult> ConnectAsync(
        ServerProfileDto server,
        AppSettings settings,
        CancellationToken ct,
        RdpModeOverride rdpModeOverride = RdpModeOverride.UseProfile)
    {
        ArgumentNullException.ThrowIfNull(server);

        _connectionSm.TryTransition(server.Id, ConnectionState.ValidatingConfig);

        if (WebAddress.From(server) is not { } address)
        {
            return Task.FromResult(Fail(server.Id, _localizer["ErrorWebInvalidAddress"]));
        }

        _connectionSm.TryTransition(server.Id, ConnectionState.LaunchingWeb);

        if (server.WebOpenExternally)
        {
            return Task.FromResult(LaunchExternally(server, address));
        }

        string? password = string.IsNullOrEmpty(server.WebPasswordEncrypted)
            ? null
            : ConnectionHelpers.DecryptPassword(server.WebPasswordEncrypted);
        string? username = string.IsNullOrWhiteSpace(server.WebUsername) ? null : server.WebUsername;

        var session = new WebSessionResult(
            server.Id,
            address,
            username,
            password,
            server.SessionLoggingOverride);
        return Task.FromResult(new ConnectionResult(true, null, session));
    }

    private ConnectionResult LaunchExternally(ServerProfileDto server, Uri address)
    {
        ProcessStartInfo? startInfo = WebExternalLaunch.TryCreate(server, address, out string? errorKey);
        if (startInfo is null)
        {
            return Fail(server.Id, _localizer[errorKey!]);
        }

        Process? process;
        try
        {
            process = _startProcess(startInfo);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            Core.Logging.FileLogger.Warn($"Web external launch failed for {server.DisplayName}: {ex.Message}");
            return Fail(server.Id, _localizer.Format("ErrorWebExternalLaunchFailed", ex.Message));
        }

        Core.Logging.FileLogger.Info($"Web console {server.DisplayName} opened externally.");

        // A browser that hands the address to an instance already running returns no process,
        // or one that exits at once: there is nothing left to watch, so the row goes back to idle
        // rather than claiming a client it cannot see.
        if (process is null)
        {
            _connectionSm.TryTransition(server.Id, ConnectionState.LaunchedExternalClient);
            _connectionSm.TryTransition(server.Id, ConnectionState.Disconnected);
            return new ConnectionResult(true, null, null);
        }

        _connectionSm.TryTransition(server.Id, ConnectionState.LaunchedExternalClient);
        try
        {
            process.EnableRaisingEvents = true;
            process.Exited += (_, _) =>
            {
                _connectionSm.TryTransition(server.Id, ConnectionState.Disconnected);
                process.Dispose();
            };
            if (process.HasExited)
            {
                _connectionSm.TryTransition(server.Id, ConnectionState.Disconnected);
            }
        }
        catch (InvalidOperationException)
        {
            _connectionSm.TryTransition(server.Id, ConnectionState.Disconnected);
        }

        return new ConnectionResult(true, null, null);
    }

    private ConnectionResult Fail(string serverId, string message)
    {
        _connectionSm.SetError(serverId, message);
        return new ConnectionResult(false, message, null);
    }
}
