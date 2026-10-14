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
using Heimdall.TestSupport;

namespace Heimdall.Core.Tests;

/// <summary>
/// Pins the two properties every "connection refused" test relies on: a connect to the reserved
/// port is refused, and nothing else can bind it while it is held. The helper source is shared
/// with the App tests, so it is proven once here.
/// </summary>
public sealed class RefusedLoopbackPortTests
{
    [Fact]
    public async Task AConnectToTheReservedPort_IsRefused()
    {
        using RefusedLoopbackPort refused = RefusedLoopbackPort.Reserve();
        using Socket client = new(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

        SocketException failure = await Assert.ThrowsAsync<SocketException>(
            () => client.ConnectAsync(IPAddress.Loopback, refused.Port));

        Assert.Equal(SocketError.ConnectionRefused, failure.SocketErrorCode);
    }

    [Fact]
    public void TheReservedPort_CannotBeBoundWhileHeld()
    {
        using RefusedLoopbackPort refused = RefusedLoopbackPort.Reserve();

        using Socket plainChallenger = new(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        AssertBindIsRefused(plainChallenger, refused.Port);

        using Socket reuseChallenger = new(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        reuseChallenger.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        AssertBindIsRefused(reuseChallenger, refused.Port);
    }

    /// <summary>
    /// The positive control for the test above: once the reservation is released, the same bind
    /// succeeds. Without it, a challenger that failed for an unrelated reason would read as proof.
    /// </summary>
    [Fact]
    public void TheReservedPort_CanBeBoundOnceReleased()
    {
        RefusedLoopbackPort refused = RefusedLoopbackPort.Reserve();
        int port = refused.Port;
        refused.Dispose();

        using Socket successor = new(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        successor.Bind(new IPEndPoint(IPAddress.Loopback, port));

        Assert.Equal(port, ((IPEndPoint)successor.LocalEndPoint!).Port);
    }

    private static void AssertBindIsRefused(Socket challenger, int port)
    {
        SocketException refusal = Assert.Throws<SocketException>(
            () => challenger.Bind(new IPEndPoint(IPAddress.Loopback, port)));

        // Measured on Windows with .NET 10: a plain bind answers AddressAlreadyInUse and a
        // ReuseAddress bind answers AccessDenied. Naming both keeps an unrelated socket failure
        // from passing as proof.
        Assert.Contains(
            refusal.SocketErrorCode,
            new[] { SocketError.AddressAlreadyInUse, SocketError.AccessDenied });
    }
}
