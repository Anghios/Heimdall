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

using System.Reflection;
using Heimdall.App.Services;
using Heimdall.App.ViewModels;
using Heimdall.Core.Localization;

namespace Heimdall.App.Tests;

/// <summary>
/// The open-sessions threshold counts what the machine is hosting, not what one window can see,
/// and going past it shows a notice: it never refuses a session.
/// </summary>
/// <remarks>
/// Detaching a session removed it from the counted collection while its host stayed alive, so
/// detaching repeatedly let the count be passed without bound - and every one of those hosts is an
/// ActiveX or WebView2 surface that keeps costing. Every test here also proves that no dialog is
/// raised: the dialog proxy throws on any call.
/// </remarks>
public sealed class ConnectionViewModelSessionLimitTests
{
    [Fact]
    public void ADetachedSessionStillCountsTowardTheThreshold()
    {
        SessionTabViewModel detached = HostedSession("detached", "SSH");
        ConnectionViewModel viewModel = CreateViewModel(out _, detached);

        // The window itself is empty. Before the fix, the census read zero here.
        Assert.Empty(viewModel.ActiveSessions);

        SessionTabViewModel opened = InvokeGuardedAddSession(viewModel, "new", "SSH", limit: 1);

        Assert.Contains(opened, viewModel.ActiveSessions);
        Assert.True(viewModel.IsSessionLoadNoticeShown);
    }

    [Fact]
    public void TheThresholdIsTheTotalAcrossBothPlaces_AndNothingIsRefused()
    {
        SessionTabViewModel detached = HostedSession("detached", "RDP");
        ConnectionViewModel viewModel = CreateViewModel(out _, detached);

        SessionTabViewModel attached = viewModel.AddSession("attached", "Attached", "SSH");
        attached.HostControl = new object();

        // One here, one detached, threshold of three: a third stays under it.
        InvokeGuardedAddSession(viewModel, "third", "SSH", limit: 3).HostControl = new object();
        Assert.False(viewModel.IsSessionLoadNoticeShown);

        // A fourth goes past it: opened all the same, with the notice.
        SessionTabViewModel fourth = InvokeGuardedAddSession(viewModel, "fourth", "SSH", limit: 3);
        Assert.Contains(fourth, viewModel.ActiveSessions);
        Assert.True(viewModel.IsSessionLoadNoticeShown);
        Assert.Equal("SessionLoadNoticeText", viewModel.SessionLoadNoticeText);
    }

    [Fact]
    public void AHiddenNotice_StaysHiddenWhilePastTheThreshold_AndComesBackAfterDroppingUnder()
    {
        ConnectionViewModel viewModel = CreateViewModel(out _);
        viewModel.AddSession("one", "One", "SSH").HostControl = new object();
        SessionTabViewModel second = InvokeGuardedAddSession(viewModel, "two", "SSH", limit: 1);
        second.HostControl = new object();
        Assert.True(viewModel.IsSessionLoadNoticeShown);

        viewModel.DismissSessionLoadNoticeCommand.Execute(null);
        InvokeGuardedAddSession(viewModel, "three", "SSH", limit: 1).HostControl = new object();
        Assert.False(viewModel.IsSessionLoadNoticeShown);

        // Back under the threshold: the notice goes, and the dismissal is forgotten.
        foreach (SessionTabViewModel session in viewModel.ActiveSessions.ToList())
        {
            viewModel.RemoveFromStrip(session);
        }

        Assert.False(viewModel.IsSessionLoadNoticeShown);

        viewModel.AddSession("again-1", "Again", "SSH").HostControl = new object();
        InvokeGuardedAddSession(viewModel, "again-2", "SSH", limit: 1);
        Assert.True(viewModel.IsSessionLoadNoticeShown);
    }

    [Fact]
    public void GoingBackUnderTheThreshold_TakesTheNoticeDown()
    {
        ConnectionViewModel viewModel = CreateViewModel(out _);
        SessionTabViewModel first = viewModel.AddSession("one", "One", "SSH");
        first.HostControl = new object();
        InvokeGuardedAddSession(viewModel, "two", "SSH", limit: 1).HostControl = new object();
        Assert.True(viewModel.IsSessionLoadNoticeShown);

        viewModel.RemoveFromStrip(first);
        Assert.True(viewModel.IsSessionLoadNoticeShown);

        viewModel.RemoveFromStrip(viewModel.ActiveSessions.Single());
        Assert.False(viewModel.IsSessionLoadNoticeShown);
    }

    [Fact]
    public void ADetachedToolPaneDoesNotCount()
    {
        SessionTabViewModel detachedTool = HostedSession("hash", "TOOL:HASH");
        ConnectionViewModel viewModel = CreateViewModel(out TrackingDialogProxy dialog, detachedTool);

        // Tools were never counted while docked, and detaching one must not start counting it.
        Assert.NotNull(InvokeGuardedAddSession(viewModel, "new", "SSH", limit: 1));
        Assert.False(viewModel.IsSessionLoadNoticeShown);
        Assert.Equal(0, dialog.WarningCallCount);
    }

    [Fact]
    public void ASessionInBothPlacesCountsOnce()
    {
        // Reattaching puts the session back in the tab collection before the floating window
        // closes, so for that moment it is legitimately in both. Counting it twice there would
        // refuse a session the user is entitled to.
        ConnectionViewModel viewModel = CreateViewModel(out TrackingDialogProxy dialog);
        SessionTabViewModel attached = viewModel.AddSession("moving", "Moving", "SSH");
        attached.HostControl = new object();

        int counted = ConnectionViewModel.CountEmbeddedConnections([attached, attached]);

        Assert.Equal(1, counted);
        Assert.Equal(0, dialog.WarningCallCount);
    }

    [Theory]
    [InlineData("SSH", true, 1)]
    [InlineData("RDP", true, 1)]
    [InlineData("TOOL:HASH", true, 0)]
    [InlineData("SSH", false, 0)]
    public void ThePaneCensusCountsOnlyHostedNonToolPanes(
        string connectionType,
        bool hosted,
        int expected)
    {
        SessionTabViewModel session = new()
        {
            ServerId = "s",
            Title = "S",
            ConnectionType = connectionType,
        };

        if (hosted)
        {
            session.HostControl = new object();
        }

        Assert.Equal(expected, ConnectionViewModel.CountEmbeddedConnections([session]));
    }

    [Fact]
    public void TheProductionWindowServiceReportsNothingDetachedWithoutAnApplication()
    {
        // Guards the production constructor rather than only the test seam: it must be callable
        // and must answer, not throw, when no window has been created yet.
        SessionWindowService service = new();

        Assert.Empty(service.DetachedSessions);
    }

    private static SessionTabViewModel HostedSession(string id, string connectionType)
    {
        SessionTabViewModel session = new()
        {
            ServerId = id,
            Title = id,
            ConnectionType = connectionType,
        };
        session.HostControl = new object();
        return session;
    }

    private static ConnectionViewModel CreateViewModel(
        out TrackingDialogProxy dialog,
        params SessionTabViewModel[] detachedSessions)
    {
        IDialogService dialogService = DispatchProxy.Create<IDialogService, TrackingDialogProxy>();
        ISplitService splitService = DispatchProxy.Create<ISplitService, TrackingSplitProxy>();
        dialog = (TrackingDialogProxy)(object)dialogService;

        SessionTabViewModel[] detached = detachedSessions;
        SessionWindowService windows = new(
            static (_, _) => { },
            () => detached);

        return new ConnectionViewModel(
            new LocalizationManager(),
            dialogService,
            splitService,
            new PaneCloseArbiter(),
            windows);
    }

    private static SessionTabViewModel InvokeGuardedAddSession(
        ConnectionViewModel viewModel,
        string serverId,
        string connectionType,
        int limit)
    {
        MethodInfo? guardedAdd = typeof(ConnectionViewModel).GetMethod(
            nameof(ConnectionViewModel.AddSession),
            [typeof(string), typeof(string), typeof(string), typeof(int)]);

        Assert.NotNull(guardedAdd);
        return (SessionTabViewModel)guardedAdd!.Invoke(
            viewModel,
            [serverId, serverId, connectionType, limit])!;
    }

    private class TrackingDialogProxy : DispatchProxy
    {
        public int WarningCallCount { get; private set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == nameof(IDialogService.ShowWarning))
            {
                WarningCallCount++;
                return null;
            }

            throw new NotSupportedException(targetMethod?.Name);
        }
    }

    private class TrackingSplitProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == nameof(ISplitService.RegisterSession))
            {
                return null;
            }

            throw new NotSupportedException(targetMethod?.Name);
        }
    }
}
