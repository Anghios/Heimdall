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

using Heimdall.App.Views.EmbeddedRdp;

namespace Heimdall.App.Tests.Views.EmbeddedRdp;

/// <summary>
/// Pins what a connect attempt does with two layout flushes that may each have been refused.
/// </summary>
public sealed class RdpLayoutFlushDeferralPolicyTests
{
    private const int MaxAttempts = 10;

    [Fact]
    public void BothFlushesPumped_TheAttemptConnects()
    {
        Assert.Equal(
            RdpFlushDeferralOutcome.Connect,
            RdpLayoutFlushDeferralPolicy.Decide(
                UiMessagePumpAdmission.Open,
                UiMessagePumpAdmission.Open,
                attempt: 1,
                MaxAttempts));
    }

    /// <remarks>
    /// Either refusal stands the attempt down on its own. Requiring both would connect on a
    /// surface whose handle messages never ran, which is the same unrealized surface the flush
    /// exists to rule out - and it is the mutant a test that only ever refuses both cannot see.
    /// </remarks>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void AFlushWasRefused_TheAttemptStandsDown(
        bool preConnectRefused,
        bool postHandleRefused)
    {
        Assert.Equal(
            RdpFlushDeferralOutcome.RetryAfterRenderPass,
            RdpLayoutFlushDeferralPolicy.Decide(
                Admission(preConnectRefused),
                Admission(postHandleRefused),
                attempt: 1,
                MaxAttempts));
    }

    /// <summary>The admission a flush comes back with, given whether its pump was refused.</summary>
    /// <remarks>
    /// The theory above carries booleans rather than admissions because the admission type is
    /// internal to the application and a public test method cannot take one as a parameter.
    /// </remarks>
    private static UiMessagePumpAdmission Admission(bool refused) =>
        refused ? UiMessagePumpAdmission.Defer : UiMessagePumpAdmission.Open;

    /// <remarks>
    /// The ceiling is inclusive, like the surface-not-ready retry whose counter this shares. Off by
    /// one here spends the last render pass connecting unflushed instead of waiting one more.
    /// </remarks>
    [Fact]
    public void AtTheLastAllowedAttempt_TheAttemptStillStandsDown()
    {
        Assert.Equal(
            RdpFlushDeferralOutcome.RetryAfterRenderPass,
            RdpLayoutFlushDeferralPolicy.Decide(
                UiMessagePumpAdmission.Defer,
                UiMessagePumpAdmission.Open,
                attempt: MaxAttempts,
                MaxAttempts));
    }

    /// <remarks>
    /// Past the ceiling the session wins. Ten render passes with a pump still open above the
    /// attempt is not a transient, and an attempt that stands down forever never connects at all.
    /// </remarks>
    [Fact]
    public void PastTheCeiling_TheAttemptConnectsUnflushed()
    {
        Assert.Equal(
            RdpFlushDeferralOutcome.ConnectUnflushed,
            RdpLayoutFlushDeferralPolicy.Decide(
                UiMessagePumpAdmission.Defer,
                UiMessagePumpAdmission.Open,
                attempt: MaxAttempts + 1,
                MaxAttempts));
    }

    /// <remarks>
    /// The ceiling governs the refusal only. An attempt whose flushes both pumped connects
    /// normally however many times it has been round, and must not be reported as unflushed: the
    /// log line that outcome carries would name a defect that did not happen.
    /// </remarks>
    [Fact]
    public void PastTheCeiling_AFlushedAttemptStillConnectsNormally()
    {
        Assert.Equal(
            RdpFlushDeferralOutcome.Connect,
            RdpLayoutFlushDeferralPolicy.Decide(
                UiMessagePumpAdmission.Open,
                UiMessagePumpAdmission.Open,
                attempt: MaxAttempts + 1,
                MaxAttempts));
    }
}
