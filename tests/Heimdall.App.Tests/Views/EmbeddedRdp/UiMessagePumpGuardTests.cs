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

using System.Threading;
using Heimdall.App.Views.EmbeddedRdp;

namespace Heimdall.App.Tests.Views.EmbeddedRdp;

/// <summary>
/// Pins what the pump latch decides: one pump per thread, released when its lease ends, and
/// nothing shared between threads.
/// </summary>
/// <remarks>
/// The defect this guards against is a stack overflow, which is not a failure a test can observe:
/// the process dies where it stands, no handler runs, and there is nothing left to assert on. What
/// is testable is the decision that bounds the nesting, so that is what is extracted and pinned
/// here. <see cref="RdpLayoutFlushPumpWiringTests"/> is the separate question of whether the view
/// still asks it.
/// </remarks>
public sealed class UiMessagePumpGuardTests
{
    [Fact]
    public void Acquire_OnAQuietThread_Opens()
    {
        var guard = new UiMessagePumpGuard();

        using var lease = guard.Acquire();

        Assert.Equal(UiMessagePumpAdmission.Open, lease.Admission);
        Assert.True(guard.IsPumping);
    }

    /// <remarks>
    /// The defect itself, stated as a test: a pump asked for from inside a pump is the nesting
    /// that consumes the stack, and it is refused.
    /// </remarks>
    [Fact]
    public void Acquire_InsideAnOpenPump_Defers()
    {
        var guard = new UiMessagePumpGuard();

        using var outer = guard.Acquire();
        using var inner = guard.Acquire();

        Assert.Equal(UiMessagePumpAdmission.Open, outer.Admission);
        Assert.Equal(UiMessagePumpAdmission.Defer, inner.Admission);
    }

    /// <remarks>
    /// A latch that took but never released would refuse every pump the process asks for after the
    /// first connect, which is a worse defect than the one being fixed and a silent one: the RDP
    /// surface would simply stop being realized before Connect.
    /// </remarks>
    [Fact]
    public void Dispose_OfAnAdmittedLease_ReopensTheThread()
    {
        var guard = new UiMessagePumpGuard();

        using (var first = guard.Acquire())
        {
            Assert.Equal(UiMessagePumpAdmission.Open, first.Admission);
        }

        Assert.False(guard.IsPumping);

        using var second = guard.Acquire();
        Assert.Equal(UiMessagePumpAdmission.Open, second.Admission);
    }

    /// <remarks>
    /// The refused lease must not release the latch it never took. A <c>Dispose</c> that released
    /// unconditionally would unlatch the pump still running above it, and the level after that
    /// would be admitted: the nesting would be bounded at two rather than at one, and a burst deep
    /// enough to overflow the stack would still overflow it.
    /// </remarks>
    [Fact]
    public void Dispose_OfADeferredLease_LeavesTheOpenPumpLatched()
    {
        var guard = new UiMessagePumpGuard();

        using var outer = guard.Acquire();

        using (var refused = guard.Acquire())
        {
            Assert.Equal(UiMessagePumpAdmission.Defer, refused.Admission);
        }

        Assert.True(guard.IsPumping);
        using var afterRefusal = guard.Acquire();
        Assert.Equal(UiMessagePumpAdmission.Defer, afterRefusal.Admission);
    }

    /// <remarks>
    /// The latch has to survive between two calls of the same thread or it decides nothing: a
    /// property handing back a fresh guard every time would admit every pump ever asked for, and
    /// every other test in this file would still pass, because they all hold one instance.
    /// </remarks>
    [Fact]
    public void ForCurrentThread_IsTheSameLatchAcrossCalls()
    {
        using var outer = UiMessagePumpGuard.ForCurrentThread.Acquire();
        using var inner = UiMessagePumpGuard.ForCurrentThread.Acquire();

        Assert.Equal(UiMessagePumpAdmission.Open, outer.Admission);
        Assert.Equal(UiMessagePumpAdmission.Defer, inner.Admission);
    }

    /// <remarks>
    /// A pump belongs to the thread whose queue it drains. A latch shared between threads would
    /// refuse a pump on a thread where nothing is pumping, which is a refusal with no defect
    /// behind it - and floating session windows are the shape that would meet it.
    /// </remarks>
    [Fact]
    public void ForCurrentThread_IsNotSharedBetweenThreads()
    {
        using var held = UiMessagePumpGuard.ForCurrentThread.Acquire();
        Assert.Equal(UiMessagePumpAdmission.Open, held.Admission);

        UiMessagePumpAdmission elsewhere = UiMessagePumpAdmission.Defer;
        var other = new Thread(() =>
        {
            using var lease = UiMessagePumpGuard.ForCurrentThread.Acquire();
            elsewhere = lease.Admission;
        });

        other.Start();
        Assert.True(other.Join(TimeSpan.FromSeconds(30)), "the second thread never finished");

        Assert.Equal(UiMessagePumpAdmission.Open, elsewhere);
    }
}
