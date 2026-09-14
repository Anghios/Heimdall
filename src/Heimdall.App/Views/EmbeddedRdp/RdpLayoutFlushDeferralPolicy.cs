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

namespace Heimdall.App.Views.EmbeddedRdp;

/// <summary>What a connect attempt does with the answers its layout flushes came back with.</summary>
internal enum RdpFlushDeferralOutcome
{
    /// <summary>Both flushes pumped, so the surface is realized and the attempt connects.</summary>
    Connect,

    /// <summary>A flush was refused; the attempt stands down and resumes on a later render pass.</summary>
    RetryAfterRenderPass,

    /// <summary>A flush was refused and the retries are spent, so the attempt connects regardless.</summary>
    ConnectUnflushed,
}

/// <summary>
/// Decides whether a connect attempt whose layout flush was refused may still call Connect.
/// </summary>
/// <remarks>
/// <para>A flush refused by <see cref="UiMessagePumpGuard"/> ran its layout passes but drained no
/// queue, so the caller does not know its surface is realized - and worse, it now knows it is
/// standing on somebody else's pump. Connecting from there is what the September 2026 stack
/// overflows did: the ActiveX control was opened from a stack that already carried the pump above
/// it, and the next notification of the burst added another level.</para>
/// <para>The way out already existed for a surface that is merely not laid out yet: stand down,
/// and let the render-pass retry bring the attempt back. It returns through the outer pump, which
/// unwinds every nested frame before the retry runs on a stack of its own. So the refusal reuses
/// it, and reuses its counter too - an attempt that stands down forever is an attempt that never
/// connects, and the ceiling is what stops that.</para>
/// <para>Which is why the ceiling's outcome is <see cref="RdpFlushDeferralOutcome.ConnectUnflushed"/>
/// rather than a failure. Ten refusals in a row means the pump above this attempt has not closed
/// across ten render passes, and at that point a session the user asked for is worth more than the
/// guarantee that its surface was flushed. The nesting is bounded either way: the pumps themselves
/// are latched, so what this decides is only whether to wait for a clean stack, never how deep the
/// stack may go.</para>
/// </remarks>
internal static class RdpLayoutFlushDeferralPolicy
{
    /// <summary>Decides what an attempt does with the answers its two flushes gave.</summary>
    /// <param name="preConnect">What the flush before the handle was created came back with.</param>
    /// <param name="postHandle">What the flush after it came back with.</param>
    /// <param name="attempt">The attempt number this connect is on, counting from one.</param>
    /// <param name="maxAttempts">The last attempt number allowed to stand down.</param>
    /// <remarks>
    /// Either refusal is enough. They are two separate pumps and a single one refused is a single
    /// point where the queue was not drained, so reading them together - both refused before
    /// standing down - would connect on a surface whose handle messages never ran.
    /// </remarks>
    internal static RdpFlushDeferralOutcome Decide(
        UiMessagePumpAdmission preConnect,
        UiMessagePumpAdmission postHandle,
        int attempt,
        int maxAttempts)
    {
        if (preConnect == UiMessagePumpAdmission.Open
            && postHandle == UiMessagePumpAdmission.Open)
        {
            return RdpFlushDeferralOutcome.Connect;
        }

        return attempt <= maxAttempts
            ? RdpFlushDeferralOutcome.RetryAfterRenderPass
            : RdpFlushDeferralOutcome.ConnectUnflushed;
    }
}
