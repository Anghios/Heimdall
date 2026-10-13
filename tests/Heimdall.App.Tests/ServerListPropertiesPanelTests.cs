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

using Heimdall.Core.Configuration;

namespace Heimdall.App.Tests;

/// <summary>
/// The properties panel preference and the credential provider fact, seen through the list view
/// model (issue #557).
/// </summary>
public sealed partial class ServerListSelectionTests
{
    [Fact]
    public async Task ThePropertiesPanel_IsOffByDefault()
    {
        await using ServerListSelectionFixture fixture = await ServerListSelectionFixture.CreateAsync();
        fixture.LoadServers(await fixture.ConfigManager.LoadSettingsAsync(), CreateServer("alpha", "Alpha", "ops"));

        Assert.False(fixture.ViewModel.ShowPropertiesPanel);
    }

    [Fact]
    public async Task ShowingThePropertiesPanel_ReachesTheSettings()
    {
        await using ServerListSelectionFixture fixture = await ServerListSelectionFixture.CreateAsync();
        fixture.LoadServers(await fixture.ConfigManager.LoadSettingsAsync(), CreateServer("alpha", "Alpha", "ops"));

        fixture.ViewModel.ShowPropertiesPanel = true;
        await fixture.ViewModel.ViewPreferencePersistence;

        AppSettings saved = await fixture.ConfigManager.LoadSettingsAsync();
        Assert.True(saved.ShowSessionPropertiesPanel);
    }

    [Fact]
    public async Task APersistedPropertiesPanel_IsAppliedOnLoadWithoutBeingWrittenBack()
    {
        await using ServerListSelectionFixture fixture = await ServerListSelectionFixture.CreateAsync();
        await fixture.ConfigManager.MergeSettingAsync(persisted =>
        {
            persisted.ShowSessionPropertiesPanel = true;
            persisted.SessionPropertiesPanelHeight = 333;
        });
        AppSettings settings = await fixture.ConfigManager.LoadSettingsAsync();
        Task persistenceBefore = fixture.ViewModel.ViewPreferencePersistence;

        fixture.LoadServers(settings, CreateServer("alpha", "Alpha", "ops"));

        Assert.True(fixture.ViewModel.ShowPropertiesPanel);
        Assert.Equal(333d, fixture.ViewModel.PropertiesPanelHeight);
        Assert.Same(persistenceBefore, fixture.ViewModel.ViewPreferencePersistence);
    }

    [Fact]
    public async Task ADraggedPanelHeight_IsClampedAndSaved()
    {
        await using ServerListSelectionFixture fixture = await ServerListSelectionFixture.CreateAsync();
        fixture.LoadServers(await fixture.ConfigManager.LoadSettingsAsync(), CreateServer("alpha", "Alpha", "ops"));

        fixture.ViewModel.CommitPropertiesPanelHeight(310.6);
        await fixture.ViewModel.ViewPreferencePersistence;
        Assert.Equal(311d, fixture.ViewModel.PropertiesPanelHeight);
        Assert.Equal(311, (await fixture.ConfigManager.LoadSettingsAsync()).SessionPropertiesPanelHeight);

        fixture.ViewModel.CommitPropertiesPanelHeight(10);
        await fixture.ViewModel.ViewPreferencePersistence;
        Assert.Equal(Heimdall.App.Services.SessionPropertiesPanelLayout.MinHeight, fixture.ViewModel.PropertiesPanelHeight);
        Assert.Equal(120, (await fixture.ConfigManager.LoadSettingsAsync()).SessionPropertiesPanelHeight);
    }

    [Fact]
    public async Task TheCredentialProviderSetting_ReachesTheAuthenticationLineOfEveryRow()
    {
        await using ServerListSelectionFixture fixture = await ServerListSelectionFixture.CreateAsync();
        await fixture.ConfigManager.MergeSettingAsync(persisted => persisted.UseExternalCredentialProvider = true);
        fixture.LoadServers(await fixture.ConfigManager.LoadSettingsAsync(), CreateServer("alpha", "Alpha", "ops"));

        Assert.Equal("SSH agent + Credential provider", fixture.ServerById("alpha").AuthSummary);

        await fixture.ConfigManager.MergeSettingAsync(persisted => persisted.UseExternalCredentialProvider = false);
        fixture.LoadServers(await fixture.ConfigManager.LoadSettingsAsync(), CreateServer("alpha", "Alpha", "ops"));

        Assert.Equal("SSH agent", fixture.ServerById("alpha").AuthSummary);
    }
}
