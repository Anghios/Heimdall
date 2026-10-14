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
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Heimdall.Core.SessionHealth;

namespace Heimdall.Core.Tests;

/// <summary>
/// The web console probe, against real servers on the loopback interface.
/// </summary>
public sealed class HttpHealthProbeTests
{
    private const int TimeoutMs = 5000;

    [Theory]
    [InlineData("200 OK")]
    [InlineData("401 Unauthorized")]
    [InlineData("403 Forbidden")]
    [InlineData("404 Not Found")]
    public async Task AnyAnswerBelow500_MeansTheConsoleIsThere(string status)
    {
        await using LoopbackServer server = LoopbackServer.Answering(status);
        using HttpHealthProbe probe = new();

        HealthState state = await probe.ProbeAsync(server.HttpAddress, TimeoutMs, CancellationToken.None);

        Assert.Equal(HealthStatus.Up, state.Status);
        Assert.True(state.LatencyMs > 0);
        Assert.Null(state.Reason);
    }

    [Theory]
    [InlineData("500 Internal Server Error", 500)]
    [InlineData("503 Service Unavailable", 503)]
    public async Task AServerError_MeansDownWithItsCode(string status, int code)
    {
        await using LoopbackServer server = LoopbackServer.Answering(status);
        using HttpHealthProbe probe = new();

        HealthState state = await probe.ProbeAsync(server.HttpAddress, TimeoutMs, CancellationToken.None);

        Assert.Equal(HealthStatus.Down, state.Status);
        Assert.True(HttpHealthProbe.TryParseHttpErrorReason(state.Reason, out int parsed));
        Assert.Equal(code, parsed);
    }

    /// <summary>A redirect is an answer; following it would contact a host nobody configured.</summary>
    [Fact]
    public async Task ARedirect_IsAnAnswerAndIsNotFollowed()
    {
        await using LoopbackServer server = LoopbackServer.Answering(
            "302 Found",
            "Location: http://127.0.0.1:1/elsewhere\r\n");
        using HttpHealthProbe probe = new();

        HealthState state = await probe.ProbeAsync(server.HttpAddress, TimeoutMs, CancellationToken.None);

        Assert.Equal(HealthStatus.Up, state.Status);
        Assert.Single(server.Requests);
    }

    /// <summary>The probe carries nothing a forged server could take.</summary>
    [Fact]
    public async Task TheRequest_CarriesNoCredentialsAndNoCookies()
    {
        await using LoopbackServer server = LoopbackServer.Answering(
            "200 OK",
            "Set-Cookie: session=abc\r\n");
        using HttpHealthProbe probe = new();

        await probe.ProbeAsync(server.HttpAddress, TimeoutMs, CancellationToken.None);
        await probe.ProbeAsync(server.HttpAddress, TimeoutMs, CancellationToken.None);

        Assert.Equal(2, server.Requests.Count);
        Assert.All(server.Requests, request =>
        {
            Assert.StartsWith("GET / HTTP/1.1", request, StringComparison.Ordinal);
            Assert.DoesNotContain("Authorization:", request, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Cookie:", request, StringComparison.OrdinalIgnoreCase);
        });
    }

    /// <summary>The handler the probe really sends through, not a fresh copy of the recipe.</summary>
    [Fact]
    public void TheHandler_GoesDirectFollowsNothingAndReadsNoBody()
    {
        using HttpHealthProbe probe = new();
        SocketsHttpHandler handler = Assert.IsType<SocketsHttpHandler>(probe.Handler);

        Assert.False(handler.UseProxy);
        Assert.False(handler.AllowAutoRedirect);
        Assert.False(handler.UseCookies);
        Assert.False(handler.PreAuthenticate);
        Assert.Null(handler.Credentials);
        Assert.Equal(TimeSpan.Zero, handler.PooledConnectionLifetime);
        Assert.Equal(0, handler.MaxResponseDrainSize);
        Assert.Equal(TimeSpan.Zero, handler.ResponseDrainTimeout);
    }

    /// <summary>The caller stopping the cycle is not the console timing out.</summary>
    [Fact]
    public async Task TheCallerCancelling_IsNotATimeout()
    {
        await using LoopbackServer server = LoopbackServer.Silent();
        using HttpHealthProbe probe = new();
        using CancellationTokenSource cancel = new(TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => probe.ProbeAsync(server.HttpAddress, TimeoutMs, cancel.Token));
    }

    [Fact]
    public async Task AnUnknownName_IsADnsFailure()
    {
        using HttpHealthProbe probe = new();

        HealthState state = await probe.ProbeAsync(new Uri("http://heimdall-probe-test.invalid/"), TimeoutMs, CancellationToken.None);

        Assert.Equal(HealthStatus.Down, state.Status);
        Assert.Equal("dns", state.Reason);
    }

    [Fact]
    public async Task AClosedPort_IsRefused()
    {
        int port = LoopbackServer.FreePort();
        using HttpHealthProbe probe = new();

        HealthState state = await probe.ProbeAsync(new Uri($"http://127.0.0.1:{port}/"), TimeoutMs, CancellationToken.None);

        Assert.Equal(HealthStatus.Down, state.Status);
        Assert.Equal("refused", state.Reason);
    }

    /// <summary>A console that accepts and never answers is down, not probing forever.</summary>
    [Fact]
    public async Task ASilentServer_TimesOut()
    {
        await using LoopbackServer server = LoopbackServer.Silent();
        using HttpHealthProbe probe = new();

        HealthState state = await probe.ProbeAsync(server.HttpAddress, 300, CancellationToken.None);

        Assert.Equal(HealthStatus.Down, state.Status);
        Assert.Equal("timeout", state.Reason);
    }

    /// <summary>
    /// The consoles this is for present self-signed certificates; the probe checks reachability,
    /// not trust, which the web tab decides.
    /// </summary>
    [Fact]
    public async Task ASelfSignedConsole_IsUp()
    {
        await using LoopbackServer server = LoopbackServer.AnsweringOverTls("200 OK");
        using HttpHealthProbe probe = new();

        HealthState state = await probe.ProbeAsync(server.HttpsAddress, TimeoutMs, CancellationToken.None);

        Assert.Equal(HealthStatus.Up, state.Status);
        Assert.Null(state.Reason);
        Assert.Single(server.Requests);
    }

    /// <summary>
    /// A port that answers something the system refuses as TLS still answers: up, with a reason,
    /// rather than down.
    /// </summary>
    [Fact]
    public async Task ARefusedHandshake_IsUpWithAReason()
    {
        await using LoopbackServer server = LoopbackServer.Garbage();
        using HttpHealthProbe probe = new();

        HealthState state = await probe.ProbeAsync(server.HttpsAddress, TimeoutMs, CancellationToken.None);

        Assert.Equal(HealthStatus.Up, state.Status);
        Assert.Equal(HttpHealthProbe.TlsFailedReason, state.Reason);
        Assert.Null(state.LatencyMs);
    }

    /// <summary>
    /// A load balancer with no live member, or a console whose web service died behind its
    /// listener, accepts the connection and closes it during the handshake: that is down.
    /// </summary>
    [Fact]
    public async Task AHandshakeCutShort_IsDown()
    {
        await using LoopbackServer server = LoopbackServer.ClosingAtOnce();
        using HttpHealthProbe probe = new();

        HealthState state = await probe.ProbeAsync(server.HttpsAddress, TimeoutMs, CancellationToken.None);

        Assert.Equal(HealthStatus.Down, state.Status);
        Assert.NotEqual(HttpHealthProbe.TlsFailedReason, state.Reason);
    }

    [Theory]
    [InlineData("http-503", true, 503)]
    [InlineData("http-500", true, 500)]
    [InlineData("http-404", false, 0)]
    [InlineData("http-", false, 0)]
    [InlineData("http-50x", false, 0)]
    [InlineData("timeout", false, 0)]
    [InlineData(null, false, 0)]
    public void TheErrorCode_IsReadBackOnlyFromAServerErrorTag(string? reason, bool expected, int code)
    {
        Assert.Equal(expected, HttpHealthProbe.TryParseHttpErrorReason(reason, out int parsed));
        Assert.Equal(code, parsed);
    }

    /// <summary>
    /// A handshake the system refused is told from one the peer cut short by the transport error
    /// beneath it, wherever the handler wraps it.
    /// </summary>
    [Fact]
    public void ARefusalIsToldFromAClosedConnection()
    {
        static HttpRequestException Secure(Exception inner) =>
            new(HttpRequestError.SecureConnectionError, "tls", inner);

        Assert.True(HttpHealthProbe.IsTlsRefusal(Secure(new AuthenticationException("refused"))));
        Assert.True(HttpHealthProbe.IsTlsRefusal(Secure(new AuthenticationException("alert", new System.ComponentModel.Win32Exception(unchecked((int)0x80090326))))));
        Assert.False(HttpHealthProbe.IsTlsRefusal(Secure(new AuthenticationException("closed", new IOException("eof")))));
        Assert.False(HttpHealthProbe.IsTlsRefusal(Secure(new IOException("reset", new SocketException((int)SocketError.ConnectionReset)))));
        Assert.False(HttpHealthProbe.IsTlsRefusal(new HttpRequestException(HttpRequestError.ConnectionError, "x", new AuthenticationException("y"))));
    }

    /// <summary>
    /// A loopback server that answers every connection the same way and keeps what it was asked.
    /// </summary>
    private sealed class LoopbackServer : IAsyncDisposable
    {
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _loop;
        private readonly List<string> _requests = [];

        private LoopbackServer(Func<TcpClient, CancellationToken, Task> handle)
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            int port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            HttpAddress = new Uri($"http://127.0.0.1:{port}/");
            HttpsAddress = new Uri($"https://127.0.0.1:{port}/");
            _loop = Task.Run(async () =>
            {
                while (!_stop.IsCancellationRequested)
                {
                    TcpClient client;
                    try
                    {
                        client = await _listener.AcceptTcpClientAsync(_stop.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                    catch (SocketException)
                    {
                        return;
                    }

                    _ = Task.Run(async () =>
                    {
                        using (client)
                        {
                            try
                            {
                                await handle(client, _stop.Token);
                            }
                            catch (Exception ex) when (ex is IOException or OperationCanceledException or AuthenticationException or SocketException)
                            {
                                // The probe hung up or the test ended.
                            }
                        }
                    });
                }
            });
        }

        public Uri HttpAddress { get; }

        public Uri HttpsAddress { get; }

        public IReadOnlyList<string> Requests
        {
            get
            {
                lock (_requests)
                {
                    return _requests.ToList();
                }
            }
        }

        public static LoopbackServer Answering(string status, string extraHeaders = "")
        {
            LoopbackServer? server = null;
            server = new LoopbackServer(async (client, ct) =>
                await server!.AnswerAsync(client.GetStream(), status, extraHeaders, ct));
            return server;
        }

        public static LoopbackServer AnsweringOverTls(string status)
        {
            X509Certificate2 certificate = CreateSelfSigned();
            LoopbackServer? server = null;
            server = new LoopbackServer(async (client, ct) =>
            {
                using SslStream tls = new(client.GetStream());
                await tls.AuthenticateAsServerAsync(certificate);
                await server!.AnswerAsync(tls, status, "", ct);
            });
            return server;
        }

        public static LoopbackServer Silent() => new(async (_, ct) => await Task.Delay(Timeout.Infinite, ct));

        public static LoopbackServer Garbage() => new(async (client, ct) =>
        {
            byte[] noise = Encoding.ASCII.GetBytes("this is not a TLS server\r\n");
            await client.GetStream().WriteAsync(noise, ct);
            await Task.Delay(Timeout.Infinite, ct);
        });

        public static LoopbackServer ClosingAtOnce() => new((client, _) =>
        {
            client.Client.Close();
            return Task.CompletedTask;
        });

        public static int FreePort()
        {
            TcpListener listener = new(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        private async Task AnswerAsync(Stream stream, string status, string extraHeaders, CancellationToken ct)
        {
            byte[] buffer = new byte[8192];
            StringBuilder request = new();
            while (!request.ToString().Contains("\r\n\r\n", StringComparison.Ordinal))
            {
                int read = await stream.ReadAsync(buffer, ct);
                if (read == 0)
                {
                    return;
                }

                request.Append(Encoding.ASCII.GetString(buffer, 0, read));
            }

            lock (_requests)
            {
                _requests.Add(request.ToString());
            }

            string response = $"HTTP/1.1 {status}\r\n{extraHeaders}Content-Length: 2\r\nConnection: close\r\n\r\nok";
            await stream.WriteAsync(Encoding.ASCII.GetBytes(response), ct);
            await stream.FlushAsync(ct);
        }

        private static X509Certificate2 CreateSelfSigned()
        {
            using RSA key = RSA.Create(2048);
            CertificateRequest request = new("CN=127.0.0.1", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using X509Certificate2 created = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));

            // SslStream on Windows needs a key it can reach, which an ephemeral one is not.
            return X509CertificateLoader.LoadPkcs12(created.Export(X509ContentType.Pfx), null);
        }

        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync();
            _listener.Stop();
            try
            {
                await _loop;
            }
            catch (Exception ex) when (ex is OperationCanceledException or SocketException or ObjectDisposedException)
            {
                // Stopped.
            }

            _stop.Dispose();
        }
    }
}
