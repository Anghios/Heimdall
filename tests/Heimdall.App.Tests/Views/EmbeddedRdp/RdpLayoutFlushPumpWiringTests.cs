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

namespace Heimdall.App.Tests.Views.EmbeddedRdp;

/// <summary>
/// Reads the view's own code to check that both of its message pumps still go through the latch,
/// and that the connect path still acts on a flush that was refused.
/// </summary>
/// <remarks>
/// <para><see cref="UiMessagePumpGuardTests"/> pins what the latch decides and
/// <see cref="RdpLayoutFlushDeferralPolicyTests"/> pins what the connect path does with the
/// answer. Nothing in either fails if the view stops calling them, and the failure that would
/// follow is the one that cannot be caught: the process dies on a stack overflow with no
/// exception, no log line and no dump unless the machine was configured for one in advance.</para>
/// <para><b>What this measures, exactly.</b> Both pumps live in a WPF code-behind that needs a
/// live <c>Application</c>, an ActiveX host and a realized surface before either is reached, so
/// they are read from the source rather than run. The text is blanked of comments and literals
/// first, and the two sites that sit at their method's own brace depth are required to stand as
/// statements of it rather than merely to appear in the file. The three control tests at the
/// bottom are what makes that reading worth anything: each mutates the real source in memory into
/// a shape this file must reject, and fails if the assertion above it would have stayed
/// green.</para>
/// <para><b>What it cannot see, said plainly rather than dressed up.</b> It does not establish
/// that either pump runs. <c>EnsureHostHandle</c> pumps inside a conditional and
/// <c>RunConnectAttempt</c> flushes inside a <c>try</c>, so neither site is at its body's own
/// depth and both are read by written order alone - which says the latch is written above the
/// pump, not that the latch is consulted. No reading here evaluates a condition either, so a guard
/// inverted above any of these sites would keep every assertion green while no live connect ever
/// asked the latch anything.</para>
/// </remarks>
public sealed class RdpLayoutFlushPumpWiringTests
{
    private const string FlushMember =
        "private UiMessagePumpAdmission FlushLayoutPipeline(string stage)";
    private const string EnsureHandleMember = "private void EnsureHostHandle()";
    private const string RunAttemptMember = "private void RunConnectAttempt(int attempt)";

    private const string AcquireStatement =
        "using var pump = UiMessagePumpGuard.ForCurrentThread.Acquire();";
    private const string FlushRefusal = "if (pump.Admission == UiMessagePumpAdmission.Defer)";
    private const string HandleAdmission = "if (pump.Admission == UiMessagePumpAdmission.Open)";
    private const string PumpStatement = "WinForms.Application.DoEvents();";
    private const string NestedFrameStatement =
        "Dispatcher.Invoke(DispatcherPriority.Render, new Action(delegate { }));";

    // The two flush results, carried whole. The mutant is the revert: a bare call, which is what
    // the code said before the stack overflows of September 2026, leaves the connect path with
    // nothing to test and no name here to find.
    private const string PreConnectFlush = "var preConnectFlush = FlushLayoutPipeline(";
    private const string PostHandleFlush = "var postHandleFlush = FlushLayoutPipeline(";
    private const string DeferralDecision = "RdpLayoutFlushDeferralPolicy.Decide(";
    private const string RetryTest = "if (deferral == RdpFlushDeferralOutcome.RetryAfterRenderPass)";
    private const string RetryStatement = "_ = RetryBeginConnectAsync(attempt);";

    [Fact]
    public void TheLayoutFlushTakesTheLatchBeforeItPumps()
    {
        string logic = ViewSource.HandlerLogic(FlushMember);

        Assert.True(
            ViewSource.IsStatementOfTheMethodBody(logic, AcquireStatement),
            "the layout flush does not take the pump latch as a step of its body");
        Assert.True(
            ViewSource.IsStatementOfTheMethodBody(logic, FlushRefusal),
            "the layout flush does not test the latch's answer as a step of its body");
        Assert.True(
            ViewSource.IsStatementOfTheMethodBody(logic, PumpStatement),
            "the layout flush no longer pumps at all");

        AssertWrittenInOrder(logic, AcquireStatement, FlushRefusal, PumpStatement);
    }

    /// <remarks>
    /// The dispatcher round trip pushes a nested frame of its own, so it is a second pump and has
    /// to stand below the same refusal. Left above it, the nesting this fix removes comes back
    /// through the half nobody thinks of as a pump.
    /// </remarks>
    [Fact]
    public void TheNestedDispatcherFrameIsUnderTheSameRefusal()
    {
        string logic = ViewSource.HandlerLogic(FlushMember);

        Assert.True(
            ViewSource.IsStatementOfTheMethodBody(logic, NestedFrameStatement),
            "the layout flush no longer makes the render round trip");

        AssertWrittenInOrder(logic, FlushRefusal, NestedFrameStatement);
    }

    /// <remarks>
    /// This one pumps inside <c>if (!_rdpHost.IsHandleCreated)</c>, so the statement predicate
    /// cannot reach it and the reading falls back to written order.
    /// </remarks>
    [Fact]
    public void TheHandleFlushAsksTheLatchBeforeItPumps()
    {
        string logic = ViewSource.HandlerLogic(EnsureHandleMember);

        AssertWrittenInOrder(logic, AcquireStatement, HandleAdmission, PumpStatement);
    }

    /// <remarks>
    /// A refused flush leaves the surface unrealized and the stack carrying somebody else's pump.
    /// The connect path has to keep both answers and put them to the policy; what the policy then
    /// decides is pinned behaviourally next door.
    /// </remarks>
    [Fact]
    public void TheConnectPathPutsBothFlushAnswersToThePolicy()
    {
        string logic = ViewSource.HandlerLogic(RunAttemptMember);

        AssertWrittenInOrder(
            logic,
            PreConnectFlush,
            PostHandleFlush,
            DeferralDecision,
            RetryTest,
            RetryStatement);
    }

    /// <remarks>
    /// The control for <see cref="TheLayoutFlushTakesTheLatchBeforeItPumps"/>. A call left behind
    /// as prose is the mutant a substring search cannot tell from a call that runs.
    /// </remarks>
    [Fact]
    public void TheLatchIsNotFoundWhenItIsOnlyLeftInAComment()
    {
        string logic = MutatedLogic(FlushMember, AcquireStatement, "// " + AcquireStatement);

        Assert.False(
            ViewSource.IsStatementOfTheMethodBody(logic, AcquireStatement),
            "a commented-out latch still reads as a step of the flush, so this file proves nothing");
    }

    /// <remarks>
    /// The control for the refusal. Folding it behind a term that is false by construction keeps
    /// every name exactly where it was and loses the behaviour entirely.
    /// </remarks>
    [Fact]
    public void TheRefusalIsNotFoundWhenItIsFoldedBehindAFalseTerm()
    {
        string logic = MutatedLogic(
            FlushMember,
            FlushRefusal,
            "if (false && pump.Admission == UiMessagePumpAdmission.Defer)");

        Assert.False(
            ViewSource.IsStatementOfTheMethodBody(logic, FlushRefusal),
            "a refusal folded behind a false term still reads as the refusal");
    }

    /// <remarks>
    /// The control for the ordering. A pump written above the latch it is supposed to obey is the
    /// wiring mistake that restores the defect while every presence assertion stays green, so the
    /// ordering has to be an assertion that can actually fail.
    /// </remarks>
    [Fact]
    public void APumpWrittenAboveTheLatchIsRejected()
    {
        string logic = ViewSource.HandlerLogic(FlushMember);

        Assert.ThrowsAny<Xunit.Sdk.XunitException>(
            () => AssertWrittenInOrder(logic, PumpStatement, AcquireStatement));
    }

    /// <summary>
    /// The blanked text of one method, read from a copy of the view in which one statement of that
    /// same method has been replaced.
    /// </summary>
    /// <remarks>
    /// The replacement is made inside the method's own span, not at the file's first match. Both
    /// pump sites are written with the same two statements, so a mutation applied to the first
    /// match in the file would land in <c>EnsureHostHandle</c> and leave the method under test
    /// untouched - a control that mutates nothing passes for the same reason the real assertion
    /// does, and proves nothing.
    /// </remarks>
    private static string MutatedLogic(string member, string statement, string replacement)
    {
        string code = ViewSource.Code();
        int memberAt = code.IndexOf(member, StringComparison.Ordinal);
        Assert.True(memberAt >= 0, $"method not found in the view: {member}");

        int at = code.IndexOf(statement, memberAt, StringComparison.Ordinal);
        Assert.True(at >= 0, $"the statement being mutated is not in {member}: {statement}");

        string mutant = string.Concat(
            code.AsSpan(0, at),
            replacement,
            code.AsSpan(at + statement.Length));

        return ViewSource.HandlerBody(ViewSource.WithoutCommentsAndLiterals(mutant), member);
    }

    /// <summary>Asserts each site is written above the next, in the blanked method text.</summary>
    /// <remarks>
    /// Each site is looked for below the one before it rather than from the top of the method, and
    /// the difference is not cosmetic: <c>RunConnectAttempt</c> hands an attempt to
    /// <c>RetryBeginConnectAsync</c> twice, once for a surface that is not laid out yet and once
    /// for a flush that was refused, and a search that always started from the top would measure
    /// the first call's offset against the second call's test and report an order that is not the
    /// one written.
    /// </remarks>
    private static void AssertWrittenInOrder(string logic, params string[] sites)
    {
        int below = 0;
        foreach (string site in sites)
        {
            int at = logic.IndexOf(site, below, StringComparison.Ordinal);
            Assert.True(
                at >= 0,
                $"the site is not written in this method below the one that must precede it: {site}");
            below = at + site.Length;
        }
    }
}
