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

using System;

namespace Heimdall.App.Views.EmbeddedRdp;

/// <summary>Whether a message pump may be opened on the calling thread.</summary>
internal enum UiMessagePumpAdmission
{
    /// <summary>Nothing is pumping this thread's queue, so this pump may run.</summary>
    Open,

    /// <summary>A pump is already draining this thread's queue, so this one must not nest.</summary>
    Defer,
}

/// <summary>
/// Refuses a message pump that would nest inside one already open on the same thread.
/// </summary>
/// <remarks>
/// <para>The RDP connect path pumps the UI thread four times before <c>Connect</c>: the WPF and
/// WinForms layout passes have to be realized before the ActiveX control is handed a window, and
/// <c>DoEvents</c> is what realizes them. Pumping is also what makes the path re-entrant, and the
/// view already knows it - the flush sites carry a comment about a tab close dispatched inside the
/// pump tearing the view down underneath them.</para>
/// <para>What nothing bounded is the nesting itself. A message delivered inside the pump can be a
/// COM disconnect or auto-reconnect notification, which starts another attempt, which opens
/// another pump, which delivers the next notification of the same burst. Every level is real
/// stack: WPF interop, the WinForms pump and the mstsc ActiveX callback together cost several
/// kilobytes per level, and a 1 MB thread stack does not survive many of them. A burst that drops
/// every transport at once - a Hyper-V adapter recreated underneath the tunnels, or the machine
/// entering sleep with sessions open - is exactly the input that produces one, and the process
/// dies on <c>STATUS_STACK_OVERFLOW</c>, which no handler can catch and no log records.</para>
/// <para>So an inner pump is refused rather than bounded by a depth ceiling. Refusing costs the
/// caller nothing it actually needs: the outer pump is draining the very same queue, so the
/// messages still run - what the inner caller loses is only the guarantee that they have run by
/// the time <see cref="UiMessagePumpLease.Admission"/> returns. That guarantee is what the connect
/// path wants, and the way to get it is to let the stack unwind and come back on a later render
/// pass, not to pump again from underneath.</para>
/// <para>The latch is per thread rather than per view because the burst is not per view. Several
/// panes drop together, and a pump opened by one pane delivers the notification that starts the
/// next pane's attempt; a latch owned by each view would see depth 1 at every level and refuse
/// nothing.</para>
/// </remarks>
internal sealed class UiMessagePumpGuard
{
    [ThreadStatic]
    private static UiMessagePumpGuard? t_forCurrentThread;

    private bool _pumping;

    /// <summary>The latch belonging to the calling thread, created on first use.</summary>
    internal static UiMessagePumpGuard ForCurrentThread =>
        t_forCurrentThread ??= new UiMessagePumpGuard();

    /// <summary>Whether a pump admitted by this guard is currently open.</summary>
    internal bool IsPumping => _pumping;

    /// <summary>Asks to open a pump, and takes the latch when the answer is yes.</summary>
    /// <returns>
    /// A lease carrying the answer. It must be disposed either way: only an admitted lease
    /// releases the latch, and only the lease that took it may release it.
    /// </returns>
    internal UiMessagePumpLease Acquire()
    {
        if (_pumping)
        {
            return new UiMessagePumpLease(owner: null);
        }

        _pumping = true;
        return new UiMessagePumpLease(this);
    }

    /// <summary>Releases the latch. Called only by the lease that took it.</summary>
    private void Leave() => _pumping = false;

    /// <summary>The answer to one <see cref="Acquire"/>, and the release of the latch it took.</summary>
    /// <remarks>
    /// A deferred lease holds no owner, which is what makes its disposal a no-op: a lease that
    /// released on every path would clear the latch its own caller never took, and the pump
    /// standing above it would be unlatched while still running.
    /// </remarks>
    internal readonly struct UiMessagePumpLease : IDisposable
    {
        private readonly UiMessagePumpGuard? _owner;

        internal UiMessagePumpLease(UiMessagePumpGuard? owner) => _owner = owner;

        /// <summary>Whether the caller may pump.</summary>
        internal UiMessagePumpAdmission Admission =>
            _owner is null ? UiMessagePumpAdmission.Defer : UiMessagePumpAdmission.Open;

        /// <inheritdoc/>
        public void Dispose() => _owner?.Leave();
    }
}
