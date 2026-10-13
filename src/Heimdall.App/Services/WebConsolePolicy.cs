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

namespace Heimdall.App.Services;

/// <summary>
/// The decisions of the embedded web console, kept out of the view so they can be tested
/// without a browser.
/// </summary>
internal static class WebConsolePolicy
{
    /// <summary>
    /// Whether the console may navigate to <paramref name="uri"/>: web pages and the browser's
    /// own blank and blob documents. A file:, a custom protocol handler or a script URL would
    /// reach the machine rather than the console.
    /// </summary>
    public static bool IsNavigable(string? uri)
    {
        if (!Uri.TryCreate(uri, UriKind.Absolute, out Uri? parsed))
        {
            return false;
        }

        return parsed.Scheme is "http" or "https" or "about" or "blob";
    }

    /// <summary>
    /// Whether the stored account answers an HTTP authentication challenge. Once per tab and
    /// host: a second challenge means the account was refused, and answering it again would
    /// loop, or lock the account on a console that counts failures. The browser's own sign-in
    /// prompt takes over.
    /// </summary>
    /// <remarks>
    /// Only the profile's own host and port get the account: a page that redirects to, or loads
    /// from, another server must not receive the console's password by asking for it.
    /// </remarks>
    /// <remarks>
    /// Without a stored password the account is not sent either: an empty password is a failed
    /// attempt that iLO and iDRAC count towards their lockout, spent before the user is asked.
    /// </remarks>
    public static bool ShouldAnswerChallenge(
        string? username,
        string? password,
        bool alreadyAnsweredForHost,
        Uri challengeUri,
        Uri profileAddress)
    {
        ArgumentNullException.ThrowIfNull(challengeUri);
        ArgumentNullException.ThrowIfNull(profileAddress);

        return !string.IsNullOrEmpty(username)
            && !string.IsNullOrEmpty(password)
            && !alreadyAnsweredForHost
            && string.Equals(challengeUri.Scheme, profileAddress.Scheme, StringComparison.OrdinalIgnoreCase)
            && string.Equals(challengeUri.Host, profileAddress.Host, StringComparison.OrdinalIgnoreCase)
            && challengeUri.Port == profileAddress.Port;
    }

    /// <summary>
    /// Whether a navigation that did not succeed ends the session. Only a failure to reach the
    /// console before it ever loaded does: an HTTP error status is a page the console chose to
    /// show, and a failure after the first load is one link among many.
    /// </summary>
    public static bool IsSessionFailure(bool isSuccess, int httpStatusCode, bool canceled, bool loadedBefore)
        => !isSuccess && httpStatusCode == 0 && !canceled && !loadedBefore;

    /// <summary>The key a challenge is remembered by: scheme, host and port, case-folded.</summary>
    public static string ChallengeKey(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        return $"{uri.Scheme}://{uri.Host.ToLowerInvariant()}:{uri.Port}";
    }
}
