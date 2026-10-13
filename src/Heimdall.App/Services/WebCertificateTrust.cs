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

using System.Security.Cryptography;
using System.Text;
using Heimdall.Core.Certificates;
using Heimdall.Sftp;

namespace Heimdall.App.Services;

/// <summary>What is known of a certificate a web console presented and the browser refused.</summary>
internal enum WebCertificateVerdict
{
    /// <summary>The same certificate was trusted before, for good or for this run.</summary>
    Trusted,

    /// <summary>Nothing was ever trusted for this address.</summary>
    Unknown,

    /// <summary>Another certificate was trusted for this address.</summary>
    Changed,
}

/// <summary>
/// Trust on first use for the certificates of web consoles the browser does not accept, the
/// same model as FTPS: a pin per host and port, kept in the settings, asked once.
/// </summary>
/// <remarks>
/// A store of its own rather than the FTPS one, so a pin taken for a web console never answers
/// for an FTPS server on the same address, or the reverse. The one difference from FTPS is the
/// changed certificate: FTPS refuses it, a web console asks again with a warning, because iLO,
/// iDRAC and switches regenerate their self-signed certificate on a firmware update and a refusal
/// with no way past it would lock the user out of the console.
/// </remarks>
public sealed class WebCertificateTrust
{
    private readonly FtpsCertificateStore _store = new();

    public WebCertificateTrust()
    {
        _store.CertificateTrusted += (key, entry) => CertificateTrusted?.Invoke(key, entry);
    }

    /// <summary>Raised when a pin is added or refreshed; the app persists it.</summary>
    public event Action<string, FtpsCertificateEntry>? CertificateTrusted;

    public void LoadEntriesFromConfig(IEnumerable<KeyValuePair<string, FtpsCertificateEntry>> entries)
        => _store.LoadEntriesFromConfig(entries);

    internal WebCertificateVerdict Evaluate(string host, int port, string fingerprint)
    {
        if (_store.GetSessionEntry(host, port) is { } session
            && FingerprintEquals(session.Fingerprint, fingerprint))
        {
            return WebCertificateVerdict.Trusted;
        }

        if (_store.GetEntry(host, port) is not { } stored)
        {
            return WebCertificateVerdict.Unknown;
        }

        if (!FingerprintEquals(stored.Fingerprint, fingerprint))
        {
            return WebCertificateVerdict.Changed;
        }

        _store.RefreshLastSeen(host, port);
        return WebCertificateVerdict.Trusted;
    }

    private static bool FingerprintEquals(string? a, string? b)
    {
        if (a is null || b is null || a.Length != b.Length)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(a), Encoding.ASCII.GetBytes(b));
    }

    /// <summary>Records the user's answer to a prompt for this address.</summary>
    internal void Apply(string host, int port, FtpsCertificateEntry entry, FtpsCertificateDecision decision)
    {
        switch (decision)
        {
            case FtpsCertificateDecision.Accept:
                _store.Trust(host, port, entry);
                break;
            case FtpsCertificateDecision.TrustOnce:
                _store.TrustForSession(host, port, entry);
                break;
        }
    }
}
