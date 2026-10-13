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
using Heimdall.App.Views;
using Heimdall.Core.Codecs;
using Heimdall.Core.Configuration;

namespace Heimdall.App.Tests;

/// <summary>
/// The zoom each web console was last left at, kept per profile.
/// </summary>
public sealed class WebZoomMemoryTests
{
    [Fact]
    public void AZoom_IsRememberedPerProfile()
    {
        WebZoomMemory memory = new();

        memory.Remember("ilo", 1.25);

        Assert.Equal(1.25, memory.Get("ilo"));
        Assert.Null(memory.Get("idrac"));
    }

    /// <summary>
    /// The browser steps zoom by multiplication: in then out lands near 1, not on it. That is
    /// the default, and leaves no entry behind.
    /// </summary>
    [Theory]
    [InlineData(1.0)]
    [InlineData(1.0000000000000002)]
    [InlineData(0.999)]
    public void TheDefaultZoom_LeavesNoEntry(double zoom)
    {
        WebZoomMemory memory = new();
        memory.Remember("ilo", 1.5);
        List<(string, double?)> changes = [];
        memory.ZoomChanged += (profileId, kept) => changes.Add((profileId, kept));

        memory.Remember("ilo", zoom);

        Assert.Null(memory.Get("ilo"));
        Assert.Equal([("ilo", (double?)null)], changes);
    }

    [Theory]
    [InlineData(10.0, WebZoomMemory.MaxZoom)]
    [InlineData(0.1, WebZoomMemory.MinZoom)]
    [InlineData(1.333333, 1.33)]
    public void AZoom_IsKeptInsideTheBrowsersRangeToThePercent(double zoom, double expected)
    {
        WebZoomMemory memory = new();

        memory.Remember("ilo", zoom);

        Assert.Equal(expected, memory.Get("ilo"));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void ANonsenseZoom_IsIgnored(double zoom)
    {
        WebZoomMemory memory = new();
        memory.Remember("ilo", 1.5);

        memory.Remember("ilo", zoom);

        Assert.Equal(1.5, memory.Get("ilo"));
    }

    /// <summary>Every wheel notch raises a change; one that lands on the kept value writes nothing.</summary>
    [Fact]
    public void TheSameZoomTwice_IsAnnouncedOnce()
    {
        WebZoomMemory memory = new();
        int changes = 0;
        memory.ZoomChanged += (_, _) => changes++;

        memory.Remember("ilo", 1.5);
        memory.Remember("ilo", 1.5);
        memory.Remember("idrac", 1.0);

        Assert.Equal(1, changes);
    }

    /// <summary>A hand-edited settings file cannot open a console at a zoom the browser refuses.</summary>
    [Fact]
    public void LoadingTheSettings_DropsWhatTheBrowserWouldRefuse()
    {
        WebZoomMemory memory = new();

        memory.LoadFromConfig(new Dictionary<string, double>
        {
            ["kept"] = 1.5,
            ["default"] = 1.0,
            ["huge"] = 50,
            ["zero"] = 0,
            ["nan"] = double.NaN,
            [""] = 1.5,
        });

        Assert.Equal(1.5, memory.Get("kept"));
        Assert.All(["default", "huge", "zero", "nan", ""], id => Assert.Null(memory.Get(id)));
    }

    /// <summary>A console opened twice is two sessions of one profile, and shares its zoom.</summary>
    [Fact]
    public void TheZoom_IsKeptUnderTheProfileNotTheSession()
    {
        string first = SessionIdCodec.Create("ilo");
        string second = SessionIdCodec.Create("ilo");

        Assert.NotEqual(first, second);
        Assert.Equal("ilo", EmbeddedWebView.ZoomProfileId(first));
        Assert.Equal("ilo", EmbeddedWebView.ZoomProfileId(second));
        Assert.Equal("ad-hoc", EmbeddedWebView.ZoomProfileId("ad-hoc"));
    }

    [Fact]
    public async Task AZoom_ReachesTheSettingsAndLeavesThemAtTheDefault()
    {
        string rootPath = Path.Combine(Path.GetTempPath(), "Heimdall-WebZoomMemoryTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(rootPath);
        try
        {
            ConfigManager configManager = new(rootPath);
            await configManager.InitializeAsync();

            await App.PersistWebConsoleZoomAsync(configManager, "ilo", 1.5);
            Assert.Equal(1.5, (await ReloadAsync(rootPath)).WebConsoleZoomByProfile["ilo"]);

            await App.PersistWebConsoleZoomAsync(configManager, "ilo", null);
            Assert.DoesNotContain("ilo", (await ReloadAsync(rootPath)).WebConsoleZoomByProfile);
        }
        finally
        {
            Directory.Delete(rootPath, recursive: true);
        }
    }

    private static async Task<AppSettings> ReloadAsync(string rootPath)
    {
        ConfigManager reloaded = new(rootPath);
        await reloaded.InitializeAsync();
        return await reloaded.LoadSettingsAsync();
    }
}
