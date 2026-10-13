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

using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace Heimdall.App.Services;

/// <summary>
/// The local SOCKS port each SSH gateway serves web consoles on, fixed for the run.
/// </summary>
/// <remarks>
/// <para>
/// A web console behind a gateway is reached through a SOCKS proxy the gateway's tunnel opens,
/// and the browser is told that proxy when its environment is created. The environment lives
/// as long as the application, so the port it was told must stay the gateway's port for as long:
/// a tunnel closed with its last tab and opened again with the next one binds the same port, and
/// the browser finds its proxy where it left it.
/// </para>
/// <para>
/// One port per gateway, not per console: every console behind one gateway shares one tunnel,
/// counted by reference, instead of each binding a proxy of its own on a port the others hold.
/// </para>
/// </remarks>
public sealed class WebGatewayProxyPorts
{
    private readonly ConcurrentDictionary<string, int> _ports = new(StringComparer.Ordinal);
    private readonly Func<int> _allocate;

    public WebGatewayProxyPorts()
        : this(AllocateEphemeralLoopbackPort)
    {
    }

    internal WebGatewayProxyPorts(Func<int> allocate)
    {
        _allocate = allocate;
    }

    /// <summary>The port this gateway's web consoles go through, allocated on first use.</summary>
    public int GetOrAllocate(string gatewayId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gatewayId);
        return _ports.GetOrAdd(gatewayId, _ => _allocate());
    }

    /// <summary>
    /// A loopback port free at this instant. The tunnel binds it moments later; another program
    /// taking it in between makes the tunnel fail with a port-in-use error, which is reported.
    /// </summary>
    private static int AllocateEphemeralLoopbackPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
