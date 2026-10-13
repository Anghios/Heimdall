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
using Heimdall.App.ViewModels.CommandPalette;
using Heimdall.Core.Configuration;

namespace Heimdall.App.Tests;

/// <summary>
/// Reveal in tree finds a profile the tree's filters hide, and makes it visible.
/// </summary>
public sealed partial class ServerListSelectionTests
{
    [Fact]
    public async Task PrepareReveal_ProfileHiddenByAFilter_ResetsTheFiltersAndShowsTheRow()
    {
        await using ServerListSelectionFixture fixture = await ServerListSelectionFixture.CreateAsync();
        fixture.LoadServers(fixture.ExpandGroups("ops"), CreateServer("a", "A", "ops"));
        ServerListViewModel vm = fixture.ViewModel;
        vm.FavoriteFilterEnabled = true;
        Assert.DoesNotContain(vm.Servers, row => row.Id == "a");

        ServerItemViewModel? revealed = vm.PrepareReveal("a");

        Assert.Equal("a", revealed?.Id);
        Assert.False(vm.FavoriteFilterEnabled);
        Assert.Contains(vm.Servers, row => row.Id == "a");
    }

    [Fact]
    public async Task PrepareReveal_ProfileAlreadyVisible_LeavesTheFiltersAlone()
    {
        await using ServerListSelectionFixture fixture = await ServerListSelectionFixture.CreateAsync();
        ServerProfileDto favorite = CreateServer("a", "A", "ops");
        favorite.IsFavorite = true;
        fixture.LoadServers(fixture.ExpandGroups("ops"), favorite, CreateServer("b", "B", "ops"));
        ServerListViewModel vm = fixture.ViewModel;
        vm.FavoriteFilterEnabled = true;
        Assert.Contains(vm.Servers, row => row.Id == "a");
        Assert.DoesNotContain(vm.Servers, row => row.Id == "b");

        ServerItemViewModel? revealed = vm.PrepareReveal("a");

        Assert.Equal("a", revealed?.Id);
        Assert.True(vm.FavoriteFilterEnabled);
    }

    [Fact]
    public async Task PrepareReveal_UnknownProfile_ReturnsNull_AndKeepsTheFilters()
    {
        await using ServerListSelectionFixture fixture = await ServerListSelectionFixture.CreateAsync();
        fixture.LoadServers(fixture.ExpandGroups("ops"), CreateServer("a", "A", "ops"));
        ServerListViewModel vm = fixture.ViewModel;
        vm.FavoriteFilterEnabled = true;

        Assert.Null(vm.PrepareReveal("missing"));
        Assert.True(vm.FavoriteFilterEnabled);
    }
}

/// <summary>
/// The command palette is a launcher: a filter left on in the tree does not hide sessions from it.
/// </summary>
public sealed partial class SessionCoordinatorPreMountTests
{
    [Fact]
    public async Task Palette_SearchFindsAProfileTheTreeFilterHides()
    {
        using TestHarness harness = TestHarness.Create();
        ServerProfileDto saved = harness.CreateServer("SSH");
        await harness.PersistServerAsync(saved);
        harness.Main.ServerList.FavoriteFilterEnabled = true;
        Assert.DoesNotContain(harness.Main.ServerList.Servers, row => row.Id == saved.Id);
        CommandPaletteViewModel palette = harness.Main.CommandPalette;

        palette.SearchText = saved.DisplayName;

        Assert.Contains(palette.Results, row => row.Id == saved.Id);
    }

    [Fact]
    public async Task Palette_SuggestionsIncludeAProfileTheTreeFilterHides()
    {
        using TestHarness harness = TestHarness.Create();
        ServerProfileDto saved = harness.CreateServer("SSH");
        await harness.PersistServerAsync(saved);
        harness.Main.ServerList.FavoriteFilterEnabled = true;
        Assert.DoesNotContain(harness.Main.ServerList.Servers, row => row.Id == saved.Id);
        CommandPaletteViewModel palette = harness.Main.CommandPalette;

        palette.SearchText = "x";
        palette.SearchText = string.Empty;

        Assert.Contains(palette.Results, row => row.Id == saved.Id);
    }
}
