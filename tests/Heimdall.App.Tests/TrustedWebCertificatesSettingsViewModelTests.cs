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
using System.Reflection;
using Heimdall.App.Services;
using Heimdall.App.ViewModels.Settings;
using Heimdall.Core.Certificates;
using Heimdall.Core.Localization;

namespace Heimdall.App.Tests;

/// <summary>
/// The settings panel that lists and forgets the certificates trusted for web consoles.
/// </summary>
public sealed class TrustedWebCertificatesSettingsViewModelTests
{
    [Fact]
    public async Task EveryDurablePin_IsListedUnderItsAddress()
    {
        WebCertificateTrust trust = TrustWith(("ilo.example.test", 443, "SHA256:AA"), ("fe80::1", 8443, "SHA256:BB"));
        TrustedWebCertificatesSettingsViewModel vm = await CreateAsync(trust, confirm: true);

        vm.Refresh();

        Assert.Equal(["ilo.example.test:443", "[fe80::1]:8443"], vm.Rows.Select(row => row.Endpoint));
        Assert.True(vm.HasRows);
        Assert.False(vm.IsEmptyStateVisible);
    }

    [Fact]
    public async Task APinForTheSessionOnly_IsNotListed()
    {
        var trust = new WebCertificateTrust();
        trust.Apply("ilo.example.test", 443, Entry("SHA256:AA"), FtpsCertificateDecision.TrustOnce);
        TrustedWebCertificatesSettingsViewModel vm = await CreateAsync(trust, confirm: true);

        vm.Refresh();

        Assert.Empty(vm.Rows);
        Assert.True(vm.IsEmptyStateVisible);
    }

    [Fact]
    public async Task TheSearch_FiltersRowsButNotTheEmptyState()
    {
        WebCertificateTrust trust = TrustWith(("ilo.example.test", 443, "SHA256:AA"), ("nas.example.test", 5001, "SHA256:BB"));
        TrustedWebCertificatesSettingsViewModel vm = await CreateAsync(trust, confirm: true);
        vm.Refresh();

        vm.SearchText = "nas";
        Assert.Equal(["nas.example.test:5001"], vm.Rows.Select(row => row.Endpoint));

        vm.SearchText = "nothing-matches";
        Assert.Empty(vm.Rows);
        Assert.False(vm.IsEmptyStateVisible);
    }

    [Fact]
    public async Task Forgetting_RemovesThePinAndTellsTheOpenTabs()
    {
        WebCertificateTrust trust = TrustWith(("ilo.example.test", 443, "SHA256:AA"));
        var removedKeys = new List<string>();
        trust.CertificateRemoved += removedKeys.Add;
        var revoked = new List<string>();
        TrustedWebCertificatesSettingsViewModel vm = await CreateAsync(trust, confirm: true, revoke: key =>
        {
            revoked.Add(key);
            trust.Remove(key);
            return Task.CompletedTask;
        });
        vm.Refresh();

        await vm.ForgetCommand.ExecuteAsync(vm.Rows.Single());

        Assert.Equal(["ilo.example.test:443"], revoked);
        Assert.Equal(["ilo.example.test:443"], removedKeys);
        Assert.Empty(vm.Rows);
        Assert.Equal(WebCertificateVerdict.Unknown, trust.Evaluate("ilo.example.test", 443, "SHA256:AA"));
        Assert.Contains("ilo.example.test:443", vm.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Declining_KeepsThePin()
    {
        WebCertificateTrust trust = TrustWith(("ilo.example.test", 443, "SHA256:AA"));
        int revokes = 0;
        TrustedWebCertificatesSettingsViewModel vm = await CreateAsync(trust, confirm: false, revoke: _ => { revokes++; return Task.CompletedTask; });
        vm.Refresh();

        await vm.ForgetCommand.ExecuteAsync(vm.Rows.Single());

        Assert.Equal(0, revokes);
        Assert.Single(vm.Rows);
    }

    /// <summary>A save that fails keeps the row, the pin and an error to retry from.</summary>
    [Fact]
    public async Task AFailedSave_KeepsTheRowAndSaysSo()
    {
        WebCertificateTrust trust = TrustWith(("ilo.example.test", 443, "SHA256:AA"));
        TrustedWebCertificatesSettingsViewModel vm = await CreateAsync(
            trust,
            confirm: true,
            revoke: _ => throw new IOException("settings.json is locked"));
        vm.Refresh();

        await vm.ForgetCommand.ExecuteAsync(vm.Rows.Single());

        Assert.Single(vm.Rows);
        Assert.Equal(WebCertificateVerdict.Trusted, trust.Evaluate("ilo.example.test", 443, "SHA256:AA"));
        Assert.Contains("settings.json is locked", vm.StatusMessage, StringComparison.Ordinal);
        Assert.Contains("could not be saved", vm.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AConfirmationThatCannotShow_IsNotAYes()
    {
        WebCertificateTrust trust = TrustWith(("ilo.example.test", 443, "SHA256:AA"));
        int revokes = 0;
        TrustedWebCertificatesSettingsViewModel vm = await CreateAsync(
            trust,
            confirm: null,
            revoke: _ => { revokes++; return Task.CompletedTask; });
        vm.Refresh();

        await vm.ForgetCommand.ExecuteAsync(vm.Rows.Single());

        Assert.Equal(0, revokes);
        Assert.Single(vm.Rows);
        Assert.Contains("confirmation could not be shown", vm.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task APinTrustedWhileThePanelIsOpen_AppearsWithoutARefresh()
    {
        var trust = new WebCertificateTrust();
        TrustedWebCertificatesSettingsViewModel vm = await CreateAsync(trust, confirm: true);
        vm.Refresh();

        trust.Apply("ilo.example.test", 443, Entry("SHA256:AA"), FtpsCertificateDecision.Accept);

        Assert.Equal(["ilo.example.test:443"], vm.Rows.Select(row => row.Endpoint));
    }

    [Theory]
    [InlineData("ilo.example.test:443", "ilo.example.test", 443)]
    [InlineData("[fe80::1]:8443", "fe80::1", 8443)]
    public void AStoreKey_SplitsBackIntoHostAndPort(string key, string host, int port)
    {
        Assert.True(WebCertificateTrust.TryParseKey(key, out string parsedHost, out int parsedPort));
        Assert.Equal(host, parsedHost);
        Assert.Equal(port, parsedPort);
    }

    [Theory]
    [InlineData("")]
    [InlineData("no-port")]
    [InlineData("host:0")]
    [InlineData("host:99999")]
    [InlineData(":443")]
    public void AMalformedKey_IsRefused(string key)
    {
        Assert.False(WebCertificateTrust.TryParseKey(key, out _, out _));
    }

    [Fact]
    public void RemovingAnIpv6Pin_ReachesTheStoredEntry()
    {
        WebCertificateTrust trust = TrustWith(("fe80::1", 8443, "SHA256:BB"));

        Assert.True(trust.Remove("[fe80::1]:8443"));
        Assert.Equal(WebCertificateVerdict.Unknown, trust.Evaluate("fe80::1", 8443, "SHA256:BB"));
    }

    private static async Task<TrustedWebCertificatesSettingsViewModel> CreateAsync(
        WebCertificateTrust trust,
        bool? confirm,
        Func<string, Task>? revoke = null)
    {
        var localizer = new LocalizationManager();
        await localizer.LoadAsync(Path.Combine(AppContext.BaseDirectory, "locales"), "en");
        return new TrustedWebCertificatesSettingsViewModel(
            trust,
            localizer,
            ConfirmOnlyDialog.Create(confirm),
            new FakeUiDispatcher(),
            revoke ?? (key => { trust.Remove(key); return Task.CompletedTask; }));
    }

    private static WebCertificateTrust TrustWith(params (string Host, int Port, string Fingerprint)[] pins)
    {
        var trust = new WebCertificateTrust();
        foreach ((string host, int port, string fingerprint) in pins)
        {
            trust.Apply(host, port, Entry(fingerprint), FtpsCertificateDecision.Accept);
        }

        return trust;
    }

    private static FtpsCertificateEntry Entry(string fingerprint) => new(
        fingerprint,
        DateTimeOffset.UtcNow,
        DateTimeOffset.UtcNow,
        "CN=ilo",
        "CN=ilo",
        DateTimeOffset.UtcNow.AddDays(-1),
        DateTimeOffset.UtcNow.AddYears(1),
        FtpsCertificateSource.UserConfirmed);

    /// <summary>
    /// A dialog service that only answers confirmations: true, false, or a throw for null. Any
    /// other call fails the test, since this panel has no business making it.
    /// </summary>
    public class ConfirmOnlyDialog : DispatchProxy
    {
        private bool? _answer;

        internal static IDialogService Create(bool? answer)
        {
            IDialogService proxy = Create<IDialogService, ConfirmOnlyDialog>();
            ((ConfirmOnlyDialog)(object)proxy)._answer = answer;
            return proxy;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == nameof(IDialogService.ShowConfirmAsync))
            {
                return _answer is { } answer
                    ? Task.FromResult(answer)
                    : throw new InvalidOperationException("no dialog owner");
            }

            throw new InvalidOperationException($"Unexpected dialog call: {targetMethod?.Name}");
        }
    }
}
