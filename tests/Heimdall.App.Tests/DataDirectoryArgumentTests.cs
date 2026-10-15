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

using System.IO;
using Heimdall.App.Services;
using Heimdall.Core.Configuration;

namespace Heimdall.App.Tests;

/// <summary>
/// <c>--data-dir</c>: a second instance with its own profiles, settings and logs, so a hand test
/// never writes into the user's live profile.
/// </summary>
/// <remarks>
/// The process-wide override itself is never set here: tests run in parallel and every one of
/// them resolves paths. Only the pure parts are exercised; the hand pass runs the option end to end.
/// </remarks>
public sealed class DataDirectoryArgumentTests
{
    private static readonly string Absolute = Path.Combine(Path.GetTempPath(), "heimdall-hand-test");

    [Fact]
    public void TheOption_WithASeparateValue_IsRead()
    {
        Assert.Equal(Absolute, DataDirectoryArgument.Find(["--data-dir", Absolute]));
    }

    [Fact]
    public void TheOption_WithAnEqualsSign_IsRead()
    {
        Assert.Equal(Absolute, DataDirectoryArgument.Find([$"--data-dir={Absolute}"]));
    }

    [Fact]
    public void ARelativePath_IsMadeAbsolute()
    {
        Assert.Equal(Path.GetFullPath("hand-test"), DataDirectoryArgument.Find(["--data-dir", "hand-test"]));
    }

    /// <param name="commandLine">The arguments, separated by a vertical bar.</param>
    [Theory]
    [InlineData("")]
    [InlineData("--data-dir")]
    [InlineData("--data-dir|--other")]
    [InlineData("--data-dir=")]
    [InlineData("--elevated-launch|x")]
    public void NoUsableValue_LeavesTheDefault(string commandLine)
    {
        string[] args = commandLine.Length == 0 ? [] : commandLine.Split('|');

        Assert.Null(DataDirectoryArgument.Find(args));
    }

    [Fact]
    public void TheLastOccurrence_Wins()
    {
        string other = Path.Combine(Path.GetTempPath(), "other");

        Assert.Equal(other, DataDirectoryArgument.Find(["--data-dir", Absolute, $"--data-dir={other}"]));
    }

    /// <summary>The command library moves with the data directory, so a test instance shares no database.</summary>
    [Fact]
    public void TheCommandLibrary_FollowsTheDataDirectory()
    {
        Assert.Equal(
            Path.Combine(Absolute, ApplicationDataPathResolver.TwinShellFolderName),
            ApplicationDataPathResolver.ResolveTwinShellDirectory(Absolute, @"C:\Users\someone\AppData\Local"));
        Assert.Equal(
            Path.Combine(@"C:\Users\someone\AppData\Local", ApplicationDataPathResolver.TwinShellFolderName),
            ApplicationDataPathResolver.ResolveTwinShellDirectory(null, @"C:\Users\someone\AppData\Local"));
    }

    [Fact]
    public void ARelativeDataRoot_IsRefused_BeforeAnythingIsSet()
    {
        Assert.Throws<ArgumentException>(() => ApplicationDataPathResolver.UseDataRoot("relative"));
        Assert.False(ApplicationDataPathResolver.IsOverridden);
    }
}
