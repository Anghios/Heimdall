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

using System.Security.Cryptography.X509Certificates;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Heimdall.App.Services;
using Heimdall.App.ViewModels;
using Heimdall.App.ViewModels.Dialogs;
using Heimdall.Core.Certificates;
using Heimdall.Core.Localization;
using Heimdall.Core.Logging;
using Heimdall.Core.Models;
using Microsoft.Web.WebView2.Core;

namespace Heimdall.App.Views;

/// <summary>
/// A web console in a session tab, rendered by WebView2 in the browser profile all web tabs
/// share (see <see cref="WebView2Helper.GetWebConsoleEnvironmentAsync"/>).
/// </summary>
public partial class EmbeddedWebView : UserControl, IDisposable, ISessionPaneOwner
{
    private readonly HashSet<string> _answeredChallenges = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Task<FtpsCertificateDecision>> _pendingCertificatePrompts =
        new(StringComparer.Ordinal);
    private readonly HashSet<string> _rejectedCertificates = new(StringComparer.Ordinal);

    private WebSessionResult? _session;
    private SessionTabViewModel? _sessionTab;
    private LocalizationManager? _localizer;
    private SessionPaneModel? _ownerPane;
    private bool _connectedRaised;
    private bool _disposed;

    /// <summary>Raised when the console's first page has loaded. Parameter: ServerId.</summary>
    public event Action<string>? SessionConnected;

    /// <summary>Raised when the console cannot be shown. Parameters: ServerId, message.</summary>
    public event Action<string, string>? SessionError;

    /// <summary>Raised to request a split pane. Parameter: SessionTab.</summary>
    public event Action<SessionTabViewModel?>? RequestSplit;

    /// <summary>The pins the user gave to consoles the browser refused; set by the session manager.</summary>
    public WebCertificateTrust? CertificateTrust { get; set; }

    /// <summary>The pane this view lives in, wherever a split, a merge or a detach moved it.</summary>
    internal SessionPaneModel? OwningPane => _ownerPane;

    public EmbeddedWebView()
    {
        InitializeComponent();

        if (TryFindResource("BackgroundColor") is System.Windows.Media.Color themeColor)
        {
            ConsoleWebView.DefaultBackgroundColor =
                System.Drawing.Color.FromArgb(themeColor.A, themeColor.R, themeColor.G, themeColor.B);
        }
    }

    public void SetOwningPane(SessionPaneModel pane)
    {
        ArgumentNullException.ThrowIfNull(pane);
        _ownerPane = pane;
    }

    /// <summary>Opens the console's address in this view.</summary>
    public async Task InitializeSessionAsync(
        WebSessionResult session,
        SessionTabViewModel sessionTab,
        string displayName,
        LocalizationManager? localizer)
    {
        _session = session;
        _sessionTab = sessionTab;
        _localizer = localizer;

        SessionTitleText.Text = displayName;
        AddressTextBlock.Text = session.Address.AbsoluteUri;
        StatusTextBlock.Text = Text("StatusWebConnecting");

        ReloadButton.ToolTip = Text("TooltipWebReload");
        BackButton.ToolTip = Text("TooltipWebBack");
        SplitButton.ToolTip = Text("TooltipSplitSession");
        FallbackRetryButton.Content = Text("BtnWebRetry");
        FallbackTitleText.Text = Text("WebFallbackTitle");
        System.Windows.Automation.AutomationProperties.SetName(ReloadButton, Text("TooltipWebReload"));
        System.Windows.Automation.AutomationProperties.SetName(BackButton, Text("TooltipWebBack"));
        System.Windows.Automation.AutomationProperties.SetName(SplitButton, Text("A11ySplitSession"));
        System.Windows.Automation.AutomationProperties.SetName(FallbackRetryButton, Text("BtnWebRetry"));
        System.Windows.Automation.AutomationProperties.SetName(StatusTextBlock, Text("A11yConnectionStatus"));

        await InitializeWebViewAsync();
    }

    private async Task InitializeWebViewAsync()
    {
        try
        {
            if (!WebView2Helper.IsAvailable)
            {
                ReportFailure(Text("ErrorWebView2NotFound"));
                return;
            }

            CoreWebView2Environment environment = await WebView2Helper.GetWebConsoleEnvironmentAsync();
            await ConsoleWebView.EnsureCoreWebView2Async(environment);
            if (_disposed || _session is null)
            {
                return;
            }

            CoreWebView2 core = ConsoleWebView.CoreWebView2;

            // A console is a web application the user operates: copy and paste, find and zoom
            // stay. Developer tools stay off, as in every embedded view.
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.IsPasswordAutosaveEnabled = false;
            core.Settings.IsGeneralAutofillEnabled = false;

            core.NavigationStarting += OnNavigationStarting;
            core.NavigationCompleted += OnNavigationCompleted;
            core.HistoryChanged += OnHistoryChanged;
            core.ServerCertificateErrorDetected += OnServerCertificateErrorDetected;
            core.BasicAuthenticationRequested += OnBasicAuthenticationRequested;

            core.Navigate(_session.Address.AbsoluteUri);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            FileLogger.Error($"Web console init failed for {_session?.ServerId}: {ex.Message}");
            ReportFailure(ex.Message);
        }
    }

    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (WebConsolePolicy.IsNavigable(e.Uri))
        {
            LoadingBar.Visibility = Visibility.Visible;
            return;
        }

        e.Cancel = true;
        FileLogger.Warn("Web console blocked a navigation to a non-web address.");
    }

    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        LoadingBar.Visibility = Visibility.Collapsed;
        if (_session is null)
        {
            return;
        }

        if (e.IsSuccess)
        {
            AddressTextBlock.Text = ConsoleWebView.CoreWebView2?.Source ?? AddressTextBlock.Text;
            StatusTextBlock.Text = Text("StatusWebConnected");
            if (!_connectedRaised)
            {
                _connectedRaised = true;
                SessionConnected?.Invoke(_session.ServerId);
            }

            return;
        }

        // A navigation the user or a newer navigation cancelled is not a failure.
        if (e.WebErrorStatus == CoreWebView2WebErrorStatus.OperationCanceled)
        {
            return;
        }

        string reason = _localizer?.Format("ErrorWebNavigationFailed", e.WebErrorStatus.ToString())
            ?? e.WebErrorStatus.ToString();
        FileLogger.Warn($"Web console {_session.ServerId} navigation failed: {e.WebErrorStatus}.");
        ReportFailure(reason);
    }

    private void OnHistoryChanged(object? sender, object e)
    {
        BackButton.IsEnabled = ConsoleWebView.CoreWebView2?.CanGoBack == true;
    }

    private void OnBasicAuthenticationRequested(object? sender, CoreWebView2BasicAuthenticationRequestedEventArgs e)
    {
        if (_session is null || !Uri.TryCreate(e.Uri, UriKind.Absolute, out Uri? challengeUri))
        {
            return;
        }

        string key = WebConsolePolicy.ChallengeKey(challengeUri);
        if (!WebConsolePolicy.ShouldAnswerChallenge(
                _session.Username,
                _answeredChallenges.Contains(key),
                challengeUri,
                _session.Address))
        {
            return;
        }

        _answeredChallenges.Add(key);
        e.Response.UserName = _session.Username;
        e.Response.Password = _session.Password ?? string.Empty;
    }

    private async void OnServerCertificateErrorDetected(
        object? sender,
        CoreWebView2ServerCertificateErrorDetectedEventArgs e)
    {
        CoreWebView2Deferral deferral = e.GetDeferral();
        try
        {
            e.Action = await DecideCertificateAsync(e);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            FileLogger.Warn($"Web console certificate check failed: {ex.Message}");
            e.Action = CoreWebView2ServerCertificateErrorAction.Cancel;
        }
        finally
        {
            deferral.Complete();
        }
    }

    private async Task<CoreWebView2ServerCertificateErrorAction> DecideCertificateAsync(
        CoreWebView2ServerCertificateErrorDetectedEventArgs e)
    {
        if (CertificateTrust is null
            || !Uri.TryCreate(e.RequestUri, UriKind.Absolute, out Uri? requestUri))
        {
            return CoreWebView2ServerCertificateErrorAction.Cancel;
        }

        using X509Certificate2 certificate = e.ServerCertificate.ToX509Certificate2();
        string fingerprint = CertificateFingerprint.ComputeSha256(certificate);
        string host = requestUri.Host;
        int port = requestUri.Port;
        string promptKey = $"{host.ToLowerInvariant()}:{port}|{fingerprint}";

        if (_rejectedCertificates.Contains(promptKey))
        {
            return CoreWebView2ServerCertificateErrorAction.Cancel;
        }

        WebCertificateVerdict verdict = CertificateTrust.Evaluate(host, port, fingerprint);
        if (verdict == WebCertificateVerdict.Trusted)
        {
            return CoreWebView2ServerCertificateErrorAction.AlwaysAllow;
        }

        // A page loads many resources from one host; every one of them reports the same
        // certificate, and the user must be asked once, not once per resource.
        if (!_pendingCertificatePrompts.TryGetValue(promptKey, out Task<FtpsCertificateDecision>? pending))
        {
            var prompt = new FtpsCertificatePrompt(
                host,
                port,
                fingerprint,
                certificate.Subject,
                certificate.Issuer,
                new DateTimeOffset(certificate.NotBefore),
                new DateTimeOffset(certificate.NotAfter),
                e.ErrorStatus.ToString());
            CertificatePromptWording wording = verdict == WebCertificateVerdict.Changed
                ? CertificatePromptWording.WebChanged
                : CertificatePromptWording.WebFirstUse;
            pending = AskAsync(prompt, wording);
            _pendingCertificatePrompts[promptKey] = pending;
        }

        FtpsCertificateDecision decision;
        try
        {
            decision = await pending;
        }
        finally
        {
            _pendingCertificatePrompts.Remove(promptKey);
        }

        if (decision == FtpsCertificateDecision.Reject)
        {
            _rejectedCertificates.Add(promptKey);
            return CoreWebView2ServerCertificateErrorAction.Cancel;
        }

        var entry = new FtpsCertificateEntry(
            fingerprint,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            certificate.Subject,
            certificate.Issuer,
            new DateTimeOffset(certificate.NotBefore),
            new DateTimeOffset(certificate.NotAfter),
            FtpsCertificateSource.UserConfirmed)
        {
            ValidationErrors = e.ErrorStatus.ToString(),
        };
        CertificateTrust.Apply(host, port, entry, decision);
        return CoreWebView2ServerCertificateErrorAction.AlwaysAllow;
    }

    private async Task<FtpsCertificateDecision> AskAsync(FtpsCertificatePrompt prompt, CertificatePromptWording wording)
    {
        // Yield first, so the dialog's nested message loop does not run inside the browser's
        // event callback.
        await Dispatcher.Yield(DispatcherPriority.Background);
        if (_disposed || Application.Current is not { } app || _localizer is null)
        {
            return FtpsCertificateDecision.Reject;
        }

        return DialogFtpsCertificateVerifier.ShowDialog(app, _localizer, prompt, wording, CancellationToken.None);
    }

    private void ReportFailure(string message)
    {
        Dispatcher.Invoke(() =>
        {
            LoadingBar.Visibility = Visibility.Collapsed;
            StatusTextBlock.Text = message;
            FallbackMessageText.Text = message;
            FallbackPanel.Visibility = Visibility.Visible;

            // Airspace: the browser's window covers any WPF sibling, so it steps aside.
            ConsoleWebView.Visibility = Visibility.Collapsed;
        });

        if (_session?.ServerId is { } serverId)
        {
            SessionError?.Invoke(serverId, message);
        }
    }

    private void OnReloadClick(object sender, RoutedEventArgs e)
    {
        if (_disposed || _session is null)
        {
            return;
        }

        FallbackPanel.Visibility = Visibility.Collapsed;
        ConsoleWebView.Visibility = Visibility.Visible;
        _answeredChallenges.Clear();
        _rejectedCertificates.Clear();

        if (ConsoleWebView.CoreWebView2 is not { } core)
        {
            _ = InitializeWebViewAsync();
            return;
        }

        StatusTextBlock.Text = Text("StatusWebConnecting");

        // After a failure the browser still shows the error page of the address that failed;
        // reloading it would retry the error page, so the console's own address is opened again.
        if (string.Equals(core.Source, "about:blank", StringComparison.Ordinal) || !_connectedRaised)
        {
            core.Navigate(_session.Address.AbsoluteUri);
        }
        else
        {
            core.Reload();
        }
    }

    private void OnBackClick(object sender, RoutedEventArgs e)
    {
        if (ConsoleWebView.CoreWebView2 is { CanGoBack: true } core)
        {
            core.GoBack();
        }
    }

    private void OnSplitClick(object sender, RoutedEventArgs e) => RequestSplit?.Invoke(_sessionTab);

    private string Text(string key) => _localizer?[key] ?? key;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (ConsoleWebView.CoreWebView2 is { } core)
        {
            core.NavigationStarting -= OnNavigationStarting;
            core.NavigationCompleted -= OnNavigationCompleted;
            core.HistoryChanged -= OnHistoryChanged;
            core.ServerCertificateErrorDetected -= OnServerCertificateErrorDetected;
            core.BasicAuthenticationRequested -= OnBasicAuthenticationRequested;
        }

        try
        {
            ConsoleWebView.Dispose();
        }
        catch (InvalidOperationException)
        {
            // WebView2 may already be disposed.
        }
    }
}
