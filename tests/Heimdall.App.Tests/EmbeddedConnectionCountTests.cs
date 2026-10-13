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

using Heimdall.App.ViewModels;
using Heimdall.Core.Models;

namespace Heimdall.App.Tests;

/// <summary>
/// The embedded session limit counts connections, not panes: the companion panes Heimdall opens
/// beside a session on its own do not make that session cost two.
/// </summary>
public sealed class EmbeddedConnectionCountTests
{
    [Fact]
    public void SshWithItsSftpCompanion_CountsOnce()
    {
        Assert.Equal(1, ConnectionViewModel.CountEmbeddedConnections([SshWithSftpCompanion("web")]));
    }

    [Fact]
    public void TenSshTabsWithCompanions_ReachTheDefaultLimitOfTen_NotFive()
    {
        SessionTabViewModel[] tabs = Enumerable.Range(0, 10)
            .Select(index => SshWithSftpCompanion($"host-{index}"))
            .ToArray();

        Assert.Equal(10, ConnectionViewModel.CountEmbeddedConnections(tabs));
        Assert.False(ConnectionViewModel.IsAtSessionThreshold(tabs[..9], 10));
        Assert.True(ConnectionViewModel.IsAtSessionThreshold(tabs, 10));
    }

    [Fact]
    public void LocalShellWithItsFileBrowser_CountsOnce()
    {
        SessionTabViewModel tab = Split(
            Pane("Local", serverId: "local-session", originalServerId: "local-profile"),
            Pane("LOCAL", serverId: "", originalServerId: ""));

        Assert.Equal(1, ConnectionViewModel.CountEmbeddedConnections([tab]));
    }

    [Fact]
    public void StandaloneSftpTab_Counts()
    {
        SessionTabViewModel tab = new() { RootContent = Pane("SFTP", "sftp-session", "files") };

        Assert.Equal(1, ConnectionViewModel.CountEmbeddedConnections([tab]));
    }

    [Fact]
    public void SplitBetweenTwoDifferentHosts_CountsTwice()
    {
        SessionTabViewModel tab = Split(
            Pane("SSH", "ssh-session", "web"),
            Pane("SFTP", "sftp-session", "db"));

        Assert.Equal(2, ConnectionViewModel.CountEmbeddedConnections([tab]));
    }

    [Fact]
    public void TwoSftpPanesOfTheSameProfile_AreNotEachOthersCompanion()
    {
        SessionTabViewModel tab = Split(
            Pane("SFTP", "sftp-1", "files"),
            Pane("SFTP", "sftp-2", "files"));

        Assert.Equal(2, ConnectionViewModel.CountEmbeddedConnections([tab]));
    }

    private static SessionTabViewModel SshWithSftpCompanion(string profileId) => Split(
        Pane("SSH", $"{profileId}-ssh", profileId),
        Pane("SFTP", $"{profileId}-sftp", profileId));

    private static SessionTabViewModel Split(SessionPaneModel first, SessionPaneModel second) => new()
    {
        RootContent = new SplitContainerModel { First = first, Second = second },
    };

    private static SessionPaneModel Pane(string connectionType, string serverId, string originalServerId) => new()
    {
        ConnectionType = connectionType,
        ServerId = serverId,
        OriginalServerId = originalServerId,
        HostControl = new object(),
    };
}
