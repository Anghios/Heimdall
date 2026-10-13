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

using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Heimdall.App.Services;
using Heimdall.Core.Certificates;
using Heimdall.Core.Configuration;
using Heimdall.Core.Localization;
using Heimdall.Core.Logging;

namespace Heimdall.App.ViewModels.Settings;

/// <summary>
/// Settings panel listing the certificates trusted for web consoles, and forgetting one.
/// </summary>
/// <remarks>
/// <para>
/// The missing half of <see cref="WebCertificateTrust"/>: "Trust certificate" on a console's
/// prompt is a durable decision, and without this screen the only way back from one given by
/// reflex, or for a console since re-imaged, was editing settings.json by hand. It mirrors the
/// RDP certificate panel, minus the profile names: a web pin belongs to an address, not to a
/// profile, so the address is what the row shows.
/// </para>
/// <para>
/// Forgetting writes the settings first and only then drops the pin from memory, so a failed
/// write leaves the row on screen, the pin in force and an error to retry from, instead of a
/// screen claiming a removal the next start would undo.
/// </para>
/// </remarks>
public sealed partial class TrustedWebCertificatesSettingsViewModel : ObservableObject, IDisposable
{
    /// <summary>Visual severity passed to the confirmation: forgetting is destructive.</summary>
    private const string ConfirmSeverityWarning = "warning";

    /// <summary>Characters of a fingerprint shown in the grid before it is elided.</summary>
    private const int FingerprintDisplayLength = 26;

    private readonly WebCertificateTrust _trust;
    private readonly LocalizationManager _localizer;
    private readonly IDialogService _dialogService;
    private readonly IUiDispatcher _dispatcher;
    private readonly Func<string, Task> _revoke;
    private readonly List<TrustedWebCertificateRowViewModel> _allRows = [];
    private bool _disposed;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private TrustedWebCertificateRowViewModel? _selectedRow;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatusMessage))]
    private string _statusMessage = string.Empty;

    /// <summary>Initializes a new instance for the running application.</summary>
    public TrustedWebCertificatesSettingsViewModel(
        WebCertificateTrust trust,
        IConfigManager configManager,
        LocalizationManager localizer,
        IDialogService dialogService,
        IUiDispatcher dispatcher)
        : this(
            trust,
            localizer,
            dialogService,
            dispatcher,
            async key =>
            {
                await configManager.MergeSettingAsync(
                    settings => settings.TrustedWebCertificates.Remove(key)).ConfigureAwait(true);
                trust.Remove(key);
            })
    {
    }

    /// <summary>Initializes a new instance with an arbitrary revocation, for tests.</summary>
    internal TrustedWebCertificatesSettingsViewModel(
        WebCertificateTrust trust,
        LocalizationManager localizer,
        IDialogService dialogService,
        IUiDispatcher dispatcher,
        Func<string, Task> revoke)
    {
        _trust = trust;
        _localizer = localizer;
        _dialogService = dialogService;
        _dispatcher = dispatcher;
        _revoke = revoke;

        _trust.CertificateTrusted += OnTrustChanged;
        _trust.CertificateRemoved += OnCertificateRemoved;
        _localizer.LocaleChanged += OnLocaleChanged;
    }

    /// <summary>The rows the grid shows, after the search box has been applied.</summary>
    public ObservableCollection<TrustedWebCertificateRowViewModel> Rows { get; } = [];

    /// <summary>Whether any certificate is trusted at all, search box ignored.</summary>
    public bool HasRows => _allRows.Count > 0;

    /// <summary>Whether any row survives the search box.</summary>
    public bool HasVisibleRows => Rows.Count > 0;

    /// <summary>
    /// Whether the "nothing is trusted" panel replaces the grid. From the unfiltered set: a
    /// search that matches nothing is an empty grid, not an invitation to go and connect.
    /// </summary>
    public bool IsEmptyStateVisible => !HasRows;

    /// <summary>Whether the status line under the grid has anything to say.</summary>
    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    /// <summary>Rebuilds every row from the trust store.</summary>
    [RelayCommand]
    public void Refresh()
    {
        RebuildRows();
        StatusMessage = string.Empty;
    }

    /// <summary>Forgets one certificate, once the user has confirmed it.</summary>
    /// <remarks>
    /// The next visit to that console asks again, from any tab: the open tabs drop what the
    /// browser had accepted when they hear of the removal.
    /// </remarks>
    [RelayCommand]
    private async Task ForgetAsync(TrustedWebCertificateRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        bool confirmed;
        try
        {
            confirmed = await _dialogService.ShowConfirmAsync(
                _localizer["DialogTrustedWebCertificateForgetTitle"],
                _localizer.Format("DialogTrustedWebCertificateForgetMessage", row.Endpoint, row.Fingerprint),
                ConfirmSeverityWarning,
                _localizer["DialogTrustedWebCertificateForgetConfirm"],
                _localizer["DialogTrustedWebCertificateForgetDecline"]).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            // A confirmation that could not be shown is not a yes.
            FileLogger.Warn($"Trusted web certificates: the confirmation could not be shown, so {row.Endpoint} was kept: {ex.Message}");
            StatusMessage = _localizer.Format("ToastTrustedWebCertificateForgetFailed", ex.Message);
            return;
        }

        if (!confirmed)
        {
            return;
        }

        try
        {
            await _revoke(row.Key).ConfigureAwait(true);
            RebuildRows();
            StatusMessage = _localizer.Format("ToastTrustedWebCertificateForgotten", row.Endpoint);
        }
        catch (Exception ex)
        {
            FileLogger.Warn($"Web certificate revocation was not saved: {ex.Message}");
            StatusMessage = _localizer.Format("ToastTrustedWebCertificateSaveFailed", ex.Message);
        }
    }

    /// <summary>Forgets the row the grid has selected, so Delete can be bound to it.</summary>
    [RelayCommand]
    private Task ForgetSelectedAsync() => ForgetAsync(SelectedRow);

    private void RebuildRows()
    {
        _allRows.Clear();
        foreach ((string key, FtpsCertificateEntry entry) in _trust.GetAllEntries())
        {
            _allRows.Add(new TrustedWebCertificateRowViewModel(
                key,
                entry,
                Describe(entry.Subject),
                Describe(entry.Issuer),
                entry.FirstSeen.ToLocalTime().ToString("g", CultureInfo.CurrentCulture),
                entry.LastSeen.ToLocalTime().ToString("g", CultureInfo.CurrentCulture),
                FingerprintDisplayLength));
        }

        _allRows.Sort(static (left, right) =>
            string.Compare(left.Endpoint, right.Endpoint, StringComparison.OrdinalIgnoreCase));
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        IEnumerable<TrustedWebCertificateRowViewModel> visible = string.IsNullOrWhiteSpace(SearchText)
            ? _allRows
            : _allRows.Where(row => row.Matches(SearchText));

        Rows.Clear();
        foreach (TrustedWebCertificateRowViewModel row in visible)
        {
            Rows.Add(row);
        }

        OnPropertyChanged(nameof(HasRows));
        OnPropertyChanged(nameof(HasVisibleRows));
        OnPropertyChanged(nameof(IsEmptyStateVisible));
    }

    private string Describe(string? value)
        => string.IsNullOrWhiteSpace(value) || value == "(unknown)"
            ? _localizer["LblTrustedRdpCertificateFieldUnknown"]
            : value;

    /// <summary>A console trusted from a tab while the panel is open appears without a refresh.</summary>
    private void OnTrustChanged(string key, FtpsCertificateEntry entry) => _dispatcher.Invoke(RebuildRows);

    private void OnCertificateRemoved(string key) => _dispatcher.Invoke(RebuildRows);

    private void OnLocaleChanged(string locale) => _dispatcher.Invoke(RebuildRows);

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _trust.CertificateTrusted -= OnTrustChanged;
        _trust.CertificateRemoved -= OnCertificateRemoved;
        _localizer.LocaleChanged -= OnLocaleChanged;
    }
}

/// <summary>One trusted web console certificate, as the settings grid shows it.</summary>
public sealed class TrustedWebCertificateRowViewModel
{
    internal TrustedWebCertificateRowViewModel(
        string key,
        FtpsCertificateEntry entry,
        string subjectDisplay,
        string issuerDisplay,
        string firstSeenDisplay,
        string lastSeenDisplay,
        int fingerprintDisplayLength)
    {
        Key = key;
        Endpoint = key;
        Entry = entry;
        Fingerprint = entry.Fingerprint;
        FingerprintDisplay = entry.Fingerprint.Length <= fingerprintDisplayLength
            ? entry.Fingerprint
            : entry.Fingerprint[..fingerprintDisplayLength] + "...";
        SubjectDisplay = subjectDisplay;
        IssuerDisplay = issuerDisplay;
        FirstSeen = entry.FirstSeen;
        FirstSeenDisplay = firstSeenDisplay;
        LastSeen = entry.LastSeen;
        LastSeenDisplay = lastSeenDisplay;
    }

    /// <summary>The store key, "host:port"; what a revocation must name.</summary>
    public string Key { get; }

    /// <summary>The console's address as the user typed it in the profile, with its port.</summary>
    public string Endpoint { get; }

    /// <summary>The stored entry, kept whole.</summary>
    public FtpsCertificateEntry Entry { get; }

    /// <summary>The full fingerprint, which identifies the certificate.</summary>
    public string Fingerprint { get; }

    /// <summary>The fingerprint as the grid shows it, elided to fit its column.</summary>
    public string FingerprintDisplay { get; }

    public string SubjectDisplay { get; }

    public string IssuerDisplay { get; }

    /// <summary>When the user first trusted this certificate.</summary>
    public DateTimeOffset FirstSeen { get; }

    public string FirstSeenDisplay { get; }

    /// <summary>When a console last presented it.</summary>
    public DateTimeOffset LastSeen { get; }

    public string LastSeenDisplay { get; }

    /// <summary>Whether the search box's text appears anywhere a user would look for it.</summary>
    internal bool Matches(string text)
        => Endpoint.Contains(text, StringComparison.CurrentCultureIgnoreCase)
            || Fingerprint.Contains(text, StringComparison.CurrentCultureIgnoreCase)
            || SubjectDisplay.Contains(text, StringComparison.CurrentCultureIgnoreCase)
            || IssuerDisplay.Contains(text, StringComparison.CurrentCultureIgnoreCase);
}
