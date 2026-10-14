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

using System.Net;
using System.Net.Sockets;

namespace Heimdall.TestSupport;

/// <summary>
/// A loopback TCP port that refuses every connection for as long as this object is alive.
/// </summary>
/// <remarks>
/// <para>
/// The port is held by a socket that is bound and never put into the listening state. A bound
/// socket accepts nothing, so a connect to it is refused, and while it stays bound no other
/// socket can bind the same endpoint, so nothing can start listening there either. Measured on
/// Windows with .NET 10: the connect fails with <see cref="SocketError.ConnectionRefused"/>,
/// after the same retry delay a closed port costs.
/// </para>
/// <para>
/// This replaces "bind a listener, read its port, stop it". From the moment that listener
/// stops, the port is free, and the test then measures whatever holds it when the connect
/// arrives instead of a refusal. Holding the endpoint makes a refusal certain by
/// construction rather than probable.
/// </para>
/// <para>
/// Destination port 0 is not an alternative: Windows answers it with
/// <see cref="SocketError.AddressNotAvailable"/>, not a refusal, and the handlers reject it as
/// an invalid port before dialling.
/// </para>
/// </remarks>
internal sealed class RefusedLoopbackPort : IDisposable
{
    private readonly Socket _reservation;

    private RefusedLoopbackPort(Socket reservation)
    {
        _reservation = reservation;
        Port = ((IPEndPoint)reservation.LocalEndPoint!).Port;
    }

    /// <summary>The reserved port on <see cref="IPAddress.Loopback"/>.</summary>
    public int Port { get; }

    /// <summary>Binds a fresh loopback port and holds it without listening.</summary>
    public static RefusedLoopbackPort Reserve()
    {
        Socket reservation = new(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        try
        {
            // Set before Bind, which rejects the option afterwards. It declares that the
            // endpoint is not shared; it is not what keeps a ReuseAddress challenger out, since
            // Windows refuses that bind whenever the owner did not opt into reuse.
            reservation.ExclusiveAddressUse = true;
            reservation.Bind(new IPEndPoint(IPAddress.Loopback, 0));

            // Deliberately no Listen call: that is what makes every connect a refusal.
            return new RefusedLoopbackPort(reservation);
        }
        catch
        {
            reservation.Dispose();
            throw;
        }
    }

    /// <summary>Releases the port.</summary>
    public void Dispose() => _reservation.Dispose();
}
