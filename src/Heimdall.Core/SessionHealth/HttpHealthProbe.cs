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
using System.Globalization;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Authentication;

namespace Heimdall.Core.SessionHealth;

/// <summary>
/// Probes a web console by asking for its page, where the TCP probe only opens its port.
/// </summary>
/// <remarks>
/// <para>
/// An open port says nothing of a console that answers every request with an error, which is
/// how a management controller looks while it boots or after its web service has crashed. Any
/// answer below 500 counts as up: a sign-in page, a 401, a redirect or a 404 all come from a
/// console that is there. A 5xx counts as down, with its code in the reason.
/// </para>
/// <para>
/// The probe carries nothing: no credentials, no cookies, and it follows no redirect, so a
/// redirect to another host is never contacted. It goes direct, like the TCP probe, never
/// through the system proxy. It reads the status line and headers; the connection is closed
/// rather than drained, so a body is never read. It asks for the console's root page, not the
/// path the tab opens: the status line of the root tells the same, and a deep link can carry a
/// query or reach a page that acts when it is requested.
/// </para>
/// <para>
/// The certificate is not checked. The probe sends nothing a forged server could take, and the
/// consoles this is for present self-signed certificates the user has pinned, or will pin, in
/// the web tab, which is where trust is decided. A console whose TLS the system refuses, an old
/// controller speaking TLS 1.0, or a port that answers something other than TLS, is reported up
/// with a reason: something answered, and the tab has its own way out for it. A handshake the
/// peer cuts short, a closed or reset connection, is down: that is how a load balancer with no
/// live member, or a console whose web service has died behind its listener, looks.
/// </para>
/// </remarks>
public sealed class HttpHealthProbe : IWebHealthProbe, IDisposable
{
    /// <summary>The reason tag for a console whose TLS handshake failed.</summary>
    public const string TlsFailedReason = "tls-failed";

    /// <summary>The prefix of the reason tag for a server error; the status code follows.</summary>
    public const string HttpErrorReasonPrefix = "http-";

    /// <summary>The first status code that counts as the console being down.</summary>
    internal const int ServerErrorFloor = 500;

    /// <summary>The product token the probe announces itself with.</summary>
    private const string UserAgentProduct = "Heimdall-HealthProbe";

    private readonly HttpClient _client;

    public HttpHealthProbe()
        : this(CreateHandler())
    {
    }

    internal HttpHealthProbe(HttpMessageHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        Handler = handler;
        // The deadline is the caller's, through the cancellation token.
        _client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        _client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgentProduct);
    }

    /// <summary>The handler the probe sends through.</summary>
    internal HttpMessageHandler Handler { get; }

    /// <inheritdoc />
    public async Task<HealthState> ProbeAsync(Uri address, int timeoutMs, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(address);

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linked.CancelAfter(timeoutMs);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, address);
            using HttpResponseMessage response = await _client
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linked.Token)
                .ConfigureAwait(false);
            stopwatch.Stop();

            return Verdict((int)response.StatusCode, (int)Math.Max(1, stopwatch.ElapsedMilliseconds));
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            return new HealthState(HealthStatus.Down, DateTime.UtcNow, null, "timeout");
        }
        catch (HttpRequestException ex) when (IsTlsRefusal(ex))
        {
            return new HealthState(HealthStatus.Up, DateTime.UtcNow, null, TlsFailedReason);
        }
        catch (HttpRequestException ex)
        {
            return new HealthState(HealthStatus.Down, DateTime.UtcNow, null, ReasonFor(ex));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new HealthState(HealthStatus.Down, DateTime.UtcNow, null, TcpHealthProbe.OtherReason);
        }
    }

    /// <summary>The verdict for an answer with <paramref name="statusCode"/>.</summary>
    internal static HealthState Verdict(int statusCode, int latencyMs) => statusCode >= ServerErrorFloor
        ? new HealthState(HealthStatus.Down, DateTime.UtcNow, null, HttpErrorReason(statusCode))
        : new HealthState(HealthStatus.Up, DateTime.UtcNow, latencyMs, null);

    /// <summary>The reason tag for a server error with <paramref name="statusCode"/>.</summary>
    internal static string HttpErrorReason(int statusCode) =>
        HttpErrorReasonPrefix + statusCode.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Reads the status code back out of a server-error reason tag.
    /// </summary>
    public static bool TryParseHttpErrorReason(string? reason, out int statusCode)
    {
        statusCode = 0;
        if (reason is null
            || !reason.StartsWith(HttpErrorReasonPrefix, StringComparison.Ordinal)
            || !int.TryParse(
                reason.AsSpan(HttpErrorReasonPrefix.Length),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int parsed)
            || parsed < ServerErrorFloor)
        {
            return false;
        }

        statusCode = parsed;
        return true;
    }

    /// <summary>
    /// Whether the handshake failed because the peer answered with something the system refused,
    /// rather than because the peer closed or reset the connection.
    /// </summary>
    /// <remarks>
    /// Measured on Windows: bytes that are not TLS raise an <see cref="AuthenticationException"/>
    /// with nothing beneath it, while a connection closed during the handshake raises an
    /// <see cref="IOException"/> over a <see cref="SocketException"/>, sometimes wrapped in an
    /// <see cref="AuthenticationException"/>. A refusal is the first with no transport error
    /// anywhere in the chain.
    /// </remarks>
    internal static bool IsTlsRefusal(HttpRequestException ex)
    {
        if (ex.HttpRequestError != HttpRequestError.SecureConnectionError)
        {
            return false;
        }

        bool refused = false;
        for (Exception? inner = ex.InnerException; inner is not null; inner = inner.InnerException)
        {
            if (inner is IOException or SocketException)
            {
                return false;
            }

            refused |= inner is AuthenticationException;
        }

        return refused;
    }

    /// <summary>The reason tag for a request that never got an answer.</summary>
    private static string ReasonFor(HttpRequestException ex)
    {
        if (ex.HttpRequestError == HttpRequestError.NameResolutionError)
        {
            return "dns";
        }

        for (Exception? inner = ex.InnerException; inner is not null; inner = inner.InnerException)
        {
            if (inner is SocketException socket)
            {
                return TcpHealthProbe.ReasonFor(socket.SocketErrorCode);
            }
        }

        return TcpHealthProbe.OtherReason;
    }

    internal static SocketsHttpHandler CreateHandler() => new()
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        UseProxy = false,
        Credentials = null,
        PreAuthenticate = false,

        // A fresh connection each time, as the TCP probe opens one: a pooled connection would
        // answer for a server that has since gone away.
        PooledConnectionLifetime = TimeSpan.Zero,

        // Disposing an unread response would otherwise read up to a megabyte of body to keep
        // the connection; a probe never keeps it.
        MaxResponseDrainSize = 0,
        ResponseDrainTimeout = TimeSpan.Zero,
        SslOptions =
        {
            // See the remarks: the probe trusts nothing it receives and sends nothing to protect.
            RemoteCertificateValidationCallback = static (_, _, _, _) => true,
        },
    };

    public void Dispose() => _client.Dispose();
}
